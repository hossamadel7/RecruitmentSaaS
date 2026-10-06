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

        private readonly IWhatsAppCloudApiService _cloudApi;

        private readonly INotificationService _notifications;
        private readonly AiIntakeService _aiIntake;
        private readonly AiIntakeQueue _aiQueue;

        public WhatsAppWebhookProcessor(
            RecruitmentCrmContext context,
            IInboxRealtimeNotifier notifier,
            ILogger<WhatsAppWebhookProcessor> logger,
            IWhatsAppCloudApiService cloudApi,
            INotificationService notifications,
            AiIntakeService aiIntake,
            AiIntakeQueue aiQueue)
        {
            _notifications = notifications;
            _aiIntake = aiIntake;
            _aiQueue = aiQueue;
            _context = context;
            _notifier = notifier;
            _logger = logger;
            _cloudApi = cloudApi;
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

                    // Manually-added accounts start as Pending; a delivered webhook proves the subscription works
                    if (account.WebhookSubscriptionStatus != (byte)WebhookSubscriptionStatus.Subscribed)
                    {
                        account.WebhookSubscriptionStatus = (byte)WebhookSubscriptionStatus.Subscribed;
                        account.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync(ct);
                    }

                    var contactsJ = value["contacts"] as JArray ?? new JArray();
                    var messagesJ = value["messages"] as JArray ?? new JArray();
                    var statusesJ = value["statuses"] as JArray ?? new JArray();
                    var field = change["field"]?.ToString();

                    foreach (var messageJ in messagesJ)
                        await ProcessInboundMessageAsync(account, contactsJ, messageJ, ct);

                    foreach (var statusJ in statusesJ)
                        await ProcessStatusUpdateAsync(statusJ, ct);

                    // ── Coexistence: outgoing messages the salesperson sent from the
                    // WhatsApp Business mobile app itself, echoed back to us so the shared
                    // inbox stays in sync. Never re-sent through Cloud API — persist only.
                    if (field == "smb_message_echoes" || value["message_echoes"] is JArray)
                    {
                        var echoesJ = value["message_echoes"] as JArray ?? new JArray();
                        foreach (var echoJ in echoesJ)
                            await ProcessMessageEchoAsync(account, echoJ, ct);
                    }

                    // ── Coexistence: one-time chat history sync after onboarding.
                    // Best-effort — the exact payload shape isn't fully documented publicly,
                    // so this never lets a parsing surprise fail the whole webhook.
                    if (field == "history" || value["history"] is JArray)
                    {
                        var historyJ = value["history"] as JArray ?? new JArray();
                        foreach (var historyChunk in historyJ)
                            await ProcessHistoryChunkAsync(account, historyChunk, ct);
                    }
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

            var contact = await FindOrCreateContactAsync(fromWaId, profileName, now, ct);
            var (conversation, isNewConversation) = await FindOrCreateConversationAsync(contact, account, now, ct);

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
                MessageSource = (byte)MessageSource.Customer,
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

            await TryConnectHandoffAsync(conversation, account, fromWaId, textBody, now, ct);
            await TryAssignDirectChatAsync(conversation, textBody, ct);

            // A new chat from someone who isn't a lead yet: the AI assistant collects name / age / job
            var aiOwnsChat = false;
            try { aiOwnsChat = await _aiIntake.ClaimAsync(conversation, account, fromWaId, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "AI assistant check failed for {ConversationId}", conversation.Id); }

            await _context.SaveChangesAsync(ct);

            if (isNewConversation)
            {
                _context.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorType = 2, // system (DB allows 1 = user, 2 = system)
                    EventType = "ConversationCreated",
                    EntityType = "WhatsAppConversation",
                    EntityId = conversation.Id,
                    CreatedAt = now
                });
                await _context.SaveChangesAsync(ct);
            }

            await _notifier.NewMessageAsync(message, conversation.AssignedSalesAgentId);
            await _notifier.UnreadCountUpdatedAsync(conversation.Id, account.Id, conversation.UnreadCount, conversation.AssignedSalesAgentId);

            if (aiOwnsChat)
            {
                _aiQueue.Enqueue(conversation.Id);   // answered a few seconds later, off the webhook
                return;
            }
            await TrySendWelcomeAsync(conversation, account, ct);
        }

        // A chat started from the registration page's "كلمنا على واتساب مباشرة" button carries
        // "(من صفحة التسجيل - <team>)" in its first message: give it to the next salesperson in that
        // team's rotation (or the general one), so it doesn't sit unassigned.
        private async Task TryAssignDirectChatAsync(WhatsAppConversation conversation, string? textBody, CancellationToken ct)
        {
            try
            {
                if (conversation.AssignedSalesAgentId != null || conversation.LeadId != null || string.IsNullOrEmpty(textBody)) return;
                var at = textBody.IndexOf(RecruitmentSaaS.Controllers.HomeController.DirectWhatsAppTag, StringComparison.Ordinal);
                if (at < 0) return;

                var tail = textBody[(at + RecruitmentSaaS.Controllers.HomeController.DirectWhatsAppTag.Length)..];
                var match = System.Text.RegularExpressions.Regex.Match(tail, @"^\s*-\s*([a-z0-9-]+)");
                Guid? teamLeaderId = null;
                if (match.Success)
                {
                    var slug = match.Groups[1].Value;
                    teamLeaderId = await _context.TeamLeadForms.Where(f => f.Slug == slug).Select(f => (Guid?)f.ManagerId).FirstOrDefaultAsync(ct);
                }

                var agentId = teamLeaderId != null
                    ? await LeadDistributor.NextTeleSalesAsync(_context, _context.Users.Where(u => u.ManagerId == teamLeaderId), LeadDistributor.TeamScope(teamLeaderId.Value), ct)
                    : await LeadDistributor.NextTeleSalesAsync(_context, _context.Users, LeadDistributor.AllScope, ct);
                if (agentId == null) return;

                conversation.AssignedSalesAgentId = agentId;
                conversation.PendingTeamManagerId = null;
                await _notifications.SendAsync(agentId.Value, "محادثة واتساب جديدة من صفحة التسجيل",
                    "عميل كلمنا على واتساب مباشرة من الصفحة — اتعينت لك", link: $"/Inbox/Index?c={conversation.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not assign a direct-WhatsApp chat {ConversationId}", conversation.Id);
            }
        }

        // Conversations already welcomed by this server process — the customer often sends several
        // messages at once, and each arrives as its own webhook.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> Welcomed = new();

        /// <summary>
        /// The automatic welcome from the assigned TeleSales (Admin > نموذج التسجيل). Sent once, when
        /// the customer's chat starts: WhatsApp only allows free text after the customer writes first,
        /// so this always lands inside the 24-hour window.
        /// </summary>
        private async Task TrySendWelcomeAsync(WhatsAppConversation conversation, WhatsAppAccount account, CancellationToken ct)
        {
            try
            {
                if (conversation.AssignedSalesAgentId == null) return;   // no salesperson yet — nobody to introduce

                var settings = await _context.LeadFormSettings.AsNoTracking().FirstOrDefaultAsync(ct);
                if (settings?.WelcomeMessageEnabled != true || string.IsNullOrWhiteSpace(settings.WelcomeMessage)) return;

                // Only the very first reply in this chat
                if (await _context.WhatsAppMessages.AnyAsync(m => m.ConversationId == conversation.Id
                                                               && m.Direction == (byte)MessageDirection.Outgoing, ct))
                    return;
                if (!Welcomed.TryAdd(conversation.Id, 0)) return;

                var agent = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == conversation.AssignedSalesAgentId, ct);
                if (agent == null) return;

                var customer = await _context.WhatsAppConversations.AsNoTracking()
                    .Where(c => c.Id == conversation.Id)
                    .Select(c => c.Lead != null ? c.Lead.FullName : c.Contact.Name)
                    .FirstOrDefaultAsync(ct);
                var contactWaId = await _context.WhatsAppContacts.AsNoTracking()
                    .Where(c => c.Id == conversation.ContactId).Select(c => c.WhatsAppPhoneNumber).FirstAsync(ct);

                var text = settings.WelcomeMessage
                    .Replace("{agent}", agent.DisplayNameAr)
                    .Replace("{name}", string.IsNullOrWhiteSpace(customer) ? "" : customer);

                var now = DateTime.UtcNow;
                var welcome = new WhatsAppMessage
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversation.Id,
                    WhatsAppAccountId = account.Id,
                    Direction = (byte)MessageDirection.Outgoing,
                    MessageType = (byte)WhatsAppMessageType.Text,
                    MessageSource = (byte)MessageSource.System,
                    TextBody = text,
                    Status = (byte)MessageStatus.Queued,
                    WhatsAppTimestamp = now,
                    CreatedAt = now
                };
                _context.WhatsAppMessages.Add(welcome);
                await _context.SaveChangesAsync(ct);

                var result = await _cloudApi.SendTextMessageAsync(account.PhoneNumberId, contactWaId, text, ct);
                welcome.Status = result.Success ? (byte)MessageStatus.Sent : (byte)MessageStatus.Failed;
                welcome.WhatsAppMessageId = result.WhatsAppMessageId;
                welcome.ErrorCode = result.ErrorCode;
                welcome.ErrorMessage = result.ErrorMessage;
                await _context.SaveChangesAsync(ct);

                await _notifier.NewMessageAsync(welcome, conversation.AssignedSalesAgentId, "رسالة ترحيب تلقائية");
                if (!result.Success)
                    _logger.LogWarning("Welcome message to conversation {ConversationId} failed: {Error}", conversation.Id, result.ErrorMessage);
            }
            catch (Exception ex)
            {
                // Never let the welcome break processing of the customer's own message
                _logger.LogError(ex, "Could not send the welcome message for conversation {ConversationId}", conversation.Id);
            }
        }

        private async Task<WhatsAppContact> FindOrCreateContactAsync(string waPhoneNumber, string? profileName, DateTime now, CancellationToken ct)
        {
            var contact = await _context.WhatsAppContacts
                .FirstOrDefaultAsync(c => c.WhatsAppPhoneNumber == waPhoneNumber, ct);

            if (contact == null)
            {
                contact = new WhatsAppContact
                {
                    Id = Guid.NewGuid(),
                    WhatsAppPhoneNumber = waPhoneNumber,
                    WhatsAppUserId = waPhoneNumber,
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

            return contact;
        }

        private async Task<(WhatsAppConversation conversation, bool isNew)> FindOrCreateConversationAsync(
            WhatsAppContact contact, WhatsAppAccount account, DateTime now, CancellationToken ct)
        {
            var conversation = await _context.WhatsAppConversations
                .FirstOrDefaultAsync(c => c.ContactId == contact.Id && c.WhatsAppAccountId == account.Id, ct);

            var isNew = conversation == null;

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

            return (conversation, isNew);
        }

        /// <summary>
        /// A message the salesperson sent from the WhatsApp Business mobile app itself
        /// (not through our inbox). Persist only — never resend through Cloud API, and
        /// never bump UnreadCount since this isn't something the CRM needs to flag.
        /// </summary>
        private async Task ProcessMessageEchoAsync(WhatsAppAccount account, JToken echoJ, CancellationToken ct)
        {
            try
            {
                var wamid = echoJ["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(wamid))
                    return;

                var alreadyExists = await _context.WhatsAppMessages
                    .AsNoTracking()
                    .AnyAsync(m => m.WhatsAppMessageId == wamid, ct);
                if (alreadyExists)
                    return;

                var toWaId = echoJ["to"]?.ToString();
                if (string.IsNullOrWhiteSpace(toWaId))
                    return;

                var now = DateTime.UtcNow;

                var contact = await FindOrCreateContactAsync(toWaId, null, now, ct);
                var (conversation, isNewConversation) = await FindOrCreateConversationAsync(contact, account, now, ct);

                var (messageType, textBody, mediaId) = ExtractContent(echoJ);
                var whatsAppTimestamp = ParseUnixTimestamp(echoJ["timestamp"]?.ToString()) ?? now;

                var message = new WhatsAppMessage
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversation.Id,
                    WhatsAppAccountId = account.Id,
                    WhatsAppMessageId = wamid,
                    Direction = (byte)MessageDirection.Outgoing,
                    MessageType = (byte)messageType,
                    MessageSource = (byte)MessageSource.WhatsAppBusinessApp,
                    TextBody = textBody,
                    MediaId = mediaId,
                    Status = (byte)MessageStatus.Sent,
                    WhatsAppTimestamp = whatsAppTimestamp,
                    CreatedAt = now
                };
                _context.WhatsAppMessages.Add(message);

                conversation.LastMessageAt = whatsAppTimestamp;
                conversation.UpdatedAt = now;
                if (conversation.Status == (byte)ConversationStatus.New)
                    conversation.Status = (byte)ConversationStatus.WaitingForCustomer;

                await _context.SaveChangesAsync(ct);

                if (isNewConversation)
                {
                    _context.AuditLogs.Add(new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        ActorType = 2, // system (DB allows 1 = user, 2 = system)
                        EventType = "ConversationCreated",
                        EntityType = "WhatsAppConversation",
                        EntityId = conversation.Id,
                        CreatedAt = now
                    });
                    await _context.SaveChangesAsync(ct);
                }

                await _notifier.NewMessageAsync(message, conversation.AssignedSalesAgentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing smb_message_echoes entry for account {AccountId}.", account.Id);
            }
        }

        /// <summary>
        /// One-time Coexistence chat-history sync. The public documentation for this payload
        /// shape is thinner than the regular messages/statuses fields, so this is deliberately
        /// defensive: it tries the documented "threads[].messages[]" shape, tolerates it being
        /// absent, and never lets an unexpected structure fail the surrounding webhook call.
        /// </summary>
        private async Task ProcessHistoryChunkAsync(WhatsAppAccount account, JToken historyChunk, CancellationToken ct)
        {
            try
            {
                var threadsJ = historyChunk["threads"] as JArray;
                if (threadsJ == null)
                {
                    _logger.LogInformation("Received a 'history' webhook chunk with no recognizable threads[] array for account {AccountId} — skipping.", account.Id);
                    return;
                }

                var now = DateTime.UtcNow;

                foreach (var thread in threadsJ)
                {
                    var threadContactId = thread["id"]?.ToString();
                    var messagesJ = thread["messages"] as JArray;
                    if (string.IsNullOrWhiteSpace(threadContactId) || messagesJ == null)
                        continue;

                    var contact = await FindOrCreateContactAsync(threadContactId, null, now, ct);
                    var (conversation, _) = await FindOrCreateConversationAsync(contact, account, now, ct);

                    foreach (var historyMessageJ in messagesJ)
                    {
                        var wamid = historyMessageJ["id"]?.ToString();
                        if (string.IsNullOrWhiteSpace(wamid))
                            continue;

                        var alreadyExists = await _context.WhatsAppMessages
                            .AsNoTracking()
                            .AnyAsync(m => m.WhatsAppMessageId == wamid, ct);
                        if (alreadyExists)
                            continue;

                        var fromWaId = historyMessageJ["from"]?.ToString();
                        var direction = string.Equals(fromWaId, threadContactId, StringComparison.OrdinalIgnoreCase)
                            ? MessageDirection.Incoming
                            : MessageDirection.Outgoing;

                        var (messageType, textBody, mediaId) = ExtractContent(historyMessageJ);
                        var whatsAppTimestamp = ParseUnixTimestamp(historyMessageJ["timestamp"]?.ToString()) ?? now;

                        _context.WhatsAppMessages.Add(new WhatsAppMessage
                        {
                            Id = Guid.NewGuid(),
                            ConversationId = conversation.Id,
                            WhatsAppAccountId = account.Id,
                            WhatsAppMessageId = wamid,
                            Direction = (byte)direction,
                            MessageType = (byte)messageType,
                            MessageSource = direction == MessageDirection.Incoming ? (byte)MessageSource.Customer : (byte)MessageSource.WhatsAppBusinessApp,
                            TextBody = textBody,
                            MediaId = mediaId,
                            Status = (byte)(direction == MessageDirection.Incoming ? MessageStatus.Received : MessageStatus.Sent),
                            WhatsAppTimestamp = whatsAppTimestamp,
                            CreatedAt = now
                        });

                        if (conversation.LastMessageAt == null || whatsAppTimestamp > conversation.LastMessageAt)
                            conversation.LastMessageAt = whatsAppTimestamp;
                    }

                    conversation.UpdatedAt = now;
                }

                await _context.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing a 'history' webhook chunk for account {AccountId}. Skipping this chunk.", account.Id);
            }
        }

        private async Task TryConnectHandoffAsync(WhatsAppConversation conversation, WhatsAppAccount account,
                                                  string fromWaId, string? textBody, DateTime now, CancellationToken ct)
        {
            if (conversation.LeadId != null)
                return;

            var match = string.IsNullOrWhiteSpace(textBody) ? Match.Empty : HandoffRefPattern.Match(textBody);
            if (!match.Success)
            {
                // No reference code (deleted by the customer, or they messaged the number directly)
                await TryLinkLeadByPhoneAsync(conversation, account, fromWaId, now, ct);
                return;
            }

            var referenceCode = match.Value;

            var handoff = await _context.WhatsAppHandoffs
                .FirstOrDefaultAsync(h => h.ReferenceCode == referenceCode &&
                    (h.Status == (byte)HandoffStatus.Generated || h.Status == (byte)HandoffStatus.Sent), ct);

            if (handoff == null)
            {
                await TryLinkLeadByPhoneAsync(conversation, account, fromWaId, now, ct);
                return;
            }

            conversation.LeadId = handoff.LeadId;
            if (handoff.AssignedSalesAgentId != Guid.Empty)
                conversation.AssignedSalesAgentId = handoff.AssignedSalesAgentId;

            handoff.ConversationId = conversation.Id;
            handoff.Status = (byte)HandoffStatus.Connected;
            handoff.ConnectedAt = now;

            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorType = 2, // system (DB allows 1 = user, 2 = system)
                EventType = "WhatsAppConnected",
                EntityType = "WhatsAppHandoff",
                EntityId = handoff.Id,
                NewValueJson = $"{{\"conversationId\":\"{conversation.Id}\",\"leadId\":\"{handoff.LeadId}\"}}",
                CreatedAt = now
            });
        }

        /// <summary>
        /// Fallback when there is no handoff reference: link the chat to the lead with the same
        /// phone number and hand it to that lead's salesperson. Lead phones are stored as entered
        /// (Egyptian local "01…" from the website form), WhatsApp sends international "201…".
        /// </summary>
        private async Task TryLinkLeadByPhoneAsync(WhatsAppConversation conversation, WhatsAppAccount account,
                                                   string fromWaId, DateTime now, CancellationToken ct)
        {
            var normalized = PhoneNumbers.Normalize(fromWaId);
            if (!PhoneNumbers.IsValid(normalized))
                return;

            var phones = PhoneNumbers.StoredVariants(normalized);

            var lead = await _context.Leads
                .AsNoTracking()
                .Where(l => phones.Contains(l.Phone))
                .Select(l => new { l.Id, l.AssignedSalesId })
                .FirstOrDefaultAsync(ct);

            if (lead == null)
                return;

            conversation.LeadId = lead.Id;

            // Don't take a chat away from someone it was deliberately given to — only replace
            // "nobody" or the number's default agent
            if (lead.AssignedSalesId != null
                && (conversation.AssignedSalesAgentId == null || conversation.AssignedSalesAgentId == account.AssignedSalesAgentId))
            {
                conversation.AssignedSalesAgentId = lead.AssignedSalesId;
            }

            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorType = 2, // system (DB allows 1 = user, 2 = system)
                EventType = "WhatsAppLinkedByPhone",
                EntityType = "WhatsAppConversation",
                EntityId = conversation.Id,
                NewValueJson = $"{{\"leadId\":\"{lead.Id}\",\"assignedTo\":\"{conversation.AssignedSalesAgentId}\"}}",
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

            if (newStatus == MessageStatus.Failed && message.ErrorCode == PhoneNumbers.NotOnWhatsAppErrorCode)
                await NotifyNotOnWhatsAppAsync(message.ConversationId, ct);
        }

        // The customer's number can't get WhatsApp messages — tell their salesperson to call instead
        private async Task NotifyNotOnWhatsAppAsync(Guid conversationId, CancellationToken ct)
        {
            try
            {
                var info = await _context.WhatsAppConversations.AsNoTracking()
                    .Where(c => c.Id == conversationId && c.Lead != null)
                    .Select(c => new { c.Lead!.Id, c.Lead.FullName, c.Lead.Phone, Agent = c.Lead.AssignedSalesId ?? c.AssignedSalesAgentId })
                    .FirstOrDefaultAsync(ct);
                if (info?.Agent == null) return;

                await _notifications.SendAsync(info.Agent.Value, "عميل مش على واتساب — كلّمه تليفون",
                    $"{info.FullName} — {info.Phone}", link: $"/TeleSales/LeadDetail/{info.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send the not-on-WhatsApp notification for conversation {ConversationId}", conversationId);
            }
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
