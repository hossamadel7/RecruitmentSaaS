using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RecruitmentSaaS.Controllers;
[Authorize(Roles = "1")]
public sealed class AiChatLearningAdminController : Controller
{
    [HttpGet("AiChatLearning")]
    public IActionResult Index() => View("~/Views/AiChatLearning/Index.cshtml");
}
