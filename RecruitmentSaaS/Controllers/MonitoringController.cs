using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RecruitmentSaaS.Controllers;

[Authorize(Roles = "1,9")]
public sealed class MonitoringController : Controller
{
    public IActionResult Index() => View();
}
