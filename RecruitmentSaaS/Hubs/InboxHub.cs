using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Services;
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

        private bool IsOrgWideRole => WhatsAppAuthorization.IsOrgWide(CurrentRole); // Admin, team leaders, head TeleSales

        public override async Task OnConnectedAsync()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{CurrentUserId}");

            if (IsOrgWideRole)
                await Groups.AddToGroupAsync(Context.ConnectionId, "org-all");
            else if (CurrentRole == "8")
            {
                // Receive whatever each team member receives (their chats' messages and assignments)
                var visible = await WhatsAppScope.VisibleAgentIdsAsync(_context, CurrentRole, CurrentUserId) ?? new();
                foreach (var memberId in visible.Where(id => id != CurrentUserId))
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{memberId}");
            }

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

            var visible = await WhatsAppScope.VisibleAgentIdsAsync(_context, CurrentRole, CurrentUserId) ?? new();
            return await _context.WhatsAppConversations
                .AsNoTracking()
                .AnyAsync(c => c.Id == conversationId && c.AssignedSalesAgentId != null && visible.Contains(c.AssignedSalesAgentId.Value));
        }
    }
}
