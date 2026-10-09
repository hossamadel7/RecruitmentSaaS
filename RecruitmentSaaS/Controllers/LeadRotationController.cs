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
        public async Task<IActionResult> Index(string? scope)
        {
            var resolved = await ResolveScopeAsync(scope);
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
            ViewBag.Scope = key;
            ViewBag.ScopeTitle = title;
            if (IsAdmin && key == LeadDistributor.AllScope)
            {
                var teamSplit = await LeadDistributor.TeamRotationAsync(_context);
                var teamState = await _context.LeadRotationStates.AsNoTracking().FirstOrDefaultAsync(s => s.ScopeKey == LeadDistributor.TeamsScope);
                ViewBag.TeamSplit = teamSplit;
                ViewBag.TeamSplitCurrent = teamSplit.FirstOrDefault(t => t.UserId == teamState?.CurrentUserId)?.Name;
            }
            ViewBag.IsAdmin = IsAdmin;
            ViewBag.IsTeam = LeadDistributor.ManagerOfScope(key) != null;
            ViewBag.MaxShare = MaxShare;

            return View(rotation);
        }

        // ── POST /LeadRotation/Save ──────────────────────────────────────────
        // userIds in the new order, shares[i] = leads per turn for userIds[i] (0 = paused)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(string scope, List<Guid> userIds, List<short> shares)
        {
            var resolved = await ResolveScopeAsync(scope);
            if (resolved == null) return Forbid();
            var (key, candidates, _) = resolved.Value;

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
            if (shares.Count > 0 && shares.All(s => s == 0))
            {
                TempData["Error"] = "لازم شخص واحد على الأقل ياخد ليدز — مينفعش توقف الكل";
                return RedirectToAction("Index", new { scope = key });
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

            TempData["Success"] = "تم حفظ طريقة التوزيع — هتتطبق على الليدز الجاية";
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

            await _context.LeadRotationStates.Where(s => s.ScopeKey == key).ExecuteDeleteAsync();
            TempData["Success"] = "الدورة هتبدأ من أول واحد في القائمة";
            return RedirectToAction("Index", new { scope = key });
        }

        // ── POST /LeadRotation/SaveTeams ── how general leads are split between teams ──
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveTeams(List<Guid> teamIds, List<short> shares)
        {
            if (!IsAdmin) return Forbid();
            var allowed = (await LeadDistributor.TeamRotationAsync(_context)).Select(t => t.UserId).ToHashSet();
            if (teamIds.Count != shares.Count || teamIds.Count != teamIds.Distinct().Count() || teamIds.Any(id => !allowed.Contains(id)))
            {
                TempData["Error"] = "قائمة الفرق اتغيرت أثناء التعديل — حدّث الصفحة وحاول تاني";
                return RedirectToAction("Index", new { scope = LeadDistributor.AllScope });
            }
            if (shares.Any(s => s < 0 || s > MaxShare))
            {
                TempData["Error"] = $"عدد الليدز لكل فريق لازم يكون من 0 لـ {MaxShare}";
                return RedirectToAction("Index", new { scope = LeadDistributor.AllScope });
            }

            await _context.LeadRotationMembers.Where(m => m.ScopeKey == LeadDistributor.TeamsScope).ExecuteDeleteAsync();
            for (var i = 0; i < teamIds.Count; i++)
                _context.LeadRotationMembers.Add(new LeadRotationMember { ScopeKey = LeadDistributor.TeamsScope, UserId = teamIds[i], Position = i, Share = shares[i] });
            await _context.SaveChangesAsync();
            await _context.LeadRotationStates.Where(s => s.ScopeKey == LeadDistributor.TeamsScope).ExecuteDeleteAsync();

            TempData["Success"] = shares.Any(s => s > 0)
                ? "تم الحفظ ✅ الليدز العامة هتتقسم على الفرق، وكل فريق يوزّع نصيبه بتوزيع الفريق"
                : "تم الحفظ — مفيش تقسيم على الفرق، الليدز العامة بتتوزع بالتوزيع العام تحت";
            return RedirectToAction("Index", new { scope = LeadDistributor.AllScope });
        }

        /// <summary>
        /// The rotation this user may see/edit. Managers are always pinned to their own team;
        /// admins get the general rotation by default or any active manager's team.
        /// </summary>
        private async Task<(string Key, IQueryable<User> Candidates, string Title)?> ResolveScopeAsync(string? scope)
        {
            if (!IsAdmin)
            {
                var me = CurrentUserId;
                return (LeadDistributor.TeamScope(me), _context.Users.Where(u => u.ManagerId == me), "فريقي");
            }

            if (string.IsNullOrEmpty(scope) || scope == LeadDistributor.AllScope)
                return (LeadDistributor.AllScope, _context.Users, "التوزيع العام");

            var managerId = LeadDistributor.ManagerOfScope(scope);
            if (managerId == null) return null;
            var manager = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == managerId && u.Role == 7 && u.IsActive);
            if (manager == null) return null;

            return (LeadDistributor.TeamScope(manager.Id), _context.Users.Where(u => u.ManagerId == manager.Id), "فريق " + manager.FullName);
        }
    }
}
