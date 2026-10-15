using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using System.Security.Claims;

namespace RecruitmentSaaS.Services;

/// <summary>Serialize staff sends with intake and future controlled dispatch. Authorization is checked before takeover.</summary>
public sealed class ConversationMutationFilter(RecruitmentCrmContext db, AiSalesControlService controls) : IAsyncActionFilter
{
    private static readonly HashSet<string> Sends = new() { "SendMessage", "SendMedia", "Forward", "SendTemplate", "Assign" };
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.RouteData.Values["controller"]?.ToString() != "Conversations" ||
            !Sends.Contains(context.RouteData.Values["action"]?.ToString() ?? "") ||
            !context.ActionArguments.TryGetValue("id", out var value) || value is not Guid id)
        { await next(); return; }
        await using var gate = await ConversationLock.AcquireAsync(db, "conversation:" + id, context.HttpContext.RequestAborted);
        var chat = await db.WhatsAppConversations.SingleOrDefaultAsync(c => c.Id == id);
        var user = context.HttpContext.User;
        if (chat == null || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
        { await next(); return; }
        var visibility = await WhatsAppScope.ForUserAsync(db, user.FindFirstValue(ClaimTypes.Role), actor);
        if (!await db.WhatsAppConversations.ApplyVisibility(visibility).AnyAsync(c => c.Id == id))
        { context.Result = new ForbidResult(); return; }
        if (chat.AssignedAiSalesAgentId != null && context.RouteData.Values["action"]?.ToString() == "Assign")
        { context.Result = new ConflictObjectResult(new { error = "Release the AI assignment from AI Sales before assigning a human owner." }); return; }
        await controls.TakeOverLockedAsync(chat, actor, "Staff initiated an inbox message or assignment.", context.HttpContext.RequestAborted);
        await db.SaveChangesAsync(context.HttpContext.RequestAborted);
        await next();
    }
}
