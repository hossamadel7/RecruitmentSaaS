using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Scripted WhatsApp assistant ("المساعد الآلي", free — no AI service). For a customer who writes to a team's number directly
    /// (no form, not already a lead) it asks name, age and job, then creates the lead and routes it the
    /// same way the registration form does: age at/above the threshold → the team's rotation, under it
    /// → waits with the team leader. Anything it can't handle goes to the team leader.
    /// </summary>
    public class AiIntakeService
    {
        public const int MaxTurns = 10;
        public const byte LeadSourceWhatsApp = 5;
        private const string AssistantName = "المساعد الآلي";

        private readonly RecruitmentCrmContext _context;
        private readonly IWhatsAppCloudApiService _cloudApi;
        private readonly IInboxRealtimeNotifier _notifier;
        private readonly INotificationService _notifications;
        private readonly ILogger<AiIntakeService> _logger;

        public AiIntakeService(RecruitmentCrmContext context, IWhatsAppCloudApiService cloudApi, IInboxRealtimeNotifier notifier,
                               INotificationService notifications, ILogger<AiIntakeService> logger)
        {
            _context = context;
            _cloudApi = cloudApi;
            _notifier = notifier;
            _notifications = notifications;
            _logger = logger;
        }

        private static string Digits(string? v) => new string((v ?? "").Where(char.IsDigit).ToArray());

        // ── Start ───────────────────────────────────────────────────────────

        /// <summary>
        /// Called by the webhook for every customer message. Returns true when the assistant owns this
        /// chat (newly started or already collecting) — the caller then queues it for a reply.
        /// </summary>
        public async Task<bool> ClaimAsync(WhatsAppConversation conversation, WhatsAppAccount account, string fromWaId, CancellationToken ct)
        {
            if (conversation.IntakeStatus == (byte)IntakeStatus.Collecting)
            {
                conversation.IntakeLastCustomerAt = DateTime.UtcNow;
                return true;
            }
            if (conversation.IntakeStatus != (byte)IntakeStatus.None) return false;

            // Only brand-new, unowned chats: a known lead, a handoff or a person already on it wins
            if (conversation.LeadId != null || conversation.AssignedSalesAgentId != null || conversation.PendingTeamManagerId != null)
                return false;
            if (!account.AiAssistantEnabled) return false;

            var settings = await _context.LeadFormSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            if (settings?.AiEnabled != true) return false;
            if (settings.AiTestMode)
            {
                var allowed = (settings.AiTestNumbers ?? "")
                    .Split(new[] { ',', '\n', '\r', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Digits).Where(d => d.Length >= 8).ToList();
                var from = Digits(fromWaId);
                if (!allowed.Any(a => from.EndsWith(a.TrimStart('0')) || a.EndsWith(from))) return false;
            }

            // Has our side already written in this chat? Then it isn't a fresh inbound chat
            var hasOutgoing = await _context.WhatsAppMessages.AnyAsync(m => m.ConversationId == conversation.Id
                                                                          && m.Direction == (byte)MessageDirection.Outgoing, ct);
            if (hasOutgoing) return false;

            var now = DateTime.UtcNow;
            conversation.IntakeStatus = (byte)IntakeStatus.Collecting;
            conversation.IntakeStartedAt = now;
            conversation.IntakeLastCustomerAt = now;
            return true;
        }

        // ── One assistant turn ──────────────────────────────────────────────

        public async Task ReplyAsync(Guid conversationId, CancellationToken ct)
        {
            var conversation = await _context.WhatsAppConversations
                .Include(c => c.Contact).Include(c => c.WhatsAppAccount)
                .FirstOrDefaultAsync(c => c.Id == conversationId, ct);
            if (conversation == null || conversation.IntakeStatus != (byte)IntakeStatus.Collecting) return;

            var settings = await _context.LeadFormSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new LeadFormSetting();
            var history = await _context.WhatsAppMessages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.WhatsAppTimestamp).ThenBy(m => m.CreatedAt)
                .ToListAsync(ct);

            // Customer's new messages since our last reply: only voice notes / photos?
            var lastOut = history.LastOrDefault(m => m.Direction == (byte)MessageDirection.Outgoing);
            var fresh = history.Where(m => m.Direction == (byte)MessageDirection.Incoming
                                        && m.MessageType != (byte)WhatsAppMessageType.Reaction
                                        && (lastOut == null || m.WhatsAppTimestamp >= lastOut.WhatsAppTimestamp)).ToList();
            if (fresh.Count == 0) return;
            if (fresh.All(m => string.IsNullOrWhiteSpace(m.TextBody) || m.MessageType == (byte)WhatsAppMessageType.Unsupported))
            {
                conversation.IntakeNoTextCount++;
                if (conversation.IntakeNoTextCount >= 2)
                {
                    await HandOffAsync(conversation, "العميل بيبعت رسايل صوتية/صور ومش بيكتب", ct);
                    return;
                }
                await SendAsync(conversation, AiMessages.Get(settings, AiMessages.NoVoice), ct);
                return;
            }

            if (conversation.IntakeTurns >= MaxTurns)
            {
                await HandOffAsync(conversation, "المحادثة طولت من غير ما تكمل البيانات", ct);
                return;
            }

            // What the customer just wrote (all their messages since our last reply)
            var said = string.Join(" ", fresh.Where(m => !string.IsNullOrWhiteSpace(m.TextBody) && m.MessageType != (byte)WhatsAppMessageType.Unsupported).Select(m => m.TextBody!.Trim()));
            var norm = IntakeScript.Normalize(said);

            if (IntakeScript.WantsPerson(norm))
            {
                await HandOffAsync(conversation, "العميل طلب يكلم موظف", ct);
                return;
            }
            if (IntakeScript.NotInterested(norm))
            {
                await HandOffAsync(conversation, "العميل قال إنه مش مهتم", ct);
                return;
            }

            var greeting = string.IsNullOrWhiteSpace(settings.AiGreeting) ? LeadFormSetting.DefaultAiGreeting : settings.AiGreeting!;
            var isFirstReply = lastOut == null;
            var asked = IntakeScript.IsQuestion(norm);
            string reply;

            if (isFirstReply)
            {
                // Their first message is usually "السلام عليكم" or a question — greet, then ask the name
                reply = greeting + "\n\n" + AiMessages.Get(settings, AiMessages.AskName);
            }
            else
            {
                // Read the answer to whatever we asked last: name → age → job
                var ok = false;
                if (conversation.IntakeName == null)
                {
                    var name = asked ? null : IntakeScript.ParseName(said);
                    if (name != null) { conversation.IntakeName = Cut(name, 200); ok = true; }
                }
                else if (conversation.IntakeAge == null)
                {
                    var age = IntakeScript.ParseAge(said);
                    if (age != null) { conversation.IntakeAge = (byte)age.Value; conversation.IntakeAgeAt = DateTime.UtcNow; ok = true; }
                }
                else if (conversation.IntakeJob == null)
                {
                    var job = asked ? null : IntakeScript.ParseJob(said);
                    if (job != null) { conversation.IntakeJob = Cut(job, 200); ok = true; }
                }

                if (conversation.IntakeName != null && conversation.IntakeAge != null && conversation.IntakeJob != null)
                {
                    await CompleteAsync(conversation, settings, ct);
                    return;
                }

                var prefix = asked ? AiMessages.Get(settings, AiMessages.ConsultantWillExplain) + "\n\n" : "";
                var next = conversation.IntakeName == null ? (ok ? AiMessages.AskName : AiMessages.AskNameAgain)
                    : conversation.IntakeAge == null ? (ok ? AiMessages.AskAge : AiMessages.AskAgeAgain)
                    : (ok ? AiMessages.AskJob : AiMessages.AskJobAgain);
                reply = prefix + AiMessages.Get(settings, next, conversation.IntakeName);
            }

            conversation.IntakeTurns++;
            await SendAsync(conversation, reply, ct);
        }

        private static string? Cut(string? v, int max) => v == null ? null : (v.Trim().Length > max ? v.Trim()[..max] : v.Trim());

        // ── Outcomes ────────────────────────────────────────────────────────

        /// <summary>The team a WhatsApp number belongs to (its team leader), or null for the general number.</summary>
        private async Task<Guid?> TeamOfAccountAsync(WhatsAppAccount account, CancellationToken ct)
        {
            var number = Digits(account.DisplayPhoneNumber);
            var forms = await _context.TeamLeadForms.AsNoTracking()
                .Where(f => f.WhatsAppNumber != null && f.Manager.IsActive)
                .Select(f => new { f.ManagerId, f.WhatsAppNumber }).ToListAsync(ct);
            return forms.FirstOrDefault(f => Digits(f.WhatsAppNumber) == number)?.ManagerId;
        }

        /// <summary>
        /// Create the lead from what was collected and route it like the registration form.
        /// Also used when the customer stops answering after giving the age.
        /// </summary>
        public async Task CompleteAsync(WhatsAppConversation conversation, LeadFormSetting settings, CancellationToken ct)
        {
            var team = await TeamOfAccountAsync(conversation.WhatsAppAccount, ct);
            var phone = PhoneNumbers.Normalize(conversation.Contact.WhatsAppPhoneNumber);
            var now = DateTime.UtcNow;

            // Already a lead with this phone (e.g. registered on the site meanwhile): attach to it
            var variants = PhoneNumbers.StoredVariants(phone);
            var lead = await _context.Leads.FirstOrDefaultAsync(l => variants.Contains(l.Phone), ct);
            if (lead == null)
            {
                var branchId = await _context.Branches.Where(b => b.IsActive).OrderBy(b => b.Name).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct);
                var adminId = await _context.Users.Where(u => u.Role == 1 && u.IsActive).OrderBy(u => u.CreatedAt).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
                if (branchId == null || adminId == null)
                {
                    await HandOffAsync(conversation, "مش قادر أسجل العميل (مفيش فرع أو أدمن نشط)", ct);
                    return;
                }
                lead = new Lead
                {
                    Id = Guid.NewGuid(),
                    BranchId = branchId.Value,
                    RegisteredById = adminId.Value,
                    FullName = conversation.IntakeName ?? conversation.Contact.Name ?? phone,
                    Phone = phone,
                    LeadSource = LeadSourceWhatsApp,
                    Status = 1,
                    Age = conversation.IntakeAge,
                    InterestedJobTitle = conversation.IntakeJob,
                    Notes = "سجّل عن طريق المساعد الآلي على واتساب",
                    TeamManagerId = team,
                    CreatedAt = now
                };
                _context.Leads.Add(lead);
            }
            conversation.LeadId = lead.Id;

            var threshold = settings.SeniorAgeThreshold == 0 ? (byte)45 : settings.SeniorAgeThreshold;
            var toRotation = lead.AssignedSalesId == null
                && conversation.IntakeAge != null
                && (conversation.IntakeAge >= threshold || settings.AiUnderAgeToRotation);

            Guid? agentId = lead.AssignedSalesId;
            if (agentId == null && toRotation)
            {
                agentId = team != null
                    ? await LeadDistributor.NextTeleSalesAsync(_context, _context.Users.Where(u => u.ManagerId == team), LeadDistributor.TeamScope(team.Value), ct)
                    : await LeadDistributor.NextTeleSalesAsync(_context, _context.Users, LeadDistributor.AllScope, ct);
            }

            conversation.IntakeStatus = (byte)IntakeStatus.Completed;
            conversation.UpdatedAt = now;

            if (agentId != null)
            {
                lead.AssignedSalesId = agentId;
                conversation.AssignedSalesAgentId = agentId;
                conversation.PendingTeamManagerId = null;
                var agent = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == agentId, ct);
                _context.LeadActivities.Add(new LeadActivity
                {
                    Id = Guid.NewGuid(),
                    LeadId = lead.Id,
                    ActivityType = 8,
                    Description = $"سجّل عن طريق المساعد الآلي على واتساب واتعيّن لـ {agent.FullName} (توزيع بالدور)",
                    ActorType = 2,
                    CreatedAt = now
                });
                await _context.SaveChangesAsync(ct);

                await SendAsync(conversation, AiMessages.Get(settings, AiMessages.DoneAssigned, conversation.IntakeName, agent.DisplayNameAr), ct);

                await NotifyAsync(agent.Id, "عميل جديد من واتساب (المساعد الآلي)",
                    $"{lead.FullName} — السن {conversation.IntakeAge?.ToString() ?? "؟"} — {conversation.IntakeJob ?? ""}", $"/Inbox/Index?c={conversation.Id}");
                await _notifier.ConversationAssignedAsync(conversation.Id, conversation.WhatsAppAccountId, null, agentId);
            }
            else
            {
                // Under the age (or no salesperson available): waits with the team leader, like the form
                conversation.PendingTeamManagerId = team;
                await _context.SaveChangesAsync(ct);
                await SendAsync(conversation, AiMessages.Get(settings, AiMessages.DoneWaiting, conversation.IntakeName), ct);
                await NotifyTeamLeaderAsync(team, "عميل جديد من واتساب في انتظار التعيين",
                    $"{lead.FullName} — السن {conversation.IntakeAge?.ToString() ?? "؟"} — {conversation.IntakeJob ?? ""}", conversation.Id, ct);
                await _notifier.ConversationAssignedAsync(conversation.Id, conversation.WhatsAppAccountId, null, null);
            }
        }

        /// <summary>To the team leader right away (asked for a person, voice notes, error, too long, no age).</summary>
        public async Task HandOffAsync(WhatsAppConversation conversation, string reason, CancellationToken ct)
        {
            var team = await TeamOfAccountAsync(conversation.WhatsAppAccount, ct);
            conversation.IntakeStatus = (byte)IntakeStatus.HandedOff;
            conversation.IntakeHandoffReason = reason;
            conversation.PendingTeamManagerId = team;
            conversation.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            var settings = await _context.LeadFormSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            await SendAsync(conversation, AiMessages.Get(settings, AiMessages.HandOff), ct);
            var who = conversation.IntakeName ?? conversation.Contact.Name ?? conversation.Contact.WhatsAppPhoneNumber;
            await NotifyTeamLeaderAsync(team, "محادثة واتساب محتاجة تدخلك", $"{who} — {reason}", conversation.Id, ct);
            await _notifier.ConversationAssignedAsync(conversation.Id, conversation.WhatsAppAccountId, null, null);
        }

        private async Task NotifyTeamLeaderAsync(Guid? team, string title, string body, Guid conversationId, CancellationToken ct)
        {
            var link = $"/Inbox/Index?c={conversationId}";
            var ids = team == null
                ? await _context.Users.Where(u => u.IsActive && u.Role == 1).Select(u => u.Id).ToListAsync(ct)   // general number: admins
                : await _context.Users.Where(u => u.IsActive && (u.Id == team || (u.Role == 8 && u.ManagerId == team))).Select(u => u.Id).ToListAsync(ct);
            foreach (var id in ids) await NotifyAsync(id, title, body, link);
        }

        private async Task NotifyAsync(Guid userId, string title, string body, string link)
        {
            try { await _notifications.SendAsync(userId, title, body, link: link); }
            catch (Exception ex) { _logger.LogWarning(ex, "AI assistant notification failed"); }
        }

        /// <summary>Send a WhatsApp text from the assistant and show it in the inbox.</summary>
        private async Task SendAsync(WhatsAppConversation conversation, string text, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var message = new WhatsAppMessage
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                WhatsAppAccountId = conversation.WhatsAppAccountId,
                Direction = (byte)MessageDirection.Outgoing,
                MessageType = (byte)WhatsAppMessageType.Text,
                MessageSource = (byte)MessageSource.System,
                TextBody = text,
                Status = (byte)MessageStatus.Queued,
                WhatsAppTimestamp = now,
                CreatedAt = now
            };
            _context.WhatsAppMessages.Add(message);
            conversation.LastMessageAt = now;
            await _context.SaveChangesAsync(ct);

            var result = await _cloudApi.SendTextMessageAsync(conversation.WhatsAppAccount.PhoneNumberId, conversation.Contact.WhatsAppPhoneNumber, text, ct);
            message.Status = result.Success ? (byte)MessageStatus.Sent : (byte)MessageStatus.Failed;
            message.WhatsAppMessageId = result.WhatsAppMessageId;
            message.ErrorCode = result.ErrorCode;
            message.ErrorMessage = result.ErrorMessage;
            await _context.SaveChangesAsync(ct);
            await _notifier.NewMessageAsync(message, conversation.AssignedSalesAgentId, AssistantName);
        }

        // ── Timeouts ────────────────────────────────────────────────────────

        /// <summary>
        /// Customers who stopped answering: with the age → routed after AiWaitAfterAgeMinutes;
        /// without it → team leader after AiWaitNoAgeHours.
        /// </summary>
        public async Task SweepTimeoutsAsync(CancellationToken ct)
        {
            var settings = await _context.LeadFormSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            if (settings == null) return;
            var now = DateTime.UtcNow;
            var afterAge = TimeSpan.FromMinutes(settings.AiWaitAfterAgeMinutes > 0 ? settings.AiWaitAfterAgeMinutes : LeadFormSetting.DefaultAiWaitAfterAgeMinutes);
            var noAge = TimeSpan.FromHours(settings.AiWaitNoAgeHours > 0 ? settings.AiWaitNoAgeHours : LeadFormSetting.DefaultAiWaitNoAgeHours);

            var waiting = await _context.WhatsAppConversations
                .Include(c => c.Contact).Include(c => c.WhatsAppAccount)
                .Where(c => c.IntakeStatus == (byte)IntakeStatus.Collecting)
                .ToListAsync(ct);
            foreach (var c in waiting)
            {
                var quietSince = c.IntakeLastCustomerAt ?? c.IntakeStartedAt ?? now;
                try
                {
                    if (c.IntakeAge != null && now - quietSince >= afterAge)
                        await CompleteAsync(c, settings, ct);
                    else if (c.IntakeAge == null && now - quietSince >= noAge)
                        await HandOffAsync(c, "العميل ما ردش على السن", ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "AI assistant timeout handling failed for {ConversationId}", c.Id);
                }
            }
        }
    }

    /// <summary>
    /// Queue between the webhook and the assistant: waits a few seconds after a customer's last message
    /// (people send several in a row) and answers each chat one turn at a time. Also runs the timeouts.
    /// </summary>
    public class AiIntakeQueue
    {
        private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
        public readonly ConcurrentDictionary<Guid, DateTime> LastMessageAt = new();

        public void Enqueue(Guid conversationId)
        {
            LastMessageAt[conversationId] = DateTime.UtcNow;
            _channel.Writer.TryWrite(conversationId);
        }

        public ChannelReader<Guid> Reader => _channel.Reader;
    }

    public class AiIntakeWorker : BackgroundService
    {
        private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(4);
        private readonly AiIntakeQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AiIntakeWorker> _logger;
        private readonly ConcurrentDictionary<Guid, byte> _running = new();

        public AiIntakeWorker(AiIntakeQueue queue, IServiceScopeFactory scopeFactory, ILogger<AiIntakeWorker> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            Task.WhenAll(ConsumeAsync(stoppingToken), SweepLoopAsync(stoppingToken));

        private async Task ConsumeAsync(CancellationToken ct)
        {
            await foreach (var id in _queue.Reader.ReadAllAsync(ct))
            {
                if (!_running.TryAdd(id, 0)) continue;   // that chat is already being answered; its latest message is in the batch
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Wait until the customer has been quiet for a moment
                        while (_queue.LastMessageAt.TryGetValue(id, out var last) && DateTime.UtcNow - last < Debounce)
                            await Task.Delay(1000, ct);
                        using var scope = _scopeFactory.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<AiIntakeService>().ReplyAsync(id, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "AI assistant turn failed for {ConversationId}", id);
                    }
                    finally
                    {
                        _running.TryRemove(id, out _);
                        // A message that arrived while we were answering gets its own turn
                        if (_queue.LastMessageAt.TryGetValue(id, out var last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(30))
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var db = scope.ServiceProvider.GetRequiredService<RecruitmentCrmContext>();
                            var unanswered = await db.WhatsAppMessages.AsNoTracking()
                                .Where(m => m.ConversationId == id)
                                .OrderByDescending(m => m.WhatsAppTimestamp).ThenByDescending(m => m.CreatedAt)
                                .Select(m => m.Direction).FirstOrDefaultAsync();
                            if (unanswered == (byte)MessageDirection.Incoming) _queue.Enqueue(id);
                        }
                    }
                }, ct);
            }
        }

        private async Task SweepLoopAsync(CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<AiIntakeService>().SweepTimeoutsAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "AI assistant timeout sweep failed");
                }
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }
        }
    }
}

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// The rules for reading the customer's answers (the texts it sends are in AiMessages) (Egyptian Arabic, free text).
    /// No AI: digits / common number words for the age, simple keyword lists for "I want a person" etc.
    /// </summary>
    public static class IntakeScript
    {
        /// <summary>Same spelling for the same word: أ/إ/آ→ا، ة→ه، ى→ي، Arabic digits → 0-9, no tashkeel.</summary>
        public static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (var ch in text.Trim().ToLowerInvariant())
            {
                var c = ch switch
                {
                    'أ' or 'إ' or 'آ' => 'ا',
                    'ة' => 'ه',
                    'ى' => 'ي',
                    >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                    >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                    _ => ch
                };
                if (c is >= '\u064B' and <= '\u0652') continue;   // tashkeel
                if (c == 'ـ') continue;                           // tatweel
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static readonly string[] PersonWords =
        {
            "اكلم حد", "اكلم موظف", "اكلم انسان", "اكلم بني ادم", "اكلم مسئول", "اكلم مسؤول", "اكلم المسئول", "اكلم المسؤول",
            "عايز موظف", "عاوز موظف", "عايز حد", "عاوز حد", "حد يكلمني", "حد يرد", "موظف حقيقي", "مش روبوت", "مش عايز روبوت",
            "اتصلوا بي", "كلموني", "كلمني", "رقم تليفون", "عايز اتصل", "عاوز اتصل"
        };
        private static readonly string[] NotInterestedWords =
        {
            "مش مهتم", "مش عايز حاجه", "مش عاوز حاجه", "مش عايز اسافر", "مش عاوز اسافر", "لا شكرا", "شكرا مش",
            "الغي", "الغاء", "متبعتليش", "ما تبعتليش", "بطلوا", "بلوك"
        };

        public static bool WantsPerson(string norm) => PersonWords.Any(w => norm.Contains(Normalize(w)));
        public static bool NotInterested(string norm) => NotInterestedWords.Any(w => norm.Contains(Normalize(w)));

        private static readonly string[] QuestionWords =
            { "؟", "?", "كام", "بكام", "المرتب", "مرتب", "الراتب", "راتب", "فيزا", "تاشيره", "السفر امتي", "امتي", "فين", "ليه", "ازاي", "هل ", "مصاريف", "فلوس", "تكلفه" };
        public static bool IsQuestion(string norm) => QuestionWords.Any(w => norm.Contains(Normalize(w)));

        private static readonly string[] GreetingOnly =
            { "السلام عليكم ورحمه الله وبركاته", "السلام عليكم ورحمه الله", "السلام عليكم", "سلام عليكم", "السلام", "اهلا وسهلا", "اهلا",
              "مرحبا", "صباح الخير", "صباح النور", "صباح الفل", "مساء الخير", "مساء النور", "مساء الفل", "هاي", "hi", "hello",
              "ازيك", "ازيكم", "تمام", "ok", "اوك", "ايوه", "نعم", "حاضر", "شكرا", "يا جماعه", "يا فندم" };

        /// <summary>A name: letters only, 2–5 words, not just a greeting. "انا اسمي محمد احمد" → "محمد احمد".</summary>
        public static string? ParseName(string? said)
        {
            // Punctuation → spaces so "السلام عليكم، أنا…" matches "السلام عليكم"
            var text = System.Text.RegularExpressions.Regex.Replace(said ?? "", @"[,،.!؛;:\-]+", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            // "السلام عليكم انا محمد" → drop the greeting(s) first and keep what follows
            for (var stripped = true; stripped;)
            {
                stripped = false;
                text = text.Trim(' ', ',', '،', '.', '!', '-');
                var normText = Normalize(text);
                foreach (var g in GreetingOnly.OrderByDescending(x => x.Length))
                {
                    var ng = Normalize(g);
                    if (normText.StartsWith(ng + " ") || normText == ng)
                    {
                        text = text.Length > ng.Length ? text[ng.Length..] : "";
                        stripped = true;
                        break;
                    }
                }
            }
            text = text.Trim(' ', ',', '،', '.', '!', '-');
            foreach (var lead in new[] { "انا اسمي", "أنا اسمي", "اسمي", "انا", "أنا", "الاسم", "إسمي" })
                if (text.StartsWith(lead + " ")) { text = text[(lead.Length + 1)..].Trim(); break; }
            text = new string(text.Where(c => char.IsLetter(c) || c == ' ').ToArray()).Trim();
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            if (text.Count(char.IsLetter) < 2) return null;
            var norm = Normalize(text);
            if (GreetingOnly.Any(g => norm == Normalize(g) || norm.StartsWith(Normalize(g) + " "))) return null;   // "مساء الخير يا جماعة" isn't a name
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 6) return null;   // a sentence, not a name
            return text;
        }

        private static readonly (string Word, int Value)[] Units =
        {
            ("واحد", 1), ("اتنين", 2), ("اثنين", 2), ("تلاته", 3), ("ثلاثه", 3), ("تلات", 3), ("ثلاث", 3),
            ("اربعه", 4), ("اربع", 4), ("خمسه", 5), ("خمس", 5), ("سته", 6), ("ست", 6), ("سبعه", 7), ("سبع", 7),
            ("تمانيه", 8), ("ثمانيه", 8), ("تمن", 8), ("تمان", 8), ("تسعه", 9), ("تسع", 9)
        };
        private static readonly (string Word, int Value)[] Tens =
            { ("عشرين", 20), ("تلاتين", 30), ("ثلاثين", 30), ("اربعين", 40), ("خمسين", 50), ("ستين", 60) };
        private static readonly (string Word, int Value)[] Teens =
            { ("تمنتاشر", 18), ("ثمانتاشر", 18), ("ثمانيه عشر", 18), ("تسعتاشر", 19), ("تسعه عشر", 19) };

        /// <summary>An exact age 18–65 from "45", "٤٥", "عندي 47 سنه", "خمسه واربعين"… null when unclear.</summary>
        public static int? ParseAge(string? said)
        {
            var norm = Normalize(said);
            var numbers = System.Text.RegularExpressions.Regex.Matches(norm, @"\d{1,3}").Select(m => int.Parse(m.Value)).ToList();
            if (numbers.Count == 1) return numbers[0] is >= 18 and <= 65 ? numbers[0] : null;
            if (numbers.Count > 1) return null;   // "من 40 ل 50" — not an exact age

            foreach (var (w, v) in Teens) if (norm.Contains(w)) return v;
            var words = norm.Split(new[] { ' ', 'و', ',', '،', '.' }, StringSplitOptions.RemoveEmptyEntries);
            int? tens = null, units = null;
            foreach (var word in words)
            {
                var t = Tens.FirstOrDefault(x => word == x.Word || word == "و" + x.Word);
                if (t.Word != null) { tens = t.Value; continue; }
                var u = Units.FirstOrDefault(x => word == x.Word);
                if (u.Word != null && units == null) units = u.Value;
            }
            if (tens == null) return null;
            var age = tens.Value + (units ?? 0);
            return age is >= 18 and <= 65 ? age : null;
        }

        /// <summary>A job: some letters, not a greeting, not a long story.</summary>
        public static string? ParseJob(string? said)
        {
            var text = (said ?? "").Trim();
            foreach (var lead in new[] { "عايز اشتغل", "عاوز اشتغل", "عايز أشتغل", "عاوز أشتغل", "انا", "أنا", "شغال" })
                if (text.StartsWith(lead + " ")) { text = text[(lead.Length + 1)..].Trim(); break; }
            if (text.Count(char.IsLetter) < 2) return null;
            var norm = Normalize(text);
            if (GreetingOnly.Any(g => norm == Normalize(g))) return null;
            return text.Length > 200 ? text[..200] : text;
        }
    }
}
