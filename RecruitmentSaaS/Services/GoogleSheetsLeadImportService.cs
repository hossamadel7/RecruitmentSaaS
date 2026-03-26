using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Text.Json;

namespace RecruitmentSaaS.Services
{
    public interface IGoogleSheetsLeadImportService
    {
        Task ImportAllAsync();
        Task<ImportResult> ImportSheetAsync(Guid sheetId);
    }

    public class ImportResult
    {
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public int Duplicates { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class GoogleSheetsLeadImportService : IGoogleSheetsLeadImportService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<GoogleSheetsLeadImportService> _logger;
        private readonly HttpClient _http;

        private static readonly Guid DefaultBranchId = new("00000000-0000-0000-0000-000000000020");
        private static readonly Guid SystemUserId = new("00000000-0000-0000-0000-000000000030");

        public GoogleSheetsLeadImportService(
            IServiceScopeFactory scopeFactory,
            IConfiguration config,
            ILogger<GoogleSheetsLeadImportService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _scopeFactory = scopeFactory;
            _config = config;
            _logger = logger;
            _http = httpClientFactory.CreateClient();
        }

        // ── Run all active sheets ─────────────────────────────────────────────
        public async Task ImportAllAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<RecruitmentCrmContext>();

            var sheets = await context.SalesGoogleSheets
                .Where(s => s.IsActive)
                .ToListAsync();

            foreach (var sheet in sheets)
            {
                try
                {
                    await ImportSheetAsync(sheet.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to import sheet {SheetId}", sheet.Id);
                }
            }
        }

        // ── Import a single sheet ─────────────────────────────────────────────
        public async Task<ImportResult> ImportSheetAsync(Guid sheetId)
        {
            var result = new ImportResult();

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<RecruitmentCrmContext>();
            var apiKey = _config["Google:ApiKey"];

            var sheet = await context.SalesGoogleSheets
                .FirstOrDefaultAsync(s => s.Id == sheetId);

            if (sheet == null) return result;

            // ── Fetch header row first to map columns dynamically ─────────────
            var headerRange = Uri.EscapeDataString($"{sheet.SheetName}!A1:Z1");
            var headerUrl = $"https://sheets.googleapis.com/v4/spreadsheets/{sheet.SpreadsheetId}/values/{headerRange}?key={apiKey}";
            var headerResp = await _http.GetAsync(headerUrl);

            if (!headerResp.IsSuccessStatusCode)
            {
                result.Errors.Add($"Could not read sheet headers: {await headerResp.Content.ReadAsStringAsync()}");
                return result;
            }

            var headerJson = await headerResp.Content.ReadAsStringAsync();
            var headerDoc = JsonDocument.Parse(headerJson);

            var headers = new List<string>();
            if (headerDoc.RootElement.TryGetProperty("values", out var hv) && hv.GetArrayLength() > 0)
                headers = hv[0].EnumerateArray()
                    .Select(h => h.GetString()?.ToLower().Trim() ?? "")
                    .ToList();

            // Find column index by any of the given header names
            int Col(params string[] names)
            {
                foreach (var n in names)
                {
                    var idx = headers.IndexOf(n);
                    if (idx >= 0) return idx;
                }
                return -1;
            }

            // Map columns — handles Facebook export naming + Arabic headers
            var colName = Col("full_name", "name", "الاسم الكامل", "الاسم");
            var colPhone = Col("phone_number", "phone", "mobile", "الهاتف", "رقم الهاتف");
            var colNotes = Col("any_notes", "notes", "ملاحظات");
            var colJobTitle = Col("job_title", "interested_job", "المسمى الوظيفي");
            var colCountry = Col("country", "interested_country", "الدولة");
            var colCampaignName = Col("campaign_name", "campaign", "الحملة");

            // ── Fetch data rows starting after last imported row ───────────────
            var startRow = sheet.LastImportedRow + 1;
            var range = Uri.EscapeDataString($"{sheet.SheetName}!A{startRow}:Z5000");
            var url = $"https://sheets.googleapis.com/v4/spreadsheets/{sheet.SpreadsheetId}/values/{range}?key={apiKey}";
            var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                result.Errors.Add($"Google Sheets API error: {await response.Content.ReadAsStringAsync()}");
                return result;
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonDocument.Parse(json);

            if (!data.RootElement.TryGetProperty("values", out var values))
                return result; // no new rows

            var rows = values.EnumerateArray().ToList();
            if (!rows.Any()) return result;

            // ── Process each row ──────────────────────────────────────────────
            int lastRow = sheet.LastImportedRow;

            foreach (var row in rows)
            {
                lastRow++;
                var cells = row.EnumerateArray()
                    .Select(c => c.GetString()?.Trim() ?? "")
                    .ToList();

                string Get(int idx) => idx >= 0 && idx < cells.Count ? cells[idx] : "";

                var name = Get(colName);
                var phone = NormalizePhone(Get(colPhone));

                // Skip empty rows
                if (string.IsNullOrWhiteSpace(phone) && string.IsNullOrWhiteSpace(name))
                {
                    result.Skipped++;
                    continue;
                }

                // Skip test leads
                if (name.ToLower().Contains("test") || phone == "01000000000")
                {
                    result.Skipped++;
                    continue;
                }

                // Duplicate check by phone
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    var exists = await context.Leads.AnyAsync(l => l.Phone == phone);
                    if (exists)
                    {
                        result.Duplicates++;
                        continue;
                    }
                }

                // ── Try to match campaign by name from sheet row ──────────────
                Guid? campaignId = sheet.CampaignId; // default from sheet config
                var campaignName = Get(colCampaignName);
                if (!string.IsNullOrWhiteSpace(campaignName))
                {
                    var matched = await context.Campaigns
                        .FirstOrDefaultAsync(c => c.Name == campaignName);
                    if (matched != null)
                        campaignId = matched.Id;
                }

                // ── Create lead ───────────────────────────────────────────────
                var lead = new Lead
                {
                    Id = Guid.NewGuid(),
                    FullName = name,
                    Phone = phone,
                    Notes = Get(colNotes),
                    InterestedJobTitle = Get(colJobTitle),
                    InterestedCountry = Get(colCountry),
                    Status = 1,         // New
                    LeadSource = 1,         // Facebook
                    CampaignId = campaignId,
                    GoogleSheetId = sheet.Id,
                    AssignedSalesId = null,      // goes to Pool
                    IsConverted = false,
                    IsDuplicate = false,
                    CreatedAt = DateTime.UtcNow,
                    BranchId = DefaultBranchId,
                    RegisteredById = SystemUserId,
                };

                context.Leads.Add(lead);

                // Funnel history entry
                context.LeadFunnelHistories.Add(new LeadFunnelHistory
                {
                    Id = Guid.NewGuid(),
                    LeadId = lead.Id,
                    FromStatus = null,
                    ToStatus = 1,
                    ChangedById = SystemUserId,
                    CreatedAt = DateTime.UtcNow,
                });

                result.Imported++;
            }

            // ── Save progress back to sheet record ────────────────────────────
            sheet.LastImportedRow = lastRow;
            sheet.LastImportedAt = DateTime.UtcNow;
            sheet.TotalImported += result.Imported;

            await context.SaveChangesAsync();

            _logger.LogInformation(
                "Sheet [{Name}] — imported: {I}, skipped: {S}, duplicates: {D}",
                sheet.Name, result.Imported, result.Skipped, result.Duplicates);

            return result;
        }

        // ── Normalize Egyptian phone numbers ──────────────────────────────────
        private static string NormalizePhone(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            var digits = new string(raw.Where(char.IsDigit).ToArray());

            // +201xxxxxxxxx or 201xxxxxxxxx → 01xxxxxxxxx
            if (digits.StartsWith("20") && digits.Length == 12)
                digits = "0" + digits[2..];

            // 2010/2011/2012/2015 prefix edge case
            if ((digits.StartsWith("2010") || digits.StartsWith("2011") ||
                 digits.StartsWith("2012") || digits.StartsWith("2015")) && digits.Length == 11)
                digits = "0" + digits[1..];

            return digits;
        }
    }

    // ── Background service — runs every 30 minutes ────────────────────────────
    public class GoogleSheetsImportBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<GoogleSheetsImportBackgroundService> _logger;

        public GoogleSheetsImportBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<GoogleSheetsImportBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Google Sheets import background service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider
                        .GetRequiredService<IGoogleSheetsLeadImportService>();

                    _logger.LogInformation("Running scheduled Google Sheets import...");
                    await service.ImportAllAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scheduled Google Sheets import failed");
                }

                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }
}