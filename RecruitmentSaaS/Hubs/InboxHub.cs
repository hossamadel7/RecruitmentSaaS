using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using System.Security.Claims;

namespace RecruitmentSaaS.Hubs
{
    /// <summary>
    /// Realtime channel for the WhatsApp Shared Inbox. Group membership is decided
    /// server-side from the caller's identity — clients never choose which org-wide
    /// group they join, only which single conversation they're currently viewing.
    /// </summary>
    [Authorize]
    public class InboxHub : Hub
    {
        private readonly RecruitmentCrmContext _context;

        public InboxHub(RecruitmentCrmContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId => Guid.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private string? CurrentRole => Context.User!.FindFirstValue(ClaimTypes.Role);

        private bool IsOrgWideRole => CurrentRole == "1" /* Admin */ || CurrentRole == "7" /* TeleSalesManager (Supervisor) */;

        public override async Task OnConnectedAsync()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{CurrentUserId}");

            if (IsOrgWideRole)
                await Groups.AddToGroupAsync(Context.ConnectionId, "org-all");

            await base.OnConnectedAsync();
        }

        /// <summary>Join the live feed for one open conversation (typing/read-state awareness).</summary>
        public async Task WatchConversation(Guid conversationId)
        {
            if (!await CanAccessConversationAsync(conversationId)) return;
            await Groups.AddToGroupAsync(Context.ConnectionId, $"wa-conversation-{conversationId}");
        }

        public async Task UnwatchConversation(Guid conversationId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"wa-conversation-{conversationId}");
        }

        private async Task<bool> CanAccessConversationAsync(Guid conversationId)
        {
            if (IsOrgWideRole) return true;

            var userId = CurrentUserId;
            return await _context.WhatsAppConversations
                .AsNoTracking()
                .AnyAsync(c => c.Id == conversationId && c.AssignedSalesAgentId == userId);
        }
    }
}
