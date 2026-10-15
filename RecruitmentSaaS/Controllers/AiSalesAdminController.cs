using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RecruitmentSaaS.Controllers;

[Authorize(Roles = "1")]
public sealed class AiSalesAdminController : Controller
{
    public IActionResult Index() => View();
}
