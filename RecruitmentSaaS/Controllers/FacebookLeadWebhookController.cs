using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace RecruitmentSaaS.Controllers
{
    [Route("api/webhooks/facebooklead")]
    [ApiController]
    public class FacebookLeadWebhookController : ControllerBase
    {
        private readonly RecruitmentCrmContext _context;
        private readonly IConfiguration _config;
        private readonly ILogger<FacebookLeadWebhookController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _verifyToken;
        private readonly string _pageAccessToken;
        private readonly Guid _defaultBranchId;
        private readonly Guid _systemUserId;

        public FacebookLeadWebhookController(
            RecruitmentCrmContext context,
            IConfiguration config,
            ILogger<FacebookLeadWebhookController> logger,
            IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _config = config;
            _logger = logger;
            _httpClientFactory = httpClientFactory;

            _verifyToken = _config["Facebook:VerifyToken"] ?? "1234";
            _pageAccessToken = _config["Facebook:PageAccessToken"] ?? string.Empty;
            _defaultBranchId = Guid.Parse(_config["Facebook:DefaultBranchId"] ?? Guid.Empty.ToString());
            _systemUserId = Guid.Parse(_config["Facebook:SystemUserId"] ?? Guid.Empty.ToString());
        }

        // ── GET: Facebook webhook verification ──────────────────────────────
        [HttpGet]
        [AllowAnonymous]
        public IActionResult VerifyWebhook()
        {
            var mode = Request.Query["hub.mode"].ToString();
            var challenge = Request.Query["hub.challenge"].ToString();
            var verifyToken = Request.Query["hub.verify_token"].ToString();

            _logger.LogInformation("Webhook verify attempt: mode={mode}, tokenProvided={hasToken}",
                mode, !string.IsNullOrEmpty(verifyToken));

            if (mode == "subscribe" && verifyToken == _verifyToken)
            {
                return Content(challenge, "text/plain");
            }

            _logger.LogWarning("Webhook verification failed. mode={mode}, verifyTokenMatch={match}",
                mode, verifyToken == _verifyToken);

            return Forbid();
        }

        // ── POST: Receive Facebook lead webhook ─────────────────────────────
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ReceiveLead()
        {
            try
            {
                string rawBody;
                using (var reader = new StreamReader(Request.Body))
                {
                    rawBody = await reader.ReadToEndAsync();
                }

                _logger.LogInformation("Received webhook POST from Facebook at {ts}", DateTime.UtcNow);
                _logger.LogInformation("Raw payload: {payload}", rawBody);

                if (string.IsNullOrWhiteSpace(rawBody))
                {
                    _logger.LogWarning("Empty webhook body received.");
                    return BadRequest("Empty payload");
                }

                JObject payload;
                try
                {
                    payload = JObject.Parse(rawBody);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Invalid JSON payload received.");
                    return BadRequest("Invalid JSON");
                }

                var leadgenId = payload?["entry"]?.First?["changes"]?.First?["value"]?["leadgen_id"]?.ToString();
                if (string.IsNullOrWhiteSpace(leadgenId))
                {
                    _logger.LogWarning("leadgen_id not found in payload.");
                    return BadRequest("leadgen_id missing");
                }

                _logger.LogInformation("Leadgen id: {leadgenId}", leadgenId);

                if (string.IsNullOrWhiteSpace(_pageAccessToken))
                {
                    _logger.LogError("Page access token is not configured.");
                    return StatusCode(500, "Configuration error");
                }

                // Prevent duplicate webhook handling
                var existingByFacebookLeadId = await _context.Leads
                    .AsNoTracking()
                    .FirstOrDefaultAsync(l => l.FacebookLeadId == leadgenId);

                if (existingByFacebookLeadId != null)
                {
                    _logger.LogInformation("Duplicate Facebook lead ignored. FacebookLeadId={leadgenId}", leadgenId);
                    return Ok("EVENT_RECEIVED");
                }

                // ── Fetch full lead details from Facebook Graph API ─────────
                var graphUrl =
                    $"https://graph.facebook.com/v25.0/{leadgenId}?access_token={_pageAccessToken}&fields=field_data,created_time,form_id";

                var http = _httpClientFactory.CreateClient();
                var graphResponse = await http.GetAsync(graphUrl);

                if (!graphResponse.IsSuccessStatusCode)
                {
                    var err = await graphResponse.Content.ReadAsStringAsync();
                    _logger.LogError("Graph API returned {status}: {err}", graphResponse.StatusCode, err);
                    return StatusCode(502, "Failed to fetch lead details");
                }

                var leadJson = await graphResponse.Content.ReadAsStringAsync();
                _logger.LogInformation("Graph API lead JSON: {leadJson}", leadJson);

                var leadJ = JObject.Parse(leadJson);
                var fieldData = leadJ["field_data"] as JArray ?? new JArray();

                foreach (var fd in fieldData)
                {
                    _logger.LogInformation("Facebook field received: {name} = {value}",
                        fd?["name"]?.ToString(),
                        fd?["values"]?.FirstOrDefault()?.ToString());
                }

                string GetFieldValue(params string[] keys)
                {
                    foreach (var k in keys)
                    {
                        var token = fieldData.FirstOrDefault(fd =>
                            string.Equals(fd?["name"]?.ToString(), k, StringComparison.OrdinalIgnoreCase));

                        if (token != null)
                        {
                            var values = token["values"] as JArray;
                            var val = values?.FirstOrDefault()?.ToString();
                            if (!string.IsNullOrWhiteSpace(val))
                                return val;
                        }
                    }

                    foreach (var fd in fieldData)
                    {
                        var name = fd?["name"]?.ToString() ?? string.Empty;
                        foreach (var k in keys)
                        {
                            if (!string.IsNullOrWhiteSpace(name) &&
                                name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var values = fd["values"] as JArray;
                                var val = values?.FirstOrDefault()?.ToString();
                                if (!string.IsNullOrWhiteSpace(val))
                                    return val;
                            }
                        }
                    }

                    return null;
                }

                string NormalizePhone(string phone)
                {
                    if (string.IsNullOrWhiteSpace(phone))
                        return phone;

                    phone = phone.Trim()
                                 .Replace(" ", "")
                                 .Replace("-", "")
                                 .Replace("(", "")
                                 .Replace(")", "");

                    if (phone.StartsWith("+2"))
                        phone = phone.Substring(2);
                    else if (phone.StartsWith("2") && phone.Length > 10)
                        phone = phone.Substring(1);

                    return phone;
                }

                var fullName = GetFieldValue("full_name", "full name", "name", "الاسم");
                var phone = GetFieldValue("phone_number", "phone", "mobile_phone", "رقم_الهاتف", "رقم الهاتف");
                var interestedCountry = GetFieldValue("country", "InterestedCountry", "interested_country", "work_country", "الدولة");
                var jobTitle = GetFieldValue("job_title", "position", "InterestedJobTitle", "job", "الوظيفة");
                var notes = GetFieldValue("notes", "note", "ملاحظات", "additional_info");

                phone = NormalizePhone(phone);

                if (string.IsNullOrWhiteSpace(phone))
                    phone = NormalizePhone(leadJ["phone_number"]?.ToString());

                _logger.LogInformation("Parsed lead: name={name}, phone={phone}, country={country}, job={job}",
                    fullName, phone, interestedCountry, jobTitle);

                if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(phone))
                {
                    _logger.LogWarning("Both fullName and phone are empty. Skipping lead.");
                    return BadRequest("No usable lead data");
                }

                // Check by phone if already exists
                Lead existingLead = null;
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    existingLead = await _context.Leads
                        .FirstOrDefaultAsync(l => l.Phone == phone);
                }

                if (existingLead != null)
                {
                    _logger.LogInformation("Existing lead found (Id={leadId}). Updating activity/visit.", existingLead.Id);

                    // Save Facebook IDs if not already set
                    if (string.IsNullOrWhiteSpace(existingLead.FacebookLeadId))
                        existingLead.FacebookLeadId = leadgenId;

                    if (string.IsNullOrWhiteSpace(existingLead.FacebookFormId))
                        existingLead.FacebookFormId = leadJ["form_id"]?.ToString();

                    var updated = false;

                    if (!string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(existingLead.FullName))
                    {
                        existingLead.FullName = fullName;
                        updated = true;
                    }

                    if (!string.IsNullOrWhiteSpace(jobTitle) && string.IsNullOrWhiteSpace(existingLead.InterestedJobTitle))
                    {
                        existingLead.InterestedJobTitle = jobTitle;
                        updated = true;
                    }

                    if (!string.IsNullOrWhiteSpace(interestedCountry) && string.IsNullOrWhiteSpace(existingLead.InterestedCountry))
                    {
                        existingLead.InterestedCountry = interestedCountry;
                        updated = true;
                    }

                    if (!string.IsNullOrWhiteSpace(notes))
                    {
                        existingLead.Notes = string.IsNullOrWhiteSpace(existingLead.Notes)
                            ? notes
                            : existingLead.Notes + " | " + notes;
                        updated = true;
                    }

                    if (updated)
                        existingLead.UpdatedAt = DateTime.UtcNow;

                    _context.LeadVisits.Add(new LeadVisit
                    {
                        Id = Guid.NewGuid(),
                        LeadId = existingLead.Id,
                        ReceptionUserId = _systemUserId,
                        BranchId = existingLead.BranchId,
                        AssignedSalesUserId = existingLead.AssignedOfficeSalesId,
                        JobPackageId = null,
                        Notes = $"Lead received from Facebook form.{(string.IsNullOrWhiteSpace(notes) ? "" : " " + notes)}",
                        MeetingOutcome = 1,
                        VisitDateTime = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    });

                    _context.LeadActivities.Add(new LeadActivity
                    {
                        Id = Guid.NewGuid(),
                        LeadId = existingLead.Id,
                        ActivityType = 2,
                        Description = $"تم استقبال lead عبر نموذج فيسبوك{(string.IsNullOrWhiteSpace(notes) ? "" : " · " + notes)}",
                        CreatedById = _systemUserId,
                        CreatedByName = "Facebook Lead",
                        ActorType = 3,
                        CreatedAt = DateTime.UtcNow
                    });

                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Existing lead updated successfully.");
                    return Ok("EVENT_RECEIVED");
                }

                // ── Create new lead ──────────────────────────────────────────
                var newLeadId = Guid.NewGuid();

                _context.Leads.Add(new Lead
                {
                    Id = newLeadId,
                    BranchId = _defaultBranchId,
                    RegisteredById = _systemUserId,
                    AssignedOfficeSalesId = null,
                    CampaignId = null,
                    FullName = fullName ?? string.Empty,
                    Phone = phone ?? string.Empty,
                    LeadSource = 1,
                    Status = 1,
                    InterestedJobTitle = jobTitle,
                    InterestedCountry = interestedCountry,
                    Notes = notes,
                    FacebookLeadId = leadgenId,
                    FacebookFormId = leadJ["form_id"]?.ToString(),
                    CreatedAt = DateTime.UtcNow
                });

                _context.LeadVisits.Add(new LeadVisit
                {
                    Id = Guid.NewGuid(),
                    LeadId = newLeadId,
                    ReceptionUserId = _systemUserId,
                    BranchId = _defaultBranchId,
                    AssignedSalesUserId = null,
                    JobPackageId = null,
                    Notes = $"Created from Facebook lead form.{(string.IsNullOrWhiteSpace(notes) ? "" : " " + notes)}",
                    MeetingOutcome = 1,
                    VisitDateTime = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });

                _context.LeadActivities.Add(new LeadActivity
                {
                    Id = Guid.NewGuid(),
                    LeadId = newLeadId,
                    ActivityType = 2,
                    Description = "تم إنشاء lead من نموذج فيسبوك · المصدر: Facebook",
                    CreatedById = _systemUserId,
                    CreatedByName = "Facebook Lead",
                    ActorType = 3,
                    CreatedAt = DateTime.UtcNow
                });

                _context.LeadFunnelHistories.Add(new LeadFunnelHistory
                {
                    Id = Guid.NewGuid(),
                    LeadId = newLeadId,
                    FromStatus = null,
                    ToStatus = 1,
                    ChangedById = _systemUserId,
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                _logger.LogInformation("New lead created successfully. LeadId={leadId}, FacebookLeadId={facebookLeadId}",
                    newLeadId, leadgenId);

                return Ok("EVENT_RECEIVED");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Facebook lead webhook.");
                return StatusCode(500, "Internal Server Error");
            }
        }
    }
}