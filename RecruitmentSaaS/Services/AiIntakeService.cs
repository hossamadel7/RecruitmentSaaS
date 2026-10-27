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
        private readonly AiSalesControlService _controls;
        private readonly IWhatsAppCloudApiService _cloudApi;
        private readonly IInboxRealtimeNotifier _notifier;
        private readonly INotificationService _notifications;
        private readonly ILogger<AiIntakeService> _logger;

        public AiIntakeService(RecruitmentCrmContext context, IWhatsAppCloudApiService cloudApi, IInboxRealtimeNotifier notifier,
                               INotificationService notifications, ILogger<AiIntakeService> logger, AiSalesControlService controls)
        {
            _context = context;
            _controls = controls;
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
            await using var gate = await ConversationLock.AcquireAsync(_context, "conversation:" + conversationId, ct);
            var conversation = await _context.WhatsAppConversations
                .Include(c => c.Contact).Include(c => c.WhatsAppAccount)
                .FirstOrDefaultAsync(c => c.Id == conversationId, ct);
            if (conversation == null || conversation.IntakeStatus != (byte)IntakeStatus.Collecting) return;

            if (conversation.AssignedAiSalesAgentId != null)
            {
                var permitted = _controls.Options.Enabled && await _context.AiSalesAgents.AnyAsync(a =>
                    a.Id == conversation.AssignedAiSalesAgentId && a.TenantId == _controls.Options.TenantId
                    && !a.Paused && a.Mode == AiSalesMode.Shadow, ct);
                if (!permitted) return;
                var control = await _context.ConversationControls.AsNoTracking().SingleOrDefaultAsync(c => c.ConversationId == conversationId, ct);
                if (control?.Controller != ConversationControllerMode.Intake) return;
            }

            var settings = await _context.LeadFormSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new LeadFormSetting();
            var history = await _context.WhatsAppMessages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.WhatsAppTimestamp).ThenBy(m => m.CreatedAt)
                .ToListAsync(ct);

            // Customer's new messages since our last reply: only voice notes / photos?
            var lastOut = history.LastOrDefault(m => m.Direction == (byte)MessageDirection.Outgoing);
            var fresh = history.Where(m => m.Direction == (byte)MessageDirection.Incoming
                                        && m.MessageType != (byte)WhatsAppMessageType.Reaction
                                        && m.MessageType != (byte)WhatsAppMessageType.Unsupported
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
                await SendAsync(conversation, AiMessages.Get(settings, AiMessages.NoVoice), ct);
                return;
            }

            if (conversation.IntakeTurns >= MaxTurns)
            {
                await HandOffAsync(conversation, "المحادثة طولت من غير ما تكمل البيانات", ct);
                return;
            }

            // What the customer just wrote (all their messages since our last reply)
            var said = string.Join(" ", fresh.Where(m => !string.IsNullOrWhiteSpace(m.TextBody)).Select(m => m.TextBody!.Trim()));
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

            if (conversation.IntakeName != null && conversation.IntakeAge != null && conversation.IntakeJob != null)
            {
                await CompleteAsync(conversation, settings, ct);
                return;
            }

            // What we asked last (nothing yet on the first message)
            var slot = isFirstReply ? IntakeSlot.None
                : conversation.IntakeName == null ? IntakeSlot.Name
                : conversation.IntakeAge == null ? IntakeSlot.Age
                : IntakeSlot.Job;

            // People often answer something else, or everything at once ("محمد علي 47 سنه نجار") — in their
            // first message too. Keep whatever is in there, then only ask for what is still missing.
            var facts = IntakeScript.Extract(said, slot, asked);
            if (facts.Age != null && conversation.IntakeAge == null)
            {
                conversation.IntakeAge = (byte)facts.Age.Value;
                conversation.IntakeAgeAt = DateTime.UtcNow;
            }
            if (facts.Job != null && conversation.IntakeJob == null) conversation.IntakeJob = Cut(facts.Job, 200);
            // "اسمي طارق" also corrects a name we got wrong earlier
            if (facts.Name != null && (conversation.IntakeName == null || facts.NameIsLabelled)) conversation.IntakeName = Cut(facts.Name, 200);

            // Asked for the name twice and still no name: carry on with the WhatsApp profile name, don't keep asking
            if (slot == IntakeSlot.Name && conversation.IntakeName == null && lastOut?.TextBody != null
                && lastOut.TextBody.TrimEnd().EndsWith(AiMessages.Get(settings, AiMessages.AskNameAgain).TrimEnd(), StringComparison.Ordinal))
            {
                var profile = (conversation.Contact.Name ?? "").Trim();
                conversation.IntakeName = Cut(profile.Any(char.IsLetter) ? profile : conversation.Contact.WhatsAppPhoneNumber, 200);
            }

            var complete = conversation.IntakeName != null && conversation.IntakeAge != null && conversation.IntakeJob != null;
            if (complete && !isFirstReply)
            {
                await CompleteAsync(conversation, settings, ct);
                return;
            }

            // Ask again (differently) only when they didn't answer the thing we had just asked
            var missing = conversation.IntakeName == null ? IntakeSlot.Name
                : conversation.IntakeAge == null ? IntakeSlot.Age
                : IntakeSlot.Job;
            var again = missing == slot;
            var next = missing == IntakeSlot.Name ? (again ? AiMessages.AskNameAgain : AiMessages.AskName)
                : missing == IntakeSlot.Age ? (again ? AiMessages.AskAgeAgain : AiMessages.AskAge)
                : (again ? AiMessages.AskJobAgain : AiMessages.AskJob);

            if (isFirstReply)
            {
                if (complete)
                {
                    // Everything was in their first message: greet, then hand over
                    conversation.IntakeTurns++;
                    await SendAsync(conversation, greeting, ct);
                    await CompleteAsync(conversation, settings, ct);
                    return;
                }
                reply = greeting + "\n\n" + AiMessages.Get(settings, next);
            }
            else
            {
                var prefix = asked ? AiMessages.Get(settings, AiMessages.ConsultantWillExplain) + "\n\n" : "";
                reply = prefix + AiMessages.Get(settings, next);
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

            // Click-to-WhatsApp attribution belongs to the inbound message. Carry the latest
            // referral into a lead created later by intake without overwriting prior attribution.
            var referral = await _context.WhatsAppMessages.AsNoTracking()
                .Where(m => m.ConversationId == conversation.Id && m.ReferralSourceId != null)
                .OrderByDescending(m => m.WhatsAppTimestamp)
                .Select(m => new
                {
                    m.ReferralCampaignId,
                    m.ReferralSourceId,
                    m.ReferralHeadline,
                    CampaignName = m.ReferralCampaign != null ? m.ReferralCampaign.Name : null,
                    CampaignUtm = m.ReferralCampaign != null ? m.ReferralCampaign.UtmCampaign : null
                })
                .FirstOrDefaultAsync(ct);

            // Already a lead with this phone (e.g. registered on the site meanwhile): attach to it
            var variants = PhoneNumbers.StoredVariants(phone);
            var lead = conversation.LeadId != null
                ? await _context.Leads.SingleOrDefaultAsync(l => l.Id == conversation.LeadId, ct)
                : await _context.Leads.FirstOrDefaultAsync(l => variants.Contains(l.Phone), ct);
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

            if (referral != null)
            {
                lead.CampaignId ??= referral.ReferralCampaignId;
                lead.UtmSource ??= "meta";
                lead.UtmMedium ??= "click-to-whatsapp";
                lead.UtmCampaign ??= Cut(referral.CampaignUtm ?? referral.CampaignName ?? referral.ReferralHeadline, 200);
                lead.UtmContent ??= Cut(referral.ReferralSourceId, 200);
            }
            conversation.LeadId = lead.Id;

            if (lead.AssignedAiSalesAgentId != null)
            {
                conversation.IntakeStatus = (byte)IntakeStatus.Completed;
                lead.Age ??= conversation.IntakeAge;
                lead.InterestedJobTitle ??= conversation.IntakeJob;
                await _controls.CompleteIntakeLockedAsync(conversation, ct);
                await _context.SaveChangesAsync(ct);
                return; // Phase 1 records the handoff; it never sends an AI sales reply.
            }
            await using var assignmentTransaction = await _context.Database.BeginTransactionAsync(ct);
            // Intake can finish after staff transferred the chat or disabled its former owner.
            var currentAgent = await ChatAssignmentPolicy.ResolveAsync(_context,
                conversation.AssignedSalesAgentId, lead.AssignedSalesId, ct);
            var threshold = settings.SeniorAgeThreshold == 0 ? (byte)45 : settings.SeniorAgeThreshold;
            var atOrAboveAge = conversation.IntakeAge != null && conversation.IntakeAge >= threshold;
            var toRotation = currentAgent == null
                && conversation.IntakeAge != null
                && (atOrAboveAge || settings.AiUnderAgeToRotation);

            Guid? agentId = currentAgent;
            // Under the age on a team's number: that team's "أقل من السن" rotation, when the team set one
            if (agentId == null && conversation.IntakeAge != null && !atOrAboveAge && team != null)
                agentId = await LeadDistributor.NextInTeamAsync(_context, team.Value, atOrAboveAge: false, ct);
            if (agentId == null && toRotation)
            {
                agentId = team != null
                    ? await LeadDistributor.NextTeleSalesAsync(_context, _context.Users.Where(u => (u.ManagerId == team || u.TeamMemberships.Any(m => m.ManagerId == team))), LeadDistributor.TeamScope(team.Value), ct)
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

                await assignmentTransaction.CommitAsync(ct);
                await SendAsync(conversation, AiMessages.Get(settings, AiMessages.DoneAssigned, agent.DisplayNameAr), ct);

                await NotifyAsync(agent.Id, "عميل جديد من واتساب (المساعد الآلي)",
                    $"{lead.FullName} — السن {conversation.IntakeAge?.ToString() ?? "؟"} — {conversation.IntakeJob ?? ""}", $"/Inbox/Index?c={conversation.Id}");
                await _notifier.ConversationAssignedAsync(conversation.Id, conversation.WhatsAppAccountId, null, agentId);
            }
            else
            {
                // Under the age (or no salesperson available): waits with the team leader, like the form
                conversation.PendingTeamManagerId = team;
                await _context.SaveChangesAsync(ct);
                await assignmentTransaction.CommitAsync(ct);
                await SendAsync(conversation, AiMessages.Get(settings, AiMessages.DoneWaiting), ct);
                await NotifyTeamLeaderAsync(team, "عميل جديد من واتساب في انتظار التعيين",
                    $"{lead.FullName} — السن {conversation.IntakeAge?.ToString() ?? "؟"} — {conversation.IntakeJob ?? ""}", conversation.Id, ct);
                await _notifier.ConversationAssignedAsync(conversation.Id, conversation.WhatsAppAccountId, null, null);
            }
        }

        /// <summary>To the team leader right away (asked for a person, voice notes, error, too long, no age).</summary>
        public async Task HandOffAsync(WhatsAppConversation conversation, string reason, CancellationToken ct)
        {
            var team = await TeamOfAccountAsync(conversation.WhatsAppAccount, ct);
            await _controls.TakeOverLockedAsync(conversation, null, reason, ct);
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
                : await _context.Users.Where(u => u.IsActive && (u.Id == team || (u.Role == 8 && (u.ManagerId == team || u.TeamMemberships.Any(m => m.ManagerId == team))))).Select(u => u.Id).ToListAsync(ct);
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
            // WhatsApp only allows free text within 24 hours of the customer's last message (even for ad chats);
            // after that (e.g. the "no age after 48 hours" hand-off) Meta rejects it, so stay silent and just hand over
            var lastCustomer = await _context.WhatsAppMessages.AsNoTracking()
                .Where(m => m.ConversationId == conversation.Id && m.Direction == (byte)MessageDirection.Incoming)
                .MaxAsync(m => (DateTime?)m.WhatsAppTimestamp, ct);
            if (lastCustomer == null || lastCustomer < now.AddHours(-24).AddMinutes(5))
            {
                _logger.LogInformation("Assistant message not sent to {ConversationId}: the 24-hour window is closed", conversation.Id);
                return;
            }
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
                    await using var gate = await ConversationLock.AcquireAsync(_context, "conversation:" + c.Id, ct);
                    await _context.Entry(c).ReloadAsync(ct);
                    if (c.IntakeStatus != (byte)IntakeStatus.Collecting) continue;
                    quietSince = c.IntakeLastCustomerAt ?? c.IntakeStartedAt ?? now;
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

}

namespace RecruitmentSaaS.Services
{
    /// <summary>What the assistant asked last.</summary>
    public enum IntakeSlot { None, Name, Age, Job }

    /// <summary>What one customer message told us. NameIsLabelled: they wrote "اسمي …" / "الاسم …".</summary>
    public sealed record IntakeFacts(string? Name, int? Age, string? Job, bool NameIsLabelled);

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
                if (c is >= 'ً' and <= 'ْ') continue;   // tashkeel
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
            { "السلام عليكم ورحمه الله وبركاته", "السلام عليكم ورحمه الله", "السلام عليكم", "سلام عليكم", "السلام", "اهلا وسهلا بكم", "اهلا وسهلا", "اهلا",
              "مرحبا", "صباح الخير", "صباح النور", "صباح الفل", "مساء الخير", "مساء النور", "مساء الفل", "هاي", "hi", "hello",
              "ازيك", "ازيكم", "تمام", "ok", "اوك", "ايوه", "نعم", "حاضر", "شكرا", "يا جماعه", "يا فندم" };

        // ── Words ───────────────────────────────────────────────────────────

        private static readonly System.Text.RegularExpressions.Regex Separators = new(@"[,،.!؛;:\-_/\\|()\[\]«»""'…~*؟?=+]+");
        private static readonly System.Text.RegularExpressions.Regex Spaces = new(@"\s+");

        /// <summary>The message as words, punctuation dropped ("على.صلاح.انا.حداد" → 4 words).</summary>
        private static string[] Words(string? text) =>
            Spaces.Replace(Separators.Replace(text ?? "", " "), " ").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        private static bool In(HashSet<string> set, string normWord) =>
            set.Contains(normWord) || (normWord.Length > 2 && normWord[0] == 'و' && set.Contains(normWord[1..]));

        private static HashSet<string> Set(params string[] words) => new(words.Select(Normalize));

        // Jobs people write. Prefix = any word starting with it ("كهرب" → كهربائي، كهربا); Exact = that word only
        // ("مساعد" but not "مساعده", "مدرس" but not "مدرسه").
        private static readonly string[] JobPrefixes =
        {
            "نجار", "نقاش", "حداد", "سباك", "كهرب", "لحام", "سواق", "سائق", "مبلط", "بلاط", "مبيض", "دهان", "مهندس", "طباخ",
            "محاسب", "ميكانيك", "سيراميك", "سراميك", "مندوب", "خياط", "ترزي", "جزار", "حلاق", "صنايعي", "استورجي", "محاره",
            "تبريد", "تكييف", "تكيف", "الوميتال", "جبس", "رخام", "مشغل", "ونش", "لودر", "حفار", "كاشير", "ممرض", "صيدل",
            "مزارع", "خراط", "سمكري", "نحات", "كوافير", "مبرمج", "خباز", "حلواني", "ويتر", "باريستا", "نظافه", "تمريض", "دليفري"
        };
        private static readonly HashSet<string> JobExact = Set("فني", "شيف", "عامل", "عمال", "مساعد", "مدرس", "حارس", "بائع", "مشرف", "مدير", "موظف", "دكتور", "طبيب", "محامي", "طيار", "امن", "ضابط");

        private static string JobCore(string normWord)
        {
            var w = normWord.Length > 3 && normWord[0] == 'و' ? normWord[1..] : normWord;
            return w.Length > 4 && w.StartsWith("ال") ? w[2..] : w;
        }
        private static bool HasArticle(string normWord) => (normWord.Length > 3 && normWord[0] == 'و' ? normWord[1..] : normWord).StartsWith("ال");
        private static bool IsJobWord(string normWord)
        {
            var w = JobCore(normWord);
            return JobExact.Contains(w) || JobPrefixes.Any(p => w.StartsWith(p));
        }

        // A name stops at any of these ("محمد طه خبره في…" → "محمد طه"); a "name" that starts with one isn't a name
        private static readonly HashSet<string> NotNameWords = Set(
            "ممكن", "تفاصيل", "التفاصيل", "تفصيل", "السن", "سن", "سني", "سنه", "عمري", "العمر", "ايه", "اي", "ايش", "اسم", "اسمي", "الاسم",
            "في", "عاوز", "عايز", "عايزه", "عاوزه", "عندي", "شغال", "شغلتي", "مهنتي", "المهنه", "مهنه", "الوظيفه", "وظيفه", "وظيفتي",
            "مطلوب", "المطلوب", "متاح", "السفر", "سفر", "اسافر", "للسفر", "تركيا", "شغل", "الشغل", "مع", "مفيش", "مش", "حضرتك", "حضرتكم",
            "هل", "كام", "بكام", "الراتب", "المرتب", "راتب", "مرتب", "انا", "من", "خبره", "دبلوم", "بكالوريوس", "ليسانس", "مؤهل", "مخلص", "جيش",
            "محافظه", "رقم", "تليفون", "موبايل", "لو", "بس", "يا", "فندم", "والله", "طيب", "تمام", "اوك", "نعم", "ايوه", "لا", "مهتم",
            "اريد", "ابغي", "محتاج", "فرصه", "عمل", "العمل", "للعمل", "اعمل", "بشتغل", "اشتغل", "التكلفه", "تكلفه", "السعر", "سعر",
            "الشروط", "شروط", "الاوراق", "اوراق", "اجراءات", "الاجراءات", "معايا", "معي", "معاك", "معك", "فيه", "هو", "هي", "ده", "دي", "كنت",
            "ازاي", "امتي", "فين", "ليه", "جاهز", "الكلام", "كلام", "علي", "عن", "ما", "هذا", "حول", "مرحبا", "السلام", "سلام", "عليكم",
            "شكرا", "حاضر", "ماله", "مين", "محدش", "حد", "بيرد", "اقل", "اكبر", "اكتر", "اكثر", "فوق", "تحت", "ابني", "ابن", "بنتي", "زوجي", "جوزي",
            "عنده", "نفسي", "ينفع", "باقي", "كمان", "لازم", "لزم", "بخصوص", "بكلم", "بسال", "ارجو", "حابب", "عاوزين", "عايزين", "احنا", "هو انا", "مواليد", "موليد", "متزوج", "اعزب", "عام", "عاما", "سنين", "مصر", "القاهره", "الجيزه", "اسكندريه", "الاسكندريه");

        // "علي" is a stop word above only as "على" (on); as a first name it comes through the exception below
        private static readonly HashSet<string> NameLabels = Set("الاسم", "اسمي", "اسم", "معك", "معاك");
        private static readonly HashSet<string> JobLabels = Set("المهنه", "مهنتي", "مهنه", "الوظيفه", "وظيفتي", "شغلتي", "صنعتي", "شغال", "بشتغل");
        private static readonly HashSet<string> WeakJobLabels = Set("انا", "شغل", "اشتغل", "اعمل", "عملي", "الشغل");
        private static readonly HashSet<string> JobStops = Set(
            "السن", "سني", "سن", "عمري", "العمر", "الاسم", "اسمي", "اسم", "عندي", "ايه", "المطلوب", "مطلوب", "كام", "بكام", "هل", "ممكن",
            "عايز", "عاوز", "تفاصيل", "التفاصيل", "جاهز", "معايا", "معي", "تليفون", "رقم", "محافظه", "خبره", "من", "لو", "هو", "ابحث",
            "محتاج", "اريد", "مواليد", "موليد", "متزوج", "اعزب", "انا", "تم", "دبلوم", "جواز");
        // "دبلوم صنايع", "معهد فني": schooling, not a job
        private static readonly HashSet<string> Schooling = Set("دبلوم", "معهد", "بكالوريوس", "ليسانس", "مؤهل", "ثانوي", "ثانويه", "مدرسه", "كليه");

        private static bool HasDigit(string w) => w.Any(char.IsDigit);
        private static bool AllLetters(string w) => w.Length > 0 && w.All(char.IsLetter);

        /// <summary>Name words from position <paramref name="start"/>: letters only, until a digit, a job or a word that can't be in a name.</summary>
        private static string? NameFrom(string[] words, int start)
        {
            var taken = new List<string>();
            for (var i = start; i < words.Length && taken.Count < 5; i++)
            {
                var w = words[i];
                var n = Normalize(w);
                if (!AllLetters(w)) break;
                // "علي" is a name when it starts or follows a name ("محمد علي حسن")
                if (n != "علي" && In(NotNameWords, n)) break;
                // "محمد النجار": a family name. "محمد نجار" / first word "نجار": a job.
                if (IsJobWord(n) && !(HasArticle(n) && taken.Count > 0)) break;
                taken.Add(w);
            }
            if (taken.Count == 0 || taken.Sum(t => t.Length) < 2) return null;
            if (taken.Count == 1 && Normalize(taken[0]) == "علي" && start == 0 && words.Length > 2) return null;   // "على كام…"
            return string.Join(" ", taken);
        }

        private static string[] WithoutGreeting(string? said)
        {
            var text = Spaces.Replace(Separators.Replace(said ?? "", " "), " ").Trim();
            for (var stripped = true; stripped;)
            {
                stripped = false;
                var normText = Normalize(text);
                foreach (var g in GreetingOnly.OrderByDescending(x => x.Length))
                {
                    var ng = Normalize(g);
                    if (normText.StartsWith(ng + " ") || normText == ng)
                    {
                        text = text.Length > ng.Length ? text[ng.Length..].Trim() : "";
                        stripped = true;
                        break;
                    }
                }
            }
            return text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>"اسمي محمد ثروت …", "الاسم: احمد السيد علي السن 23" → the name after the label.</summary>
        public static string? LabelledName(string? said)
        {
            var words = Words(said);
            for (var i = 0; i < words.Length - 1; i++)
            {
                var n = Normalize(words[i]);
                if (!In(NameLabels, n)) continue;
                var j = i + 1;
                if (Normalize(words[j]) == "حضرتك") return null;   // quoting our own question
                return NameFrom(words, j);
            }
            return null;
        }

        /// <summary>The answer to "اسم حضرتك إيه؟": the leading name words, or null when it isn't a name (a job, a question, a greeting).</summary>
        public static string? ParseName(string? said)
        {
            var labelled = LabelledName(said);
            if (labelled != null) return labelled;
            var words = WithoutGreeting(said);
            var start = words.Length > 1 && Normalize(words[0]) == "انا" ? 1 : 0;
            return NameFrom(words, start);
        }

        // ── Age ─────────────────────────────────────────────────────────────

        private static readonly (string Word, int Value)[] Units =
        {
            ("واحد", 1), ("اتنين", 2), ("اثنين", 2), ("تلاته", 3), ("ثلاثه", 3), ("تلات", 3), ("ثلاث", 3),
            ("اربعه", 4), ("اربع", 4), ("خمسه", 5), ("خمس", 5), ("سته", 6), ("ست", 6), ("سبعه", 7), ("سبع", 7),
            ("تمانيه", 8), ("ثمانيه", 8), ("تمن", 8), ("تمان", 8), ("تسعه", 9), ("تسع", 9)
        };
        private static readonly (string Word, int Value)[] Tens =
            { ("عشرين", 20), ("تلاتين", 30), ("ثلاثين", 30), ("اربعين", 40), ("خمسين", 50), ("ستين", 60), ("سبعين", 70) };
        private static readonly (string Word, int Value)[] Teens =
            { ("تمنتاشر", 18), ("ثمانتاشر", 18), ("ثمانيه عشر", 18), ("تسعتاشر", 19), ("تسعه عشر", 19) };

        private const int MinAge = 16, MaxAge = 80;
        private static int? InRange(int age, int min = MinAge, int max = MaxAge) => age >= min && age <= max ? age : null;

        private static System.Text.RegularExpressions.Regex Rx(string pattern) => new(pattern, System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex Phone = Rx(@"\d{5,}");
        private static readonly System.Text.RegularExpressions.Regex Birth = Rx(@"(?:مواليد|موليد|مواليدي|اتولدت|تاريخ الميلاد)\D{0,8}((?:\d{1,4}[\s./\\\-]*){1,3})");
        private static readonly System.Text.RegularExpressions.Regex Months = Rx(@"و?\s*\d{1,2}\s*(?:شهور|شهر|اشهر)");
        private static readonly System.Text.RegularExpressions.Regex Experience = Rx(@"(?:(?:خبره|خبرتي)\s*(?:(?!و?(?:ال)?(?:سن|عمر))\p{L}+\s+){0,2}|(?:بقالي|منذ|لمده|عملت|اشتغلت|شغال من)\s*)\d{1,2}\s*(?:سنه|سنين|سنوات|عاما|عام)?");
        private static readonly System.Text.RegularExpressions.Regex Counted = Rx(@"\d+\s*(?:الف|الاف|جنيه|دولار|ليره|يورو|ريال|عمال|عامل|افراد|فرد|انفار|نفر|ساعه|ساعات|يوم|ايام|اولاد|عيال|اطفال)");
        private static readonly System.Text.RegularExpressions.Regex Decimal = Rx(@"(\d{2})[.,]\d(?!\d)");
        private static readonly System.Text.RegularExpressions.Regex AgeAfterLabel = Rx(@"(?<!\p{L})و?(?:ال)?(?:سن|عمر)ي?(?!\p{L})\s*(?:انا\s*)?[^\d\p{L}]{0,4}\s*(\d{1,2})(?!\d)");
        private static readonly System.Text.RegularExpressions.Regex AgeBeforeUnit = Rx(@"(?<!\d)(\d{1,2})\s*(?:سنه|سنين|سنوات|عاما|عام|سن)(?!\p{L})");
        private static readonly System.Text.RegularExpressions.Regex AgeAfterHave = Rx(@"عندي\s*(\d{1,2})(?!\d)");
        private static readonly System.Text.RegularExpressions.Regex Number = Rx(@"\d+");
        // "لازم 45؟", "اقل من 45 ينفع": the age we ask for, not theirs
        private static readonly System.Text.RegularExpressions.Regex Requirement = Rx(@"(?:لازم|لزم|شرط|اقل من|اكبر من|اصغر من|اكتر من|اكثر من|فوق|تحت|اقصي|حد)\s*(?:ال)?\s*(?:سن\s*)?(?:ال\s*)?\d{1,2}");
        // "ابني عنده 19 سنه": someone else's age — only believed as the answer to our age question
        private static readonly System.Text.RegularExpressions.Regex SomeoneElse = Rx(@"(?<!\p{L})(?:ابني|ابن|بنتي|زوجي|جوزي|اخويا|اخي|اخو|والدي|ابويا)(?!\p{L})");

        private static int? FromBirthYear(int year)
        {
            if (year < 100) year += year <= 15 ? 2000 : 1900;   // "مواليد 81" → 1981
            return year is >= 1940 and <= 2012 ? InRange(DateTime.UtcNow.Year - year) : null;
        }

        /// <summary>
        /// The age in a message: "45", "٤٥", "السن 50", "عندي 47 سنه", "44 و9 شهور", "مواليد 1981", "خمسه واربعين"…
        /// <paramref name="askedAge"/>: we had just asked for it, so a bare number is the age. null when unclear.
        /// </summary>
        public static int? ParseAge(string? said, bool askedAge = true, bool bareNumberOk = true)
        {
            var norm = Phone.Replace(Normalize(said), " ");
            if (!askedAge && SomeoneElse.IsMatch(norm)) return null;

            // "فوق 45" answers our question; unasked it is usually about the age we require
            var cleaned = Experience.Replace(Months.Replace(norm, " "), " ");
            if (!askedAge) cleaned = Requirement.Replace(cleaned, " ");

            // A stated age wins over a birth date in the same message ("السن 47 مواليد 79/10/2")
            foreach (var rx in new[] { AgeAfterLabel, AgeBeforeUnit, AgeAfterHave })
            {
                var m = rx.Match(cleaned);
                if (m.Success && InRange(int.Parse(m.Groups[1].Value)) is int labelled) return labelled;
            }

            var birth = Birth.Match(norm);
            if (birth.Success)
            {
                // "مواليد 1980", "10/12/81", "79/10/2", "81" — a full year wins, then the part that can only be a year
                var parts = Number.Matches(birth.Groups[1].Value).Select(m => int.Parse(m.Value)).ToList();
                var year = parts.Where(y => y is >= 1940 and <= 2012).Select(y => (int?)y).FirstOrDefault()
                           ?? (parts.Count == 3 ? parts.Where(y => y is >= 40 and <= 99).Select(y => (int?)y).LastOrDefault()
                               : parts.Count == 1 || (parts.Count == 2 && parts[0] >= 100) ? parts[^1] : null);
                if (year is int y4 && FromBirthYear(y4) is int fromBirth) return fromBirth;
            }
            norm = cleaned;
            if (!askedAge && !bareNumberOk) return null;

            norm = Decimal.Replace(Counted.Replace(norm, " "), "$1");
            var numbers = Number.Matches(norm).Select(m => m.Value).Where(v => v.Length <= 4).Select(int.Parse).Distinct().ToList();
            if (numbers.Count == 1)
            {
                var n = numbers[0];
                if (n >= 1940) return askedAge ? FromBirthYear(n) : null;   // "1982"
                // Unasked, only a clear working age counts ("نقاش 38"), never "لحام سي 2"
                return askedAge ? InRange(n) : InRange(n, 18, 65);
            }
            if (numbers.Count > 1) return null;   // "من 40 ل 50" — not an exact age

            foreach (var (w, v) in Teens) if (norm.Contains(w)) return v;
            var words = norm.Split(new[] { ' ', ',', '،', '.' }, StringSplitOptions.RemoveEmptyEntries);
            int? tens = null, units = null;
            foreach (var word in words)
            {
                var t = Tens.FirstOrDefault(x => word == x.Word || word == "و" + x.Word);
                if (t.Word != null) { tens = t.Value; continue; }
                var u = Units.FirstOrDefault(x => word == x.Word);
                if (u.Word != null && units == null) units = u.Value;
            }
            if (tens == null) return null;
            return InRange(tens.Value + (units ?? 0));
        }

        // ── Job ─────────────────────────────────────────────────────────────

        /// <summary>A job written anywhere in the message: after "المهنه / شغال / انا …" or a known job word ("نجار موبيليا").</summary>
        public static string? FindJob(string? said)
        {
            var words = Words(said);
            var norms = words.Select(Normalize).ToArray();
            var start = -1;
            for (var i = 0; i < words.Length && start < 0; i++)
            {
                var n = norms[i];
                var next = i + 1 < words.Length ? norms[i + 1] : null;
                if (In(JobLabels, n))
                {
                    if (next != null && !HasDigit(next) && !In(JobStops, next) && next != "ايه" && next != "حضرتك") start = i + 1;
                }
                else if (In(WeakJobLabels, n))
                {
                    if (next != null && IsJobWord(next)) start = i + 1;
                }
                // "محمد النجار" is a family name; "النجار موبيليا" at the start is a job
                else if (IsJobWord(n) && !(HasArticle(n) && i > 0) && !(i > 0 && In(Schooling, norms[i - 1]))) start = i;
            }
            if (start < 0) return null;

            var taken = new List<string>();
            for (var i = start; i < words.Length && taken.Count < 6; i++)
            {
                if (HasDigit(words[i]) || (i > start && In(JobStops, norms[i]))) break;
                taken.Add(words[i]);
            }
            while (taken.Count > 0 && Normalize(taken[^1]) is "و" or "في" or "مع") taken.RemoveAt(taken.Count - 1);
            var job = string.Join(" ", taken);
            return job.Count(char.IsLetter) < 2 ? null : job;
        }

        /// <summary>The answer to "مهنة حضرتك إيه؟": some letters, not a greeting, not a long story.</summary>
        public static string? ParseJob(string? said)
        {
            var found = FindJob(said);
            if (found != null) return found;
            var text = (said ?? "").Trim();
            foreach (var lead in new[] { "عايز اشتغل", "عاوز اشتغل", "عايز أشتغل", "عاوز أشتغل", "انا", "أنا", "شغال" })
                if (text.StartsWith(lead + " ")) { text = text[(lead.Length + 1)..].Trim(); break; }
            text = string.Join(" ", WithoutGreeting(text));   // "تمام يا فندم" isn't a job
            if (text.Count(char.IsLetter) < 2) return null;
            // A long reply: keep its start, up to the first number
            var head = Words(text).TakeWhile(w => !HasDigit(w)).Take(8).ToArray();
            if (head.Length > 0 && Words(text).Length > head.Length) text = string.Join(" ", head);
            return text.Length > 200 ? text[..200] : text;
        }

        // ── Everything a message says ───────────────────────────────────────

        /// <summary>
        /// Name, age and job found in one message, whatever was asked. What was asked decides how freely the
        /// rest is read: a bare number is an age only after the age question, bare words are a name only
        /// after the name question (and never when the message is a question).
        /// </summary>
        public static IntakeFacts Extract(string? said, IntakeSlot asked, bool isQuestion)
        {
            var age = ParseAge(said, askedAge: asked == IntakeSlot.Age, bareNumberOk: !isQuestion);
            var name = LabelledName(said);
            var labelled = name != null;
            if (name == null && asked == IntakeSlot.Name && !isQuestion) name = ParseName(said);
            var job = FindJob(said);
            if (job == null && asked == IntakeSlot.Job && !isQuestion) job = ParseJob(said);
            // "احمد محمد فريد السن 24 المهنه نقاش" without being asked: a clear introduction, so the
            // leading words are the name — only with two words or more, a single word is too risky
            if (name == null && asked != IntakeSlot.Name && (age != null || job != null)
                && ParseName(said) is string intro && intro.Contains(' ')) name = intro;
            return new IntakeFacts(name, age, job, labelled);
        }
    }
}
