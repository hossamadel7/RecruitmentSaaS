using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Controllers
{
    /// <summary>
    /// Anonymous events from the public registration form (seen / field touched), sent by the
    /// browser with navigator.sendBeacon. Submissions and errors are recorded server-side in SubmitLead.
    /// </summary>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Route("track/form")]
    public class FormTrackingController : Controller
    {
        private static readonly HashSet<string> Fields = new() { "name", "phone", "age", "job", "notes" };
        private static readonly HashSet<string> Pages = new() { "home", "register", "team" };

        private readonly RecruitmentCrmContext _context;

        public FormTrackingController(RecruitmentCrmContext context)
        {
            _context = context;
        }

        public class TrackEvent
        {
            public Guid Id { get; set; }
            public string? Ev { get; set; }        // view | field
            public string? Page { get; set; }
            public string? Team { get; set; }
            public string? Field { get; set; }
            public string? Ref { get; set; }       // document.referrer
            public string? UtmSource { get; set; }
            public string? UtmCampaign { get; set; }
            public int? Secs { get; set; }
            public int? Scroll { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            // sendBeacon posts text/plain, so read the JSON body ourselves
            TrackEvent? e;
            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();
                if (body.Length > 2000) return NoContent();
                e = System.Text.Json.JsonSerializer.Deserialize<TrackEvent>(body,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { return NoContent(); }
            if (e == null || e.Id == Guid.Empty || IsBot()) return NoContent();

            var now = DateTime.UtcNow;
            var visit = await _context.FormVisits.FirstOrDefaultAsync(v => v.Id == e.Id);

            if (e.Ev == "view")
            {
                if (visit != null) return NoContent();
                _context.FormVisits.Add(new FormVisit
                {
                    Id = e.Id,
                    CreatedAt = now,
                    LastSeenAt = now,
                    Page = Pages.Contains(e.Page ?? "") ? e.Page! : "home",
                    TeamSlug = Cut(e.Team, 50),
                    Source = SourceOf(e.UtmSource, e.Ref),
                    UtmCampaign = Cut(e.UtmCampaign, 100),
                    Device = DeviceOf(Request.Headers.UserAgent.ToString())
                });
            }
            else if (e.Ev == "field" && visit != null && Fields.Contains(e.Field ?? ""))
            {
                var touched = (visit.FieldsTouched ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (!touched.Contains(e.Field!)) touched.Add(e.Field!);
                visit.FieldsTouched = string.Join(',', touched);
                visit.LastField = e.Field;
                visit.LastSeenAt = now;
            }
            else if (e.Ev == "wa" && visit != null)
            {
                visit.WhatsAppClicked = true;
                visit.LastSeenAt = now;
            }
            else if (e.Ev == "leave" && visit != null)
            {
                visit.SecondsOnPage = Math.Max(visit.SecondsOnPage, Math.Clamp(e.Secs ?? 0, 0, 3600));
                visit.MaxScrollPercent = Math.Max(visit.MaxScrollPercent, Math.Clamp(e.Scroll ?? 0, 0, 100));
            }
            else return NoContent();

            try { await _context.SaveChangesAsync(); }
            catch (DbUpdateException) { /* the same view sent twice at once — fine */ }
            return NoContent();
        }

        private bool IsBot()
        {
            var ua = Request.Headers.UserAgent.ToString().ToLowerInvariant();
            return ua.Length == 0 || ua.Contains("bot") || ua.Contains("spider") || ua.Contains("crawl") || ua.Contains("preview");
        }

        private static string? Cut(string? v, int max) =>
            string.IsNullOrWhiteSpace(v) ? null : (v.Trim().Length > max ? v.Trim()[..max] : v.Trim());

        // Where the visitor came from: the ad's utm_source when there is one, else the referring site
        private static string SourceOf(string? utmSource, string? referrer)
        {
            var s = ((utmSource ?? "") + " " + (referrer ?? "")).ToLowerInvariant();
            if (s.Contains("facebook") || s.Contains("fb.") || s.Contains("fbclid") || s.Contains("fb ")) return "facebook";
            if (s.Contains("instagram") || s.Contains("ig ")) return "instagram";
            if (s.Contains("tiktok")) return "tiktok";
            if (s.Contains("whatsapp") || s.Contains("wa.me")) return "whatsapp";
            if (s.Contains("google")) return "google";
            if (string.IsNullOrWhiteSpace(utmSource) && string.IsNullOrWhiteSpace(referrer)) return "direct";
            if (!string.IsNullOrWhiteSpace(referrer) && referrer.Contains("thearabianfahd.com")) return "direct";
            return "other";
        }

        private static string DeviceOf(string ua)
        {
            ua = ua.ToLowerInvariant();
            if (ua.Contains("ipad") || (ua.Contains("android") && !ua.Contains("mobile"))) return "tablet";
            if (ua.Contains("mobi") || ua.Contains("iphone") || ua.Contains("android")) return "mobile";
            return "desktop";
        }
    }
}
