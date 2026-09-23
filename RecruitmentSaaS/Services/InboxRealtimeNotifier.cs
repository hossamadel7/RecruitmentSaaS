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
        Task NewMessageAsync(WhatsAppMessage message, Guid? assignedAgentId);

        Task MessageStatusUpdatedAsync(Guid messageId, Guid conversationId, byte status, string? errorMessage);

        Task ConversationUpdatedAsync(Guid conversationId, Guid whatsAppAccountId, Guid? assignedAgentId);

        Task ConversationAssignedAsync(Guid conversationId, Guid whatsAppAccountId, Guid? previousAgentId, Guid? newAgentId);

        Task UnreadCountUpdatedAsync(Guid conversationId, Guid whatsAppAccountId, int unreadCount, Guid? assignedAgentId);
    }

    public class InboxRealtimeNotifier : IInboxRealtimeNotifier
    {
        private readonly IHubContext<InboxHub> _hub;

        public InboxRealtimeNotifier(IHubContext<InboxHub> hub)
        {
            _hub = hub;
        }

        public async Task NewMessageAsync(WhatsAppMessage message, Guid? assignedAgentId)
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
                message.Status,
                message.WhatsAppTimestamp,
                message.CreatedAt
            };

            await BroadcastAsync("NewMessage", message.ConversationId, assignedAgentId, payload);
        }

        public async Task MessageStatusUpdatedAsync(Guid messageId, Guid conversationId, byte status, string? errorMessage)
        {
            var payload = new { messageId, conversationId, status, errorMessage };
            await _hub.Clients.Group($"wa-conversation-{conversationId}").SendAsync("MessageStatusUpdated", payload);
            await _hub.Clients.Group("org-all").SendAsync("MessageStatusUpdated", payload);
        }

        public async Task ConversationUpdatedAsync(Guid conversationId, Guid whatsAppAccountId, Guid? assignedAgentId)
        {
            var payload = new { conversationId, whatsAppAccountId };
            await BroadcastAsync("ConversationUpdated", conversationId, assignedAgentId, payload);
        }

        public async Task ConversationAssignedAsync(Guid conversationId, Guid whatsAppAccountId, Guid? previousAgentId, Guid? newAgentId)
        {
            var payload = new { conversationId, whatsAppAccountId, previousAgentId, newAgentId };

            await _hub.Clients.Group("org-all").SendAsync("ConversationAssigned", payload);

            if (previousAgentId.HasValue)
                await _hub.Clients.Group($"user-{previousAgentId}").SendAsync("ConversationAssigned", payload);

            if (newAgentId.HasValue)
                await _hub.Clients.Group($"user-{newAgentId}").SendAsync("ConversationAssigned", payload);
        }

        public async Task UnreadCountUpdatedAsync(Guid conversationId, Guid whatsAppAccountId, int unreadCount, Guid? assignedAgentId)
        {
            var payload = new { conversationId, whatsAppAccountId, unreadCount };
            await BroadcastAsync("UnreadCountUpdated", conversationId, assignedAgentId, payload);
        }

        private async Task BroadcastAsync(string eventName, Guid conversationId, Guid? assignedAgentId, object payload)
        {
            await _hub.Clients.Group("org-all").SendAsync(eventName, payload);
            await _hub.Clients.Group($"wa-conversation-{conversationId}").SendAsync(eventName, payload);

            if (assignedAgentId.HasValue)
                await _hub.Clients.Group($"user-{assignedAgentId}").SendAsync(eventName, payload);
        }
    }
}
