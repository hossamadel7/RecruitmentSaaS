using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;

namespace RecruitmentSaaS.Controllers
{
    [AllowAnonymous]
    public class HomeController : Controller
    {
        private readonly RecruitmentCrmContext _context;
        private readonly ILogger<HomeController> _logger;
        private readonly INotificationService _notifications;

        public HomeController(RecruitmentCrmContext context, ILogger<HomeController> logger,
                              INotificationService notifications)
        {
            _context = context;
            _logger = logger;
            _notifications = notifications;
        }

        // ── GET / ─────────────────────────────────────────────────────────
        public async Task<IActionResult> Index([FromQuery(Name = "ref")] string? salesRef)
        {
            RememberSalesRef(salesRef);

            ViewBag.Branches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .ToListAsync();

            ViewBag.JobPackages = await _context.JobPackages
                .Where(j => j.IsActive)
                .OrderBy(j => j.DestinationCountry)
                .ThenBy(j => j.JobTitle)
                .ToListAsync();

            return View();
        }

        // ── GET /register — standalone registration page (form only) ─────
        // ── GET /register/{team} — a TeleSales team's own form ───────────
        [HttpGet("/register/{team?}")]
        [HttpGet("/Home/Register")]
        public async Task<IActionResult> Register(string? team, [FromQuery(Name = "ref")] string? salesRef)
        {
            if (!string.IsNullOrWhiteSpace(team))
            {
                // Unknown/disabled team links 404 so a broken link gets noticed instead of silently losing the team
                if (await FindTeamFormAsync(team) == null)
                    return NotFound();
                ViewBag.TeamSlug = team.Trim().ToLowerInvariant();
            }

            RememberSalesRef(salesRef);
            return View();
        }

        private Task<TeamLeadForm?> FindTeamFormAsync(string slug)
        {
            slug = slug.Trim().ToLowerInvariant();
            return _context.TeamLeadForms
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Slug == slug && f.Manager.IsActive && f.Manager.Role == 7);
        }

        // لو في ref — نحتفظ بيه في الـ Cookie عشان الـ SubmitLead يقراه
        private void RememberSalesRef(string? salesRef)
        {
            if (!string.IsNullOrWhiteSpace(salesRef)
                && Guid.TryParse(salesRef, out var salesId))
            {
                Response.Cookies.Append("sales_ref", salesId.ToString(), new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddHours(2)
                });
            }
        }

        // ── POST /Home/SubmitLead ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitLead(
            string fullName,
            string phone,
            byte? age,
            string? interestedJobTitle,
            string? notes,
            string? returnTo,
            string? team)
        {
            // Errors go back to whichever page the form was on
            IActionResult BackToForm() =>
                !string.IsNullOrWhiteSpace(team) ? Redirect("/register/" + Uri.EscapeDataString(team))
                : returnTo == "register" ? Redirect("/register")
                : RedirectToAction("Index");

            if (age is null or < MinAge or > MaxAge)
            {
                TempData["FormError"] = "من فضلك اختر السن";
                return BackToForm();
            }

            // 1. Find first active branch
            var activeBranches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .ToListAsync();

            var resolvedBranchId = activeBranches.Count > 0
                ? activeBranches[0].Id
                : (Guid?)null;

            if (resolvedBranchId == null)
            {
                TempData["FormError"] = "لا يوجد فرع متاح";
                return BackToForm();
            }

            // 2. Determine RegisteredById (first admin)
            var systemUserId = await _context.Users
                .Where(u => u.Role == 1 && u.IsActive)
                .OrderBy(u => u.CreatedAt)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync();

            if (systemUserId == null)
            {
                TempData["FormError"] = "حدث خطأ في النظام";
                return BackToForm();
            }

            var settings = await _context.LeadFormSettings.AsNoTracking().FirstOrDefaultAsync();
            var isSenior = age.Value >= (settings?.SeniorAgeThreshold ?? DefaultSeniorAgeThreshold);
            var teamForm = string.IsNullOrWhiteSpace(team) ? null : await FindTeamFormAsync(team);

            // 3. Duplicate phone — silent success
            phone = phone?.Trim() ?? "";
            fullName = fullName?.Trim() ?? "";
            interestedJobTitle = interestedJobTitle?.Trim();
            if (await _context.Leads.AnyAsync(l => l.Phone == phone))
                return ThankYou(isSenior
                    ? BuildWhatsAppUrl(settings, teamForm, fullName, age.Value, interestedJobTitle, referenceCode: null)
                    : null);

            // 4. مين ياخد الـ Lead
            Guid? assignedSalesId = null;
            if (teamForm != null)
            {
                // فورم الفريق: الكبار (>= السن المحدد) يتوزعوا بالدور على تيلي سيلز الفريق،
                // والباقي يفضلوا مع مدير الفريق بس (مش في الـ Pool)
                if (isSenior)
                    assignedSalesId = await NextTeamMemberAsync(teamForm.ManagerId);
                Response.Cookies.Delete("sales_ref");
            }
            else if (Request.Cookies.TryGetValue("sales_ref", out var refCookie)
                && Guid.TryParse(refCookie, out var refSalesId))
            {
                // تحقق إن الـ User ده موجود وـ Role = 3 (TeleSales)
                var salesExists = await _context.Users
                    .AnyAsync(u => u.Id == refSalesId
                               && u.Role == 3
                               && u.IsActive);

                if (salesExists)
                    assignedSalesId = refSalesId;

                // امسح الـ Cookie بعد الاستخدام
                Response.Cookies.Delete("sales_ref");
            }

            // 5. Create Lead
            var leadId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            // 6. WhatsApp redirect for applicants at/above the age threshold. If the number is one of
            //    our connected WhatsApp accounts and the lead has an agent, attach a handoff reference so
            //    the incoming chat lands in the Inbox already linked to this lead and assigned to that agent.
            string? whatsAppUrl = null;
            WhatsAppHandoff? handoff = null;
            if (isSenior)
            {
                var account = assignedSalesId.HasValue
                    ? await FindWhatsAppAccountAsync(SalesWhatsAppNumber(settings, teamForm))
                    : null;
                var referenceCode = account != null ? await WhatsAppHandoffCodes.GenerateUniqueAsync(_context) : null;

                whatsAppUrl = BuildWhatsAppUrl(settings, teamForm, fullName, age.Value, interestedJobTitle, referenceCode);

                if (account != null && referenceCode != null && whatsAppUrl != null)
                {
                    handoff = new WhatsAppHandoff
                    {
                        Id = Guid.NewGuid(),
                        LeadId = leadId,
                        WhatsAppAccountId = account.Id,
                        AssignedSalesAgentId = assignedSalesId!.Value,
                        ReferenceCode = referenceCode,
                        GeneratedWhatsAppUrl = whatsAppUrl,
                        Status = (byte)HandoffStatus.Sent,
                        CreatedAt = now,
                        SentAt = now
                    };
                }
            }

            _context.Leads.Add(new Lead
            {
                Id = leadId,
                BranchId = resolvedBranchId.Value,
                RegisteredById = systemUserId.Value,
                FullName = fullName,
                Phone = phone,
                LeadSource = 2,  // موقع إلكتروني
                Status = 1,  // New
                Age = age,
                InterestedJobTitle = interestedJobTitle,
                Notes = notes?.Trim(),
                AssignedSalesId = assignedSalesId, // ← TeleSales — null لو مفيش ref
                TeamManagerId = teamForm?.ManagerId,
                CreatedAt = now
            });

            if (handoff != null)
                _context.WhatsAppHandoffs.Add(handoff);

            await _context.SaveChangesAsync();

            if (teamForm != null)
                await NotifyTeamAsync(teamForm.ManagerId, assignedSalesId, leadId, fullName, age.Value);

            return ThankYou(whatsAppUrl);
        }

        public const byte MinAge = 18;
        public const byte MaxAge = 65;
        public const byte DefaultSeniorAgeThreshold = 45;

        // Round-robin: the team member who got a team-form lead longest ago (or never) is next
        private async Task<Guid?> NextTeamMemberAsync(Guid managerId)
        {
            var members = await _context.Users
                .Where(u => u.ManagerId == managerId && u.Role == 3 && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync();

            if (members.Count == 0)
                return null; // no team members — lead stays with the manager

            var lastAssigned = await _context.Leads
                .Where(l => l.TeamManagerId == managerId
                         && l.AssignedSalesId != null
                         && members.Contains(l.AssignedSalesId.Value))
                .GroupBy(l => l.AssignedSalesId!.Value)
                .Select(g => new { UserId = g.Key, Last = g.Max(l => l.CreatedAt) })
                .ToDictionaryAsync(x => x.UserId, x => x.Last);

            return members
                .OrderBy(id => lastAssigned.TryGetValue(id, out var last) ? last : DateTime.MinValue)
                .ThenBy(id => id)
                .First();
        }

        private async Task NotifyTeamAsync(Guid managerId, Guid? assignedSalesId, Guid leadId, string fullName, byte age)
        {
            try
            {
                if (assignedSalesId.HasValue)
                    await _notifications.SendAsync(assignedSalesId.Value,
                        "ليد جديد من فورم الفريق",
                        $"{fullName} — السن {age} — تم تعيينه لك وهيكلمك على واتساب",
                        link: $"/TeleSales/LeadDetail/{leadId}");
                else
                    await _notifications.SendAsync(managerId,
                        "ليد جديد في فورم الفريق",
                        $"{fullName} — السن {age}",
                        link: "/TeleSalesManager/TeamFormLeads");
            }
            catch (Exception ex)
            {
                // The lead is already saved; a failed notification must not fail the public form
                _logger.LogError(ex, "Failed to send team-form notification for lead {LeadId}", leadId);
            }
        }

        private IActionResult ThankYou(string? whatsAppUrl)
        {
            if (whatsAppUrl != null)
                TempData["WhatsAppUrl"] = whatsAppUrl;
            return RedirectToAction("ThankYou");
        }

        private static string Digits(string? value) => new((value ?? "").Where(char.IsDigit).ToArray());

        // The team's own number on a team form, otherwise the global sales number
        private static string SalesWhatsAppNumber(LeadFormSetting? settings, TeamLeadForm? teamForm) =>
            Digits(!string.IsNullOrWhiteSpace(teamForm?.WhatsAppNumber)
                ? teamForm.WhatsAppNumber
                : settings?.SalesWhatsAppNumber);

        private async Task<WhatsAppAccount?> FindWhatsAppAccountAsync(string number)
        {
            if (number.Length == 0) return null;
            var accounts = await _context.WhatsAppAccounts.AsNoTracking().Where(a => a.IsActive).ToListAsync();
            return accounts.FirstOrDefault(a => Digits(a.DisplayPhoneNumber) == number);
        }

        private static string? BuildWhatsAppUrl(LeadFormSetting? settings, TeamLeadForm? teamForm,
                                                string fullName, byte age, string? jobTitle, string? referenceCode)
        {
            var number = SalesWhatsAppNumber(settings, teamForm);
            if (number.Length == 0) return null;

            var message = (settings?.SalesWhatsAppMessage ?? DefaultSalesWhatsAppMessage)
                .Replace("{name}", fullName)
                .Replace("{age}", age.ToString())
                .Replace("{job}", string.IsNullOrWhiteSpace(jobTitle) ? "—" : jobTitle);

            // Matched by WhatsAppWebhookProcessor.TryConnectHandoffAsync — keep the "REF-XXXXXX" format
            if (referenceCode != null)
                message += $"\nReference: #{referenceCode}";

            return $"https://wa.me/{number}?text={Uri.EscapeDataString(message)}";
        }

        public const string DefaultSalesWhatsAppMessage =
            "السلام عليكم، أنا {name} وسجلت على موقع الفهد العربي. السن: {age} — الوظيفة المطلوبة: {job}";

        // ── GET /Home/ThankYou ────────────────────────────────────────────
        public IActionResult ThankYou()
        {
            ViewBag.WhatsAppUrl = TempData["WhatsAppUrl"] as string;
            return View();
        }

        // ── GET /Home/Privacy ────────────────────────────────────────────
        public IActionResult Privacy() => View();

        // ── GET /Home/Terms ──────────────────────────────────────────────
        public IActionResult Terms() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() =>
            View(new RecruitmentSaaS.Models.ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
    }
}