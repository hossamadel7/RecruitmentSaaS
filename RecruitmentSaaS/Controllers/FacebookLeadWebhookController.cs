using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System;
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
        private readonly string _verifyToken;
        private readonly string _pageAccessToken;

        public FacebookLeadWebhookController(
            RecruitmentCrmContext context,
            IConfiguration config,
            ILogger<FacebookLeadWebhookController> logger)
        {
            _context = context;
            _config = config;
            _logger = logger;

            // Read tokens from configuration (appsettings.json or environment variables)
            _verifyToken = _config["Facebook:VerifyToken"] ?? "1234";
            _pageAccessToken = _config["Facebook:PageAccessToken"] ?? "";
        }

        /// <summary>
        /// Facebook webhook verification endpoint
        /// Example:
        /// GET /api/webhooks/facebooklead?hub.mode=subscribe&hub.challenge=...&hub.verify_token=...
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public IActionResult VerifyWebhook(
            [FromQuery(Name = "hub.mode")] string mode,
            [FromQuery(Name = "hub.challenge")] string challenge,
            [FromQuery(Name = "hub.verify_token")] string verifyToken)
        {
            _logger.LogInformation("Webhook verify attempt: mode={mode}, tokenProvided={hasToken}", mode, !string.IsNullOrEmpty(verifyToken));
            if (mode == "subscribe" && verifyToken == _verifyToken)
            {
                // return the challenge string exactly as Facebook expects
                return Ok(challenge);
            }
            return Forbid();
        }

        /// <summary>
        /// Handle incoming lead POST from Facebook.
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ReceiveLead([FromBody] JObject payload)
        {
            try
            {
                _logger.LogInformation("Received webhook POST from Facebook: {ts}", DateTime.UtcNow);

                // Extract leadgen_id (robust parsing)
                var leadgenId = payload?["entry"]?.First?["changes"]?.First?["value"]?["leadgen_id"]?.ToString();
                if (string.IsNullOrEmpty(leadgenId))
                {
                    _logger.LogWarning("leadgen_id not found in payload. Payload: {payload}", payload?.ToString(Formatting.None));
                    return BadRequest("leadgen_id missing");
                }

                _logger.LogInformation("Leadgen id: {leadgenId}", leadgenId);

                // Fetch lead details from Graph API (request field_data)
                if (string.IsNullOrEmpty(_pageAccessToken))
                {
                    _logger.LogError("Page access token is not configured. Set Facebook:PageAccessToken in configuration.");
                    return StatusCode(500, "Configuration error");
                }

                var graphUrl = $"https://graph.facebook.com/v19.0/{leadgenId}?access_token={_pageAccessToken}&fields=field_data,created_time,form_id";
                string leadJson;
                using (var http = new HttpClient())
                {
                    var res = await http.GetAsync(graphUrl);
                    if (!res.IsSuccessStatusCode)
                    {
                        var err = await res.Content.ReadAsStringAsync();
                        _logger.LogError("Graph API returned non-success status {status}: {err}", res.StatusCode, err);
                        return StatusCode(502, "Failed to fetch lead details");
                    }
                    leadJson = await res.Content.ReadAsStringAsync();
                }

                _logger.LogDebug("Graph API lead JSON: {leadJson}", leadJson);

                var leadJ = JObject.Parse(leadJson);

                var fieldData = leadJ["field_data"] as JArray ?? new JArray();

                // Helper to find a value in field_data using multiple keys
                string GetFieldValue(params string[] keys)
                {
                    foreach (var k in keys)
                    {
                        var token = fieldData.FirstOrDefault(fd => string.Equals(fd?["name"]?.ToString(), k, StringComparison.OrdinalIgnoreCase));
                        if (token != null)
                        {
                            var values = token["values"] as JArray;
                            var val = values?.FirstOrDefault()?.ToString();
                            if (!string.IsNullOrEmpty(val)) return val;
                        }
                    }
                    // Search by name containing the key (loose match)
                    foreach (var fd in fieldData)
                    {
                        var name = fd?["name"]?.ToString() ?? "";
                        foreach (var k in keys)
                        {
                            if (!string.IsNullOrEmpty(name) && name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var values = fd["values"] as JArray;
                                var val = values?.FirstOrDefault()?.ToString();
                                if (!string.IsNullOrEmpty(val)) return val;
                            }
                        }
                    }
                    return null;
                }

                // Try common variants (English/Arabic)
                var fullName = GetFieldValue("full_name", "full name", "name", "الاسم");
                var phone = GetFieldValue("phone_number", "phone", "mobile_phone", "رقم_الهاتف", "رقم الهاتف", "phone_number");
                var interestedCountry = GetFieldValue("country", "InterestedCountry", "interested_country", "work_country", "country", "الدولة");
                var jobTitle = GetFieldValue("job_title", "position", "InterestedJobTitle", "job", "الوظيفة");
                var notes = GetFieldValue("notes", "note", "ملاحظات", "additional_info");

                // If phone still missing, try leadJ
                if (string.IsNullOrEmpty(phone))
                {
                    phone = leadJ["phone_number"]?.ToString() ?? leadJ["values"]?.First?["phone_number"]?.ToString();
                }

                _logger.LogInformation("Parsed lead values: name={name}, phone={phone}, country={country}, job={job}", fullName, phone, interestedCountry, jobTitle);

                // Basic validation
                if (string.IsNullOrEmpty(fullName) && string.IsNullOrEmpty(phone))
                {
                    _logger.LogWarning("Both fullName and phone are empty; skipping record creation.");
                    return BadRequest("No usable lead data");
                }

                RecruitmentSaaS.Models.Entities.Lead existingLead = null;
                if (!string.IsNullOrEmpty(phone))
                {
                    existingLead = await _context.Leads
                        .FirstOrDefaultAsync(l => l.Phone == phone);
                }

                if (existingLead != null)
                {
                    // Existing lead: create activity & visit record
                    _logger.LogInformation("Existing lead found (Id={leadId}). Adding visit and activity.", existingLead.Id);

                    // Add LeadVisit
                    var visit = new LeadVisit
                    {
                        Id = Guid.NewGuid(),
                        LeadId = existingLead.Id,
                        ReceptionUserId = Guid.Empty, // FB lead: no receptionist
                        BranchId = existingLead.BranchId,
                        AssignedSalesUserId = existingLead.AssignedOfficeSalesId,
                        JobPackageId = Guid.Empty,
                        Notes = $"Lead received from Facebook form. {(string.IsNullOrEmpty(notes) ? "" : notes)}",
                        MeetingOutcome = 1,
                        VisitDateTime = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.LeadVisits.Add(visit);

                    // Add LeadActivity
                    var activity = new LeadActivity
                    {
                        Id = Guid.NewGuid(),
                        LeadId = existingLead.Id,
                        ActivityType = 2,
                        Description = $"تم استقبال lead عبر نموذج فيسبوك · المصدر: Facebook · {(string.IsNullOrEmpty(notes) ? "" : notes)}",
                        CreatedById = Guid.Empty,
                        CreatedByName = "Facebook Lead",
                        ActorType = 3,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.LeadActivities.Add(activity);

                    // Optionally update fields if new info present
                    var updated = false;
                    if (!string.IsNullOrEmpty(fullName) && string.IsNullOrEmpty(existingLead.FullName))
                    {
                        existingLead.FullName = fullName;
                        updated = true;
                    }
                    if (!string.IsNullOrEmpty(jobTitle) && string.IsNullOrEmpty(existingLead.InterestedJobTitle))
                    {
                        existingLead.InterestedJobTitle = jobTitle;
                        updated = true;
                    }
                    if (!string.IsNullOrEmpty(interestedCountry) && string.IsNullOrEmpty(existingLead.InterestedCountry))
                    {
                        existingLead.InterestedCountry = interestedCountry;
                        updated = true;
                    }
                    if (!string.IsNullOrEmpty(notes))
                    {
                        existingLead.Notes = string.IsNullOrEmpty(existingLead.Notes) ? notes : (existingLead.Notes + " | " + notes);
                        updated = true;
                    }
                    if (updated)
                    {
                        existingLead.UpdatedAt = DateTime.UtcNow;
                    }

                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Existing lead updated and visit recorded.");
                    return Ok();
                }
                else
                {
                    // Create new lead
                    var newLeadId = Guid.NewGuid();

                    var leadEntity = new RecruitmentSaaS.Models.Entities.Lead
                    {
                        Id = newLeadId,
                        BranchId = Guid.Empty, // system default or assign valid branch Guid here
                        RegisteredById = Guid.Empty, // system registration from facebook
                        AssignedOfficeSalesId = Guid.Empty,
                        CampaignId = Guid.Empty,
                        FullName = fullName ?? string.Empty,
                        Phone = phone ?? string.Empty,
                        LeadSource = 1,
                        Status = 6,
                        InterestedJobTitle = jobTitle,
                        InterestedCountry = interestedCountry,
                        Notes = notes,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.Leads.Add(leadEntity);

                    // Add initial LeadVisit (so it appears in reception)
                    var visit = new LeadVisit
                    {
                        Id = Guid.NewGuid(),
                        LeadId = newLeadId,
                        ReceptionUserId = Guid.Empty,
                        BranchId = Guid.Empty,
                        AssignedSalesUserId = Guid.Empty,
                        JobPackageId = Guid.Empty,
                        Notes = $"Created from Facebook lead form. {(string.IsNullOrEmpty(notes) ? "" : notes)}",
                        MeetingOutcome = 1,
                        VisitDateTime = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.LeadVisits.Add(visit);

                    // Add LeadActivity
                    var activity = new LeadActivity
                    {
                        Id = Guid.NewGuid(),
                        LeadId = newLeadId,
                        ActivityType = 2,
                        Description = $"تم إ��شاء lead من نموذج فيسبوك · المصدر: Facebook",
                        CreatedById = Guid.Empty,
                        CreatedByName = "Facebook Lead",
                        ActorType = 3,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.LeadActivities.Add(activity);

                    // Add funnel history (optional)
                    var funnel = new LeadFunnelHistory
                    {
                        Id = Guid.NewGuid(),
                        LeadId = newLeadId,
                        FromStatus = 0,
                        ToStatus = 6,
                        ChangedById = Guid.Empty,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.LeadFunnelHistories.Add(funnel);

                    await _context.SaveChangesAsync();

                    _logger.LogInformation("New lead created (Id={leadId}).", newLeadId);
                    return Ok();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Facebook lead webhook.");
                return StatusCode(500, "Internal Server Error");
            }
        }
    }
}