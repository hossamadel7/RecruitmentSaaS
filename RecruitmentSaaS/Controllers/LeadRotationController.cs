using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    /// <summary>
    /// "توزيع الليدز": the order TeleSales receive leads in and how many each takes per turn.
    /// Admin edits the general rotation and any team's; a TeleSales Manager edits only their team's.
    /// </summary>
    [Authorize(Roles = "1,7")]
    public class LeadRotationController : Controller
    {
        public const short MaxShare = 20;
        private const int PreviewCount = 12;

        private readonly RecruitmentCrmContext _context;

        public LeadRotationController(RecruitmentCrmContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private bool IsAdmin => User.IsInRole("1");

        // ── GET /LeadRotation/Index?scope=all|team:{managerId} ───────────────
        public async Task<IActionResult> Index(string? scope, bool under = false, string? country = null)
        {
            var resolved = await ResolveScopeAsync(scope, under, country);
            if (resolved == null) return RedirectToAction("Index", new { scope = (string?)null });
            var (key, candidates, title) = resolved.Value;

            var rotation = await LeadDistributor.RotationAsync(_context, candidates, key);
            var state = await _context.LeadRotationStates.AsNoTracking().FirstOrDefaultAsync(s => s.ScopeKey == key);
            var names = rotation.ToDictionary(r => r.UserId, r => r.Name);

            var current = state?.CurrentUserId != null ? rotation.FirstOrDefault(r => r.UserId == state.CurrentUserId) : null;
            ViewBag.CurrentName = current?.Name;
            ViewBag.CurrentGiven = current != null ? state!.GivenInTurn : 0;
            ViewBag.CurrentShare = current?.Share ?? 0;
            ViewBag.Upcoming = LeadDistributor.Preview(rotation, state?.CurrentUserId, state?.GivenInTurn ?? 0, PreviewCount)
                .Select(id => names[id]).ToList();

            if (IsAdmin)
            {
                var managers = await _context.Users.AsNoTracking()
                    .Where(u => u.Role == 7 && u.IsActive)
                    .OrderBy(u => u.FullName)
                    .Select(u => new { u.Id, u.FullName })
                    .ToListAsync();
                ViewBag.Teams = managers.Select(m => (Scope: LeadDistributor.TeamScope(m.Id), Name: m.FullName)).ToList();
            }
            var teamManagerId = LeadDistributor.ManagerOfScope(key);
            var selectedCountry = CountryRouting.CountryOfScope(key);
            var rules = await CountryRouting.RulesAsync(_context, teamManagerId);
            ViewBag.Country = selectedCountry;
            ViewBag.CountryRule = rules.FirstOrDefault(r => r.Country == selectedCountry)
                ?? new CountryRoutingRule { Country = selectedCountry ?? "" };
            ViewBag.Scope = key;
            ViewBag.ScopeTitle = title;
            ViewBag.IsAdmin = IsAdmin;
            ViewBag.IsTeam = teamManagerId != null;
            ViewBag.ManagerId = teamManagerId;
            ViewBag.MaxShare = MaxShare;

            return View(rotation);
        }

        // ── POST /LeadRotation/Save ──────────────────────────────────────────
        // userIds in the new order, shares[i] = leads per turn for userIds[i] (0 = paused)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(string scope, List<Guid> userIds, List<short> shares, int? minAge, int? maxAge, bool enabled = false, string? fallbackCountry = null)
        {
            var resolved = await ResolveScopeAsync(scope);
            if (resolved == null) return Forbid();
            var (key, candidates, _) = resolved.Value;
            var country = CountryRouting.CountryOfScope(key);
            if (country != null && (!ModelState.IsValid || minAge is null or < 16 || maxAge is null or > 100 || minAge > maxAge ||
                (fallbackCountry != null && (!CountryRouting.Countries.Contains(fallbackCountry) || fallbackCountry == country))))
            {
                TempData["Error"] = "حدد السن من كام لكام بشكل صحيح (من 16 إلى 100)";
                return RedirectToAction(nameof(Index), new { scope = key });
            }
            await using var tx = await _context.Database.BeginTransactionAsync();
            await ConversationLock.LockTransactionAsync(_context, "rotation:" + key);

            var allowed = (await LeadDistributor.RotationAsync(_context, candidates, key)).Select(r => r.UserId).ToHashSet();
            if (userIds.Count != shares.Count || userIds.Count != userIds.Distinct().Count()
                || userIds.Any(id => !allowed.Contains(id)))
            {
                TempData["Error"] = "القائمة اتغيرت أثناء التعديل — حدّث الصفحة وحاول تاني";
                return RedirectToAction("Index", new { scope = key });
            }
            if (shares.Any(s => s < 0 || s > MaxShare))
            {
                TempData["Error"] = $"عدد الليدز لكل دور لازم يكون من 0 لـ {MaxShare}";
                return RedirectToAction("Index", new { scope = key });
            }
            // An under-age rotation may be all 0: those clients then wait with the team leader
            if (shares.Count > 0 && shares.All(s => s == 0) && !LeadDistributor.IsUnderAgeScope(key) && country == null)
            {
                TempData["Error"] = "لازم شخص واحد على الأقل ياخد ليدز — مينفعش توقف الكل";
                return RedirectToAction("Index", new { scope = key });
            }

            if (country != null)
            {
                var manager = LeadDistributor.ManagerOfScope(key);
                if (manager == null)
                {
                    var rule = await _context.CountryRoutingRules.SingleAsync(r => r.Country == country);
                    rule.Enabled = enabled;
                    rule.MinAge = minAge!.Value;
                    rule.MaxAge = maxAge!.Value;
                    rule.FallbackCountry = fallbackCountry;
                }
                else
                {
                    var rule = await _context.TeamCountryRoutingRules.SingleOrDefaultAsync(r => r.ManagerId == manager && r.Country == country);
                    if (rule == null)
                    {
                        rule = new TeamCountryRoutingRule { ManagerId = manager.Value, Country = country };
                        _context.TeamCountryRoutingRules.Add(rule);
                    }
                    rule.Enabled = enabled;
                    rule.MinAge = minAge!.Value;
                    rule.MaxAge = maxAge!.Value;
                    rule.FallbackCountry = fallbackCountry;
                }
            }
            await _context.LeadRotationMembers.Where(m => m.ScopeKey == key).ExecuteDeleteAsync();
            for (var i = 0; i < userIds.Count; i++)
            {
                _context.LeadRotationMembers.Add(new LeadRotationMember
                {
                    ScopeKey = key,
                    UserId = userIds[i],
                    Position = i,
                    Share = shares[i]
                });
            }
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Success"] = country == null ? "تم حفظ طريقة التوزيع — هتتطبق على الليدز الجاية"
                : enabled ? $"تم الحفظ — توزيع ليدز {country} مفعّل الآن" +
                    (shares.Any(s => s > 0) ? "" : "، حدد حصة لموظف واحد على الأقل لبدء استقبال الليدز")
                : $"تم الحفظ — توزيع ليدز {country} متوقف الآن";
            return RedirectToAction("Index", new { scope = key });
        }

        // ── POST /LeadRotation/Restart ───────────────────────────────────────
        // Next lead goes to the first person in the list
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restart(string scope)
        {
            var resolved = await ResolveScopeAsync(scope);
            if (resolved == null) return Forbid();
            var key = resolved.Value.Key;
            await using var tx = await _context.Database.BeginTransactionAsync();
            await ConversationLock.LockTransactionAsync(_context, "rotation:" + key);

            await _context.LeadRotationStates.Where(s => s.ScopeKey == key).ExecuteDeleteAsync();
            await tx.CommitAsync();
            TempData["Success"] = "الدورة هتبدأ من أول واحد في القائمة";
            return RedirectToAction("Index", new { scope = key });
        }

        /// <summary>
        /// The rotation this user may see/edit. Managers are always pinned to their own team;
        /// admins get the general rotation by default or any active manager's team.
        /// </summary>
        internal async Task<(string Key, IQueryable<User> Candidates, string Title)?> ResolveScopeAsync(string? scope, bool under = false, string? country = null)
        {
            country ??= CountryRouting.CountryOfScope(scope) ?? CountryRouting.Countries[0];
            if (!CountryRouting.Countries.Contains(country)) return null;
            if (!IsAdmin)
            {
                var me = CurrentUserId;
                return (CountryRouting.TeamScope(me, country), _context.Users.Where(u => u.ManagerId == me || u.TeamMemberships.Any(m => m.ManagerId == me)), "فريقي — " + CountryRouting.DisplayName(country));
            }
            if (scope == LeadDistributor.AllScope)
                return (LeadDistributor.AllScope, _context.Users, "التوزيع العام");
            if (string.IsNullOrEmpty(scope) || scope.StartsWith("country:"))
                return (CountryRouting.Scope(country), _context.Users, "إعلانات " + CountryRouting.DisplayName(country));
            var managerId = LeadDistributor.ManagerOfScope(scope);
            if (managerId == null) return null;
            var manager = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == managerId && u.Role == 7 && u.IsActive);
            if (manager == null) return null;
            return (CountryRouting.TeamScope(manager.Id, country),
                _context.Users.Where(u => u.ManagerId == manager.Id || u.TeamMemberships.Any(m => m.ManagerId == manager.Id)),
                "فريق " + manager.FullName + " — " + CountryRouting.DisplayName(country));
        }
    }
}
