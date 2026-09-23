using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Text.RegularExpressions;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Turns a raw Meta WhatsApp Cloud API webhook payload into Contact / Conversation / Message rows.
    /// Kept separate from the controller so the routing, idempotency and handoff-matching logic can be
    /// exercised directly in tests without going through HTTP.
    /// </summary>
    public interface IWhatsAppWebhookProcessor
    {
        Task ProcessAsync(JObject payload, CancellationToken ct = default);
    }

    public class WhatsAppWebhookProcessor : IWhatsAppWebhookProcessor
    {
        private static readonly Regex HandoffRefPattern = new(@"REF-[A-Z0-9]{6}", RegexOptions.Compiled);

        private readonly RecruitmentCrmContext _context;
        private readonly IInboxRealtimeNotifier _notifier;
        private readonly ILogger<WhatsAppWebhookProcessor> _logger;

        public WhatsAppWebhookProcessor(
            RecruitmentCrmContext context,
            IInboxRealtimeNotifier notifier,
            ILogger<WhatsAppWebhookProcessor> logger)
        {
            _context = context;
            _notifier = notifier;
            _logger = logger;
        }

        public async Task ProcessAsync(JObject payload, CancellationToken ct = default)
        {
            var entries = payload["entry"] as JArray ?? new JArray();

            foreach (var entry in entries)
            {
                var changes = entry["changes"] as JArray ?? new JArray();

                foreach (var change in changes)
                {
                    var value = change["value"];
                    if (value == null) continue;

                    var phoneNumberId = value["metadata"]?["phone_number_id"]?.ToString();
                    if (string.IsNullOrWhiteSpace(phoneNumberId))
                        continue;

                    var account = await _context.WhatsAppAccounts
                        .FirstOrDefaultAsync(a => a.PhoneNumberId == phoneNumberId, ct);

                    if (account == null)
                    {
                        _logger.LogWarning("Webhook referenced unknown PhoneNumberId={PhoneNumberId}. Ignoring.", phoneNumberId);
                        continue;
                    }

                    var contactsJ = value["contacts"] as JArray ?? new JArray();
                    var messagesJ = value["messages"] as JArray ?? new JArray();
                    var statusesJ = value["statuses"] as JArray ?? new JArray();

                    foreach (var messageJ in messagesJ)
                        await ProcessInboundMessageAsync(account, contactsJ, messageJ, ct);

                    foreach (var statusJ in statusesJ)
                        await ProcessStatusUpdateAsync(statusJ, ct);
                }
            }
        }

        private async Task ProcessInboundMessageAsync(WhatsAppAccount account, JArray contactsJ, JToken messageJ, CancellationToken ct)
        {
            var wamid = messageJ["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(wamid))
                return;

            // Idempotency — Meta can (and will) redeliver the same webhook more than once.
            var alreadyExists = await _context.WhatsAppMessages
                .AsNoTracking()
                .AnyAsync(m => m.WhatsAppMessageId == wamid, ct);
            if (alreadyExists)
                return;

            var fromWaId = messageJ["from"]?.ToString();
            if (string.IsNullOrWhiteSpace(fromWaId))
                return;

            var profileName = contactsJ
                .FirstOrDefault(c => string.Equals(c["wa_id"]?.ToString(), fromWaId, StringComparison.OrdinalIgnoreCase))
                ?["profile"]?["name"]?.ToString();

            var now = DateTime.UtcNow;

            var contact = await _context.WhatsAppContacts
                .FirstOrDefaultAsync(c => c.WhatsAppPhoneNumber == fromWaId, ct);

            if (contact == null)
            {
                contact = new WhatsAppContact
                {
                    Id = Guid.NewGuid(),
                    WhatsAppPhoneNumber = fromWaId,
                    WhatsAppUserId = fromWaId,
                    Name = profileName,
                    CreatedAt = now,
                    LastSeenAt = now
                };
                _context.WhatsAppContacts.Add(contact);
            }
            else
            {
                contact.LastSeenAt = now;
                if (string.IsNullOrWhiteSpace(contact.Name) && !string.IsNullOrWhiteSpace(profileName))
                    contact.Name = profileName;
                contact.UpdatedAt = now;
            }

            var conversation = await _context.WhatsAppConversations
                .FirstOrDefaultAsync(c => c.ContactId == contact.Id && c.WhatsAppAccountId == account.Id, ct);

            var isNewConversation = conversation == null;

            if (conversation == null)
            {
                conversation = new WhatsAppConversation
                {
                    Id = Guid.NewGuid(),
                    ContactId = contact.Id,
                    WhatsAppAccountId = account.Id,
                    AssignedSalesAgentId = account.AssignedSalesAgentId,
                    Status = (byte)ConversationStatus.New,
                    LeadStage = (byte)LeadStage.New,
                    UnreadCount = 0,
                    OpenedAt = now,
                    CreatedAt = now
                };
                _context.WhatsAppConversations.Add(conversation);
            }
            else if (conversation.Status == (byte)ConversationStatus.Closed || conversation.Status == (byte)ConversationStatus.WaitingForCustomer)
            {
                conversation.Status = (byte)ConversationStatus.Open;
            }

            var (messageType, textBody, mediaId) = ExtractContent(messageJ);

            Guid? replyToMessageId = null;
            var contextWamid = messageJ["context"]?["id"]?.ToString();
            if (!string.IsNullOrWhiteSpace(contextWamid))
            {
                replyToMessageId = await _context.WhatsAppMessages
                    .Where(m => m.WhatsAppMessageId == contextWamid)
                    .Select(m => (Guid?)m.Id)
                    .FirstOrDefaultAsync(ct);
            }

            var whatsAppTimestamp = ParseUnixTimestamp(messageJ["timestamp"]?.ToString()) ?? now;

            var message = new WhatsAppMessage
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                WhatsAppAccountId = account.Id,
                WhatsAppMessageId = wamid,
                Direction = (byte)MessageDirection.Incoming,
                MessageType = (byte)messageType,
                TextBody = textBody,
                MediaId = mediaId,
                Status = (byte)MessageStatus.Received,
                ReplyToMessageId = replyToMessageId,
                WhatsAppTimestamp = whatsAppTimestamp,
                CreatedAt = now
            };
            _context.WhatsAppMessages.Add(message);

            conversation.UnreadCount += 1;
            conversation.LastMessageAt = whatsAppTimestamp;
            conversation.UpdatedAt = now;

            await TryConnectHandoffAsync(conversation, textBody, now, ct);

            await _context.SaveChangesAsync(ct);

            if (isNewConversation)
            {
                _context.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorType = 3, // system
                    EventType = "ConversationCreated",
                    EntityType = "WhatsAppConversation",
                    EntityId = conversation.Id,
                    CreatedAt = now
                });
                await _context.SaveChangesAsync(ct);
            }

            await _notifier.NewMessageAsync(message, conversation.AssignedSalesAgentId);
            await _notifier.UnreadCountUpdatedAsync(conversation.Id, account.Id, conversation.UnreadCount, conversation.AssignedSalesAgentId);
        }

        private async Task TryConnectHandoffAsync(WhatsAppConversation conversation, string? textBody, DateTime now, CancellationToken ct)
        {
            if (conversation.LeadId != null || string.IsNullOrWhiteSpace(textBody))
                return;

            var match = HandoffRefPattern.Match(textBody);
            if (!match.Success)
                return;

            var referenceCode = match.Value;

            var handoff = await _context.WhatsAppHandoffs
                .FirstOrDefaultAsync(h => h.ReferenceCode == referenceCode &&
                    (h.Status == (byte)HandoffStatus.Generated || h.Status == (byte)HandoffStatus.Sent), ct);

            if (handoff == null)
                return;

            conversation.LeadId = handoff.LeadId;
            if (handoff.AssignedSalesAgentId != Guid.Empty)
                conversation.AssignedSalesAgentId = handoff.AssignedSalesAgentId;

            handoff.ConversationId = conversation.Id;
            handoff.Status = (byte)HandoffStatus.Connected;
            handoff.ConnectedAt = now;

            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorType = 3,
                EventType = "WhatsAppConnected",
                EntityType = "WhatsAppHandoff",
                EntityId = handoff.Id,
                NewValueJson = $"{{\"conversationId\":\"{conversation.Id}\",\"leadId\":\"{handoff.LeadId}\"}}",
                CreatedAt = now
            });
        }

        private async Task ProcessStatusUpdateAsync(JToken statusJ, CancellationToken ct)
        {
            var wamid = statusJ["id"]?.ToString();
            var statusStr = statusJ["status"]?.ToString();
            if (string.IsNullOrWhiteSpace(wamid) || string.IsNullOrWhiteSpace(statusStr))
                return;

            var newStatus = statusStr switch
            {
                "sent" => MessageStatus.Sent,
                "delivered" => MessageStatus.Delivered,
                "read" => MessageStatus.Read,
                "failed" => MessageStatus.Failed,
                _ => (MessageStatus?)null
            };
            if (newStatus == null)
                return;

            var message = await _context.WhatsAppMessages
                .FirstOrDefaultAsync(m => m.WhatsAppMessageId == wamid, ct);
            if (message == null)
                return; // status for a message we haven't recorded yet (or don't own) — safe to ignore

            // Idempotent / monotonic — never let a redelivered "delivered" event overwrite a later "read".
            if (message.Status >= (byte)newStatus && newStatus != MessageStatus.Failed)
                return;

            message.Status = (byte)newStatus;

            if (newStatus == MessageStatus.Failed)
            {
                var error = (statusJ["errors"] as JArray)?.FirstOrDefault();
                message.ErrorCode = error?["code"]?.ToString();
                message.ErrorMessage = error?["title"]?.ToString() ?? error?["message"]?.ToString();
            }

            await _context.SaveChangesAsync(ct);

            await _notifier.MessageStatusUpdatedAsync(message.Id, message.ConversationId, message.Status, message.ErrorMessage);
        }

        private static (WhatsAppMessageType type, string? textBody, string? mediaId) ExtractContent(JToken messageJ)
        {
            var type = messageJ["type"]?.ToString() ?? "unknown";

            return type switch
            {
                "text" => (WhatsAppMessageType.Text, messageJ["text"]?["body"]?.ToString(), null),
                "image" => (WhatsAppMessageType.Image, messageJ["image"]?["caption"]?.ToString(), messageJ["image"]?["id"]?.ToString()),
                "video" => (WhatsAppMessageType.Video, messageJ["video"]?["caption"]?.ToString(), messageJ["video"]?["id"]?.ToString()),
                "audio" => (WhatsAppMessageType.Audio, null, messageJ["audio"]?["id"]?.ToString()),
                "document" => (WhatsAppMessageType.Document, messageJ["document"]?["caption"]?.ToString() ?? messageJ["document"]?["filename"]?.ToString(), messageJ["document"]?["id"]?.ToString()),
                "sticker" => (WhatsAppMessageType.Sticker, null, messageJ["sticker"]?["id"]?.ToString()),
                "location" => (WhatsAppMessageType.Location, FormatLocation(messageJ["location"]), null),
                "contacts" => (WhatsAppMessageType.Contacts, "Contact card", null),
                "interactive" => (WhatsAppMessageType.Interactive, ExtractInteractiveText(messageJ["interactive"]), null),
                "button" => (WhatsAppMessageType.Interactive, messageJ["button"]?["text"]?.ToString(), null),
                _ => (WhatsAppMessageType.Unknown, null, null)
            };
        }

        private static string? FormatLocation(JToken? location)
        {
            if (location == null) return null;
            var lat = location["latitude"]?.ToString();
            var lng = location["longitude"]?.ToString();
            var name = location["name"]?.ToString();
            return string.IsNullOrWhiteSpace(name) ? $"{lat},{lng}" : $"{name} ({lat},{lng})";
        }

        private static string? ExtractInteractiveText(JToken? interactive)
        {
            if (interactive == null) return null;
            return interactive["button_reply"]?["title"]?.ToString()
                ?? interactive["list_reply"]?["title"]?.ToString();
        }

        private static DateTime? ParseUnixTimestamp(string? seconds)
        {
            if (string.IsNullOrWhiteSpace(seconds) || !long.TryParse(seconds, out var s))
                return null;
            return DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime;
        }
    }
}
