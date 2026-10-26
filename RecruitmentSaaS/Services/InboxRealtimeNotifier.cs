using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using RecruitmentSaaS.Hubs;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Central place that decides which SignalR groups hear about which inbox events,
    /// so controllers don't need to know the hub's group-naming scheme.
    /// </summary>
    public interface IInboxRealtimeNotifier
    {
        Task NewMessageAsync(WhatsAppMessage message, Guid? assignedAgentId, string? senderUserName = null);

        Task MessageStatusUpdatedAsync(Guid messageId, Guid conversationId, byte status, string? errorMessage);

        Task ConversationUpdatedAsync(Guid conversationId, Guid whatsAppAccountId, Guid? assignedAgentId);

        Task ConversationAssignedAsync(Guid conversationId, Guid whatsAppAccountId, Guid? previousAgentId, Guid? newAgentId);

        Task UnreadCountUpdatedAsync(Guid conversationId, Guid whatsAppAccountId, int unreadCount, Guid? assignedAgentId);
    }

    public class InboxRealtimeNotifier : IInboxRealtimeNotifier
    {
        private readonly IHubContext<InboxHub> _hub;
        private readonly RecruitmentSaaS.Data.RecruitmentCrmContext _context;

        public InboxRealtimeNotifier(IHubContext<InboxHub> hub, RecruitmentSaaS.Data.RecruitmentCrmContext context)
        {
            _hub = hub;
            _context = context;
        }

        public async Task NewMessageAsync(WhatsAppMessage message, Guid? assignedAgentId, string? senderUserName = null)
        {
            var payload = new
            {
                message.Id,
                message.ConversationId,
                message.WhatsAppAccountId,
                message.Direction,
                message.MessageType,
                message.TextBody,
                message.MediaUrl,
                HasMedia = message.MediaId != null,
                message.Status,
                message.WhatsAppTimestamp,
                message.CreatedAt,
                message.SenderUserId,
                message.SenderAiSalesAgentId,
                SenderUserName = senderUserName ?? message.SenderUser?.FullName
            };

            await BroadcastAsync("NewMessage", message.ConversationId, assignedAgentId, payload);
        }

        public async Task MessageStatusUpdatedAsync(Guid messageId, Guid conversationId, byte status, string? errorMessage)
        {
            var payload = new { messageId, conversationId, status, errorMessage };
            await BroadcastAsync("MessageStatusUpdated", conversationId, null, payload);
        }

        public async Task ConversationUpdatedAsync(Guid conversationId, Guid whatsAppAccountId, Guid? assignedAgentId)
        {
            var payload = new { conversationId, whatsAppAccountId };
            await BroadcastAsync("ConversationUpdated", conversationId, assignedAgentId, payload);
        }

        public async Task ConversationAssignedAsync(Guid conversationId, Guid whatsAppAccountId, Guid? previousAgentId, Guid? newAgentId)
        {
            var payload = new { conversationId, whatsAppAccountId, previousAgentId, newAgentId };

            await BroadcastAsync("ConversationAssigned", conversationId, newAgentId, payload);
            if (previousAgentId.HasValue)
                await _hub.Clients.User(previousAgentId.Value.ToString()).SendAsync("ConversationAssigned", payload);

        }

        public async Task UnreadCountUpdatedAsync(Guid conversationId, Guid whatsAppAccountId, int unreadCount, Guid? assignedAgentId)
        {
            var payload = new { conversationId, whatsAppAccountId, unreadCount };
            await BroadcastAsync("UnreadCountUpdated", conversationId, assignedAgentId, payload);
        }

        // Groups (see InboxHub): "org-all" = admin, "org-admin" = admin only,
        // "team-waiting-{leaderId}" = that team's leader + TeleSales manager.
        private async Task BroadcastAsync(string eventName, Guid conversationId, Guid? assignedAgentId, object payload)
        {
            var chat = await _context.WhatsAppConversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == conversationId);
            if (chat == null) return;
            var ownerTeam = chat.AssignedSalesAgentId == null ? null : await _context.Users
                .Where(u => u.Id == chat.AssignedSalesAgentId).Select(u => u.ManagerId).SingleOrDefaultAsync();
            var team = chat.PendingTeamManagerId ?? ownerTeam;
            if (team == null && chat.AssignedSalesAgentId == null && chat.AssignedAiSalesAgentId == null)
                team = await WhatsAppScope.TeamOfAccountAsync(_context, chat.WhatsAppAccountId);
            var recipients = await _context.Users.AsNoTracking().Where(u => u.IsActive &&
                (u.Role == 1 || u.Role == 9 || u.Id == chat.AssignedSalesAgentId ||
                 (team != null && ((u.Role == 7 && u.Id == team) || (u.Role == 8 && u.ManagerId == team)))))
                .Select(u => u.Id.ToString()).ToListAsync();
            await _hub.Clients.Users(recipients).SendAsync(eventName, payload);
        }
    }
}
