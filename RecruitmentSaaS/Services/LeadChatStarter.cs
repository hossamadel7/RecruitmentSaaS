using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services
{
    public record LeadChatStartResult(bool Success, Guid? ConversationId, bool TemplateSent, string? Error);

    /// <summary>
    /// Opens a lead's WhatsApp chat: finds or creates the contact and chat (assigned to the lead's
    /// salesperson, on the team's number) and, unless the customer wrote in the last 24 hours, sends
    /// the approved opening template — WhatsApp only lets a business write first with a template.
    /// Used by the "بدء محادثة" button and by the automatic follow-up for leads who never wrote.
    /// </summary>
    public class LeadChatStarter
    {
        private readonly RecruitmentCrmContext _context;
        private readonly IWhatsAppCloudApiService _cloudApi;
        private readonly IInboxRealtimeNotifier _notifier;
        private readonly IConfiguration _config;

        public LeadChatStarter(RecruitmentCrmContext context, IWhatsAppCloudApiService cloudApi,
                               IInboxRealtimeNotifier notifier, IConfiguration config)
        {
            _context = context;
            _cloudApi = cloudApi;
            _notifier = notifier;
            _config = config;
        }

        /// <param name="actorUserId">Who pressed the button; null for the automatic follow-up.</param>
        public async Task<LeadChatStartResult> StartAsync(Lead lead, Guid? actorUserId, string? actorName, CancellationToken ct = default)
        {
            if (lead.AssignedSalesId == null)
                return new(false, null, false, "العميل لسه مش متعيّن لموظف");

            var agent = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == lead.AssignedSalesId, ct);
            var waId = PhoneNumbers.ToWhatsAppId(PhoneNumbers.Normalize(lead.Phone));
            if (!PhoneNumbers.IsValid(waId))
                return new(false, null, false, "رقم العميل غير صحيح");

            var account = await AccountForAsync(lead, agent, ct);
            if (account == null) return new(false, null, false, "مفيش رقم واتساب متصل");

            var now = DateTime.UtcNow;
            var contact = await _context.WhatsAppContacts.FirstOrDefaultAsync(c => c.WhatsAppPhoneNumber == waId, ct);
            if (contact == null)
            {
                contact = new WhatsAppContact
                {
                    Id = Guid.NewGuid(),
                    WhatsAppPhoneNumber = waId,
                    WhatsAppUserId = waId,
                    Name = lead.FullName,
                    CreatedAt = now
                };
                _context.WhatsAppContacts.Add(contact);
            }

            var conversation = await _context.WhatsAppConversations
                .FirstOrDefaultAsync(c => c.ContactId == contact.Id && c.WhatsAppAccountId == account.Id, ct);
            if (conversation == null)
            {
                conversation = new WhatsAppConversation
                {
                    Id = Guid.NewGuid(),
                    ContactId = contact.Id,
                    WhatsAppAccountId = account.Id,
                    AssignedSalesAgentId = agent.Id,
                    LeadId = lead.Id,
                    Status = (byte)ConversationStatus.New,
                    LeadStage = (byte)LeadStage.New,
                    UnreadCount = 0,
                    OpenedAt = now,
                    CreatedAt = now
                };
                _context.WhatsAppConversations.Add(conversation);
            }
            else
            {
                conversation.LeadId ??= lead.Id;
                if (conversation.AssignedSalesAgentId == null)
                {
                    conversation.AssignedSalesAgentId = agent.Id;
                    conversation.PendingTeamManagerId = null;
                }
            }
            await _context.SaveChangesAsync(ct);

            // Customer wrote within 24h → normal messages are allowed, just open the chat
            var lastIncoming = await _context.WhatsAppMessages
                .Where(m => m.ConversationId == conversation.Id && m.Direction == (byte)MessageDirection.Incoming)
                .MaxAsync(m => (DateTime?)m.WhatsAppTimestamp, ct);
            if (lastIncoming != null && lastIncoming > now.AddHours(-24))
                return new(true, conversation.Id, false, null);

            var templateName = _config["WhatsApp:StartTemplateName"] ?? "lead_followup";
            var templateLanguage = _config["WhatsApp:StartTemplateLanguage"] ?? "ar_EG";
            var templateText = _config["WhatsApp:StartTemplateText"]
                ?? "أهلاً أ/ {{1}}\n\nبخصوص طلب التسجيل اللي قدمته على موقع *الفهد العربي لإلحاق العمالة بالخارج* (ترخيص رقم 492)، معاك أ/ {{2}} المسؤولة عن متابعة طلبك.\n\nمحتاجين نكمل بيانات طلبك عشان نحدد الفرصة المناسبة ليك حسب السن والمهنة والخبرة.";
            var customerName = string.IsNullOrWhiteSpace(lead.FullName) ? "حضرتك" : lead.FullName.Trim();
            var parameters = new[] { customerName, agent.DisplayNameAr };

            var message = new WhatsAppMessage
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                WhatsAppAccountId = account.Id,
                Direction = (byte)MessageDirection.Outgoing,
                MessageType = (byte)WhatsAppMessageType.Text,
                MessageSource = actorUserId == null ? (byte)MessageSource.System : (byte)MessageSource.CloudApi,
                TextBody = templateText.Replace("\\n", "\n").Replace("{{1}}", parameters[0]).Replace("{{2}}", parameters[1]),
                SenderUserId = actorUserId,
                Status = (byte)MessageStatus.Queued,
                WhatsAppTimestamp = now,
                CreatedAt = now
            };
            _context.WhatsAppMessages.Add(message);
            conversation.LastMessageAt = now;
            conversation.UpdatedAt = now;
            conversation.Status = (byte)ConversationStatus.WaitingForCustomer;
            await _context.SaveChangesAsync(ct);

            var result = await _cloudApi.SendTemplateMessageAsync(account.PhoneNumberId, waId, templateName, templateLanguage, parameters, ct);
            message.Status = result.Success ? (byte)MessageStatus.Sent : (byte)MessageStatus.Failed;
            message.WhatsAppMessageId = result.WhatsAppMessageId;
            message.ErrorCode = result.ErrorCode;
            message.ErrorMessage = result.ErrorMessage;
            if (result.Success) lead.LastContactedAt = now;
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorId = actorUserId,
                ActorType = actorUserId == null ? (byte)2 : (byte)1,   // DB allows 1 = user, 2 = system
                EventType = actorUserId == null ? "ChatStartedAutomatically" : "ChatStartedWithTemplate",
                EntityType = "WhatsAppConversation",
                EntityId = conversation.Id,
                CreatedAt = now
            });
            await _context.SaveChangesAsync(ct);

            await _notifier.NewMessageAsync(message, conversation.AssignedSalesAgentId, actorName ?? "رسالة متابعة تلقائية");
            return result.Success
                ? new(true, conversation.Id, true, null)
                : new(false, conversation.Id, false, "تعذر إرسال رسالة البداية: " + (result.ErrorMessage ?? ""));
        }

        // The team's number (team form), else the general sales number, else any active number
        private async Task<WhatsAppAccount?> AccountForAsync(Lead lead, User agent, CancellationToken ct)
        {
            var teamLeaderId = lead.TeamManagerId ?? agent.ManagerId;
            var teamNumber = teamLeaderId == null ? null
                : await _context.TeamLeadForms.Where(f => f.ManagerId == teamLeaderId).Select(f => f.WhatsAppNumber).FirstOrDefaultAsync(ct);
            var generalNumber = await _context.LeadFormSettings.Select(s => s.SalesWhatsAppNumber).FirstOrDefaultAsync(ct);
            var accounts = await _context.WhatsAppAccounts.Where(a => a.IsActive).ToListAsync(ct);

            static string Digits(string? v) => new string((v ?? "").Where(char.IsDigit).ToArray());
            return accounts.FirstOrDefault(a => Digits(teamNumber) != "" && Digits(a.DisplayPhoneNumber) == Digits(teamNumber))
                ?? accounts.FirstOrDefault(a => Digits(generalNumber) != "" && Digits(a.DisplayPhoneNumber) == Digits(generalNumber))
                ?? accounts.FirstOrDefault();
        }
    }

    /// <summary>
    /// Website leads at/above the age threshold who were sent to WhatsApp but never started the chat:
    /// after the delay set on نموذج التسجيل, the opening template goes out automatically from their
    /// salesperson. Each lead is tried once (the chat it creates marks it as done).
    /// </summary>
    public class LeadAutoFollowupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LeadAutoFollowupService> _logger;
        // Leads that failed before a chat could be created (e.g. a bad phone) — don't retry every minute
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> Tried = new();
        private const string PaymentErrorCode = "131042";

        public LeadAutoFollowupService(IServiceScopeFactory scopeFactory, ILogger<LeadAutoFollowupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(40), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await RunAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Automatic WhatsApp follow-up failed");
                }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentCrmContext>();

            var settings = await db.LeadFormSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            if (settings?.AutoFollowupEnabled != true || settings.AutoFollowupEnabledAt == null) return;

            var now = DateTime.UtcNow;
            var delay = settings.AutoFollowupDelayMinutes > 0 ? settings.AutoFollowupDelayMinutes : LeadFormSetting.DefaultAutoFollowupDelayMinutes;
            var due = now.AddMinutes(-delay);
            var oldest = now.AddHours(-24);                       // never chase old leads
            var since = settings.AutoFollowupEnabledAt.Value;     // only leads that came after it was turned on
            var threshold = settings.SeniorAgeThreshold;

            var leads = await db.Leads
                .Where(l => l.LeadSource == 2                      // website form
                         && l.Age != null && l.Age >= threshold
                         && l.AssignedSalesId != null
                         && !l.IsConverted && !l.IsDuplicate
                         && l.CreatedAt <= due && l.CreatedAt >= oldest && l.CreatedAt >= since
                         // no chat yet — or only our opening message, which Meta refused for billing
                         // (131042: no payment method); that gets one retry once billing is fixed
                         && !db.WhatsAppConversations.Any(c => c.LeadId == l.Id
                                && (c.WhatsAppMessages.Any(m => m.Direction == (byte)MessageDirection.Incoming)
                                    || c.WhatsAppMessages.Any(m => m.Direction == (byte)MessageDirection.Outgoing && m.ErrorCode != PaymentErrorCode)
                                    || c.WhatsAppMessages.Count(m => m.ErrorCode == PaymentErrorCode) >= 2))
                         && !db.WhatsAppHandoffs.Any(h => h.LeadId == l.Id && h.ConversationId != null))
                .OrderBy(l => l.CreatedAt)
                .Take(20)
                .ToListAsync(ct);
            leads = leads.Where(l => !Tried.ContainsKey(l.Id)).ToList();
            if (leads.Count == 0) return;

            var starter = scope.ServiceProvider.GetRequiredService<LeadChatStarter>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            foreach (var lead in leads)
            {
                Tried.TryAdd(lead.Id, 0);
                var result = await starter.StartAsync(lead, null, null, ct);
                if (!result.Success)
                {
                    _logger.LogWarning("Automatic follow-up for lead {LeadId} failed: {Error}", lead.Id, result.Error);
                    continue;
                }
                try
                {
                    await notifications.SendAsync(lead.AssignedSalesId!.Value, "اتبعتت رسالة متابعة تلقائية",
                        $"{lead.FullName} سجّل وما بدأش المحادثة — اتبعتله رسالة البداية باسمك",
                        link: $"/Inbox/Index?c={result.ConversationId}");
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Follow-up sent but the notification failed for lead {LeadId}", lead.Id); }
            }
        }
    }
}
