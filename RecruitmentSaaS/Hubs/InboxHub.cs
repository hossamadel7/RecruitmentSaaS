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

            var visibility = await WhatsAppScope.ForUserAsync(_context, CurrentRole, CurrentUserId);
            if (visibility.AllWaiting)
                await Groups.AddToGroupAsync(Context.ConnectionId, "org-admin");          // every team's waiting chats
            else if (visibility.TeamLeaderId != null)
                await Groups.AddToGroupAsync(Context.ConnectionId, $"team-waiting-{visibility.TeamLeaderId}");

            if (CurrentRole == "8" && visibility.Agents != null)
            {
                // Receive whatever each team member receives (their chats' messages and assignments)
                foreach (var memberId in visibility.Agents.Where(id => id != CurrentUserId))
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
            var visibility = await WhatsAppScope.ForUserAsync(_context, CurrentRole, CurrentUserId);
            return await _context.WhatsAppConversations
                .AsNoTracking()
                .ApplyVisibility(visibility)
                .AnyAsync(c => c.Id == conversationId);
        }
    }
}
