using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitmentSaaS.Services;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = WhatsAppAuthorization.AllowedRoles)]
    public class InboxController : Controller
    {
        // ── GET /Inbox ────────────────────────────────────────────────────────
        public IActionResult Index()
        {
            ViewData["Title"] = "الواتساب";
            ViewData["Subtitle"] = "صندوق المحادثات المشترك";
            return View();
        }
    }
}
