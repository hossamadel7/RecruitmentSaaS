using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace RecruitmentSaaS.Services;

/// <summary>Monitoring never sends messages, changes customer records or clears staff unread counts.</summary>
public sealed class MonitoringReadOnlyFilter : IAsyncAuthorizationFilter, IOrderedFilter
{
    // Run before conversation takeover and all other action filters.
    public int Order => int.MinValue;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        var logout = context.RouteData.Values["controller"]?.ToString() == "Auth"
            && context.RouteData.Values["action"]?.ToString() == "Logout";
        if (WhatsAppAuthorization.IsReadOnlyMonitor(context.HttpContext.User.FindFirstValue(ClaimTypes.Role))
            && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)
            && !HttpMethods.IsOptions(request.Method) && !logout)
        {
            context.Result = new ForbidResult();
        }
        return Task.CompletedTask;
    }
}
