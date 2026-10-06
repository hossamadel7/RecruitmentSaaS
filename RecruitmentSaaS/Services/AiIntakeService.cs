using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// AI WhatsApp assistant ("المساعد الآلي"). For a customer who writes to a team's number directly
    /// (no form, not already a lead) it asks name, age and job, then creates the lead and routes it the
    /// same way the registration form does: age at/above the threshold → the team's rotation, under it
    /// → waits with the team leader. Anything it can't handle goes to the team leader.
    /// </summary>
    public class AiIntakeService
    {
        public const int MaxTurns = 8;
        public const byte LeadSourceWhatsApp = 5;
        private const string AssistantName = "المساعد الآلي";

        private readonly RecruitmentCrmContext _context;
        private readonly IWhatsAppCloudApiService _cloudApi;
        private readonly IInboxRealtimeNotifier _notifier;
        private readonly INotificationService _notifications;
        private readonly IConfiguration _config;
        private readonly ILogger<AiIntakeService> _logger;

        public AiIntakeService(RecruitmentCrmContext context, IWhatsAppCloudApiService cloudApi, IInboxRealtimeNotifier notifier,
                               INotificationService notifications, IConfiguration config, ILogger<AiIntakeService> logger)
        {
            _context = context;
            _cloudApi = cloudApi;
            _notifier = notifier;
            _notifications = notifications;
            _config = config;
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
                                        && (lastOut == null || m.WhatsAppTimestamp >= lastOut.WhatsAppTimestamp)).ToList();
            if (fresh.Count == 0) return;
            if (fresh.All(m => string.IsNullOrWhiteSpace(m.TextBody)))
            {
                conversation.IntakeNoTextCount++;
                if (conversation.IntakeNoTextCount >= 2)
                {
                    await HandOffAsync(conversation, "العميل بيبعت رسايل صوتية/صور ومش بيكتب", ct);
                    return;
                }
                await SendAsync(conversation, "معلش يا فندم 🙏 مش بقدر أسمع الرسايل الصوتية أو أشوف الصور — ممكن تكتبلي الرد كتابة؟", ct);
                return;
            }

            if (conversation.IntakeTurns >= MaxTurns)
            {
                await HandOffAsync(conversation, "المحادثة طولت من غير ما تكمل البيانات", ct);
                return;
            }

            AssistantTurn? turn;
            try
            {
                turn = await AskClaudeAsync(conversation, settings, history, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "AI assistant failed for conversation {ConversationId}", conversationId);
                await HandOffAsync(conversation, "المساعد الآلي واجه مشكلة تقنية", ct);
                return;
            }
            if (turn == null)
            {
                await HandOffAsync(conversation, "المساعد الآلي ما قدرش يرد", ct);
                return;
            }

            // Someone took the chat over while the model was thinking — stay quiet
            var noTextCount = conversation.IntakeNoTextCount;
            await _context.Entry(conversation).ReloadAsync(ct);
            if (conversation.IntakeStatus != (byte)IntakeStatus.Collecting) return;
            conversation.IntakeNoTextCount = noTextCount;

            // Keep what we already know; take new answers (age must be a real 18–65)
            if (!string.IsNullOrWhiteSpace(turn.Name)) conversation.IntakeName = Cut(turn.Name, 200);
            if (turn.Age is >= 18 and <= 65 && conversation.IntakeAge == null)
            {
                conversation.IntakeAge = (byte)turn.Age.Value;
                conversation.IntakeAgeAt = DateTime.UtcNow;
            }
            if (!string.IsNullOrWhiteSpace(turn.Job)) conversation.IntakeJob = Cut(turn.Job, 200);

            if (turn.Handoff)
            {
                await HandOffAsync(conversation, string.IsNullOrWhiteSpace(turn.HandoffReason) ? "العميل طلب يكلم موظف" : Cut(turn.HandoffReason, 200)!, ct);
                return;
            }

            if (conversation.IntakeName != null && conversation.IntakeAge != null && conversation.IntakeJob != null)
            {
                await CompleteAsync(conversation, settings, ct);
                return;
            }

            conversation.IntakeTurns++;
            await SendAsync(conversation, string.IsNullOrWhiteSpace(turn.Reply) ? "ممكن توضحلي أكتر يا فندم؟" : turn.Reply.Trim(), ct);
        }

        private static string? Cut(string? v, int max) => v == null ? null : (v.Trim().Length > max ? v.Trim()[..max] : v.Trim());

        // ── Claude ──────────────────────────────────────────────────────────

        private sealed record AssistantTurn(string? Reply, string? Name, int? Age, string? Job, bool Handoff, string? HandoffReason);

        private async Task<AssistantTurn?> AskClaudeAsync(WhatsAppConversation conversation, LeadFormSetting settings,
                                                          List<WhatsAppMessage> history, CancellationToken ct)
        {
            var apiKey = _config["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Anthropic API key is not configured");
            var client = new AnthropicClient { ApiKey = apiKey, Timeout = TimeSpan.FromSeconds(60) };

            var greeting = string.IsNullOrWhiteSpace(settings.AiGreeting) ? LeadFormSetting.DefaultAiGreeting : settings.AiGreeting!;
            var known = $"Already collected — name: {conversation.IntakeName ?? "(not yet)"}, age: {(conversation.IntakeAge?.ToString() ?? "(not yet)")}, job: {conversation.IntakeJob ?? "(not yet)"}.";
            var system = $"""
You are the WhatsApp intake assistant of "الفهد العربي لإلحاق العمالة المصرية بالخارج", a licensed Egyptian overseas recruitment company (license 492). The first consultation is free.

Your only job: collect three things from the customer, then a human consultant takes over:
1. full name (الاسم), 2. age in years (السن) — an exact whole number, 3. the job they want (الوظيفة المطلوبة).

How to talk:
- Egyptian Arabic, short, warm and polite ("حضرتك", "يا فندم"). One question per message. No long paragraphs.
- If this is your first message in the chat, start it with exactly this greeting, then ask for the name: "{greeting}"
- Ask in this order: name → age → job. Skip what's already known. Accept free answers ("عندي ٤٧ سنة", "شغال سواق نقل تقيل").
- Age must be an exact number between 18 and 65. If the answer is vague ("فوق الأربعين", "كبير شوية") or out of range, ask again politely for the exact age. Never guess an age.
- You are an automated assistant; if asked, say so honestly.
- If they ask about salaries, countries, visas, fees or timelines: say briefly that the consultant will explain everything after these questions, then continue. Never promise anything.
- Set handoff=true (with a short Arabic reason) if they ask to talk to a person/employee, are angry or insulting, say they're not interested, or the conversation clearly can't continue.

Output: reply = your next WhatsApp message to the customer (Arabic). name/age/job = everything known so far (null if not given yet). When all three are known, reply with a short thank-you.

{known}
""";

            // WhatsApp history → alternating user/assistant turns (consecutive messages merged)
            var turns = new List<(Role Role, string Text)>();
            foreach (var m in history)
            {
                var role = m.Direction == (byte)MessageDirection.Incoming ? Role.User : Role.Assistant;
                var text = string.IsNullOrWhiteSpace(m.TextBody)
                    ? (role == Role.User ? "[رسالة صوتية أو صورة — غير مقروءة]" : "")
                    : m.TextBody!.Trim();
                if (text.Length == 0) continue;
                if (turns.Count > 0 && turns[^1].Role == role) turns[^1] = (role, turns[^1].Text + "\n" + text);
                else turns.Add((role, text));
            }
            while (turns.Count > 0 && turns[0].Role != Role.User) turns.RemoveAt(0);   // must start with the customer
            if (turns.Count == 0 || turns[^1].Role != Role.User) return null;          // nothing new to answer

            var schema = new Dictionary<string, JsonElement>
            {
                ["type"] = JsonSerializer.SerializeToElement("object"),
                ["properties"] = JsonSerializer.SerializeToElement(new Dictionary<string, object>
                {
                    ["reply"] = new { type = "string" },
                    ["name"] = new { type = new[] { "string", "null" } },
                    ["age"] = new { type = new[] { "integer", "null" } },
                    ["job"] = new { type = new[] { "string", "null" } },
                    ["handoff"] = new { type = "boolean" },
                    ["handoff_reason"] = new { type = new[] { "string", "null" } },
                }),
                ["required"] = JsonSerializer.SerializeToElement(new[] { "reply", "name", "age", "job", "handoff", "handoff_reason" }),
                ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
            };

            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = _config["Anthropic:Model"] ?? "claude-opus-5-5",
                MaxTokens = 2000,
                System = system,
                OutputConfig = new OutputConfig { Effort = Effort.Low, Format = new JsonOutputFormat { Schema = schema } },
                Messages = turns.Select(t => new MessageParam { Role = t.Role, Content = t.Text }).ToList(),
            }, ct);

            if (response.StopReason == "refusal")
            {
                _logger.LogWarning("AI assistant refusal for conversation {ConversationId}", conversation.Id);
                return null;
            }

            var json = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string? Str(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            int? Int(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
            bool Bool(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
            return new AssistantTurn(Str("reply"), Str("name"), Int("age"), Str("job"), Bool("handoff"), Str("handoff_reason"));
        }

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

                var welcome = await _context.LeadFormSettings.AsNoTracking().Select(s => new { s.WelcomeMessageEnabled, s.WelcomeMessage }).FirstOrDefaultAsync(ct);
                var customer = conversation.IntakeName ?? "";
                var text = welcome?.WelcomeMessageEnabled == true && !string.IsNullOrWhiteSpace(welcome.WelcomeMessage)
                    ? welcome.WelcomeMessage.Replace("{agent}", agent.DisplayNameAr).Replace("{name}", customer)
                    : $"شكراً يا أ/ {customer} 🙏 معاك أ/ {agent.DisplayNameAr} هتكمل مع حضرتك دلوقتي.";
                await SendAsync(conversation, text, ct);

                await NotifyAsync(agent.Id, "عميل جديد من واتساب (المساعد الآلي)",
                    $"{lead.FullName} — السن {conversation.IntakeAge?.ToString() ?? "؟"} — {conversation.IntakeJob ?? ""}", $"/Inbox/Index?c={conversation.Id}");
                await _notifier.ConversationAssignedAsync(conversation.Id, conversation.WhatsAppAccountId, null, agentId);
            }
            else
            {
                // Under the age (or no salesperson available): waits with the team leader, like the form
                conversation.PendingTeamManagerId = team;
                await _context.SaveChangesAsync(ct);
                await SendAsync(conversation, $"شكراً يا أ/ {conversation.IntakeName ?? "فندم"} 🙏 هيتواصل مع حضرتك مستشار من فريقنا قريب.", ct);
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

            await SendAsync(conversation, "تمام 🙏 هحول حضرتك لمسؤول الفريق وهيرد عليك في أقرب وقت.", ct);
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
