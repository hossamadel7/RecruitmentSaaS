using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RecruitmentSaaS.Services;

/// <summary>Private, durable coaching. Answers require explicit review; this service never sends to a customer.</summary>
public sealed class AiAdminLearningService(RecruitmentCrmContext db, AiSalesControlService controls,
    IOptions<AiAdminLearningOptions> options, IWhatsAppCloudApiService transport)
{
    public AiAdminLearningOptions Options => options.Value;
    public Guid Tenant => controls.Options.TenantId;
    public static string Phone(string value)
    {
        var phone = value.Trim().TrimStart('+');
        if (!Regex.IsMatch(phone, "^[1-9][0-9]{7,14}$")) throw new AiSalesValidation("اكتب الرقم الدولي بدون مسافات، مثل +201012345678.");
        return phone;
    }
    public async Task RequireAdminAsync(Guid actor, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == actor && u.Role == 1 && u.IsActive, ct))
            throw new AiSalesValidation("يلزم حساب مدير نشط.");
    }
    public void Require(Guid account)
    {
        controls.RequireEnabled();
        if (!Options.Enabled || !Options.PrivateRoutingEnabled || !Options.ApprovedAccountIds.Contains(account))
            throw new AiSalesValidation("تعلم المدير غير مفعل لهذا الحساب.");
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public async Task<(AiAdminChannel Channel, string Code)> BindAsync(Guid account, Guid actor, string phone, CancellationToken ct)
    {
        Require(account); await RequireAdminAsync(actor, ct); phone = Phone(phone);
        await using var gate = await ConversationLock.AcquireAsync(db, "admin-channel:" + Tenant + ":" + account, ct);
        if (!await db.WhatsAppAccounts.AnyAsync(a => a.Id == account && a.IsActive, ct)) throw new AiSalesValidation("الحساب غير متاح.");
        // Refuse to silently hide/reclassify an existing customer thread.
        if (await db.WhatsAppConversations.AnyAsync(c => c.WhatsAppAccountId == account &&
            (c.Contact.WhatsAppPhoneNumber == phone || c.Contact.WhatsAppPhoneNumber == "+" + phone || c.Contact.WhatsAppUserId == phone), ct))
            throw new AiSalesValidation("لهذا الرقم محادثة عميل موجودة؛ يلزم مراجعة فصلها قبل ربط قناة المدير.");
        var channel = await db.AiAdminChannels.SingleOrDefaultAsync(c => c.TenantId == Tenant && c.WhatsAppAccountId == account, ct);
        if (channel != null) throw new AiSalesConflict("القناة موجودة بالفعل. لا يمكن استبدال رقمها وإعادة توجيه رسائلها بالخطأ.");
        var code = "LINK-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        channel = new() { TenantId = Tenant, WhatsAppAccountId = account, AdminUserId = actor, Phone = phone,
            VerificationHash = Hash(code), VerificationExpiresAt = DateTime.UtcNow.AddMinutes(15) };
        db.AiAdminChannels.Add(channel); Audit("AdminChannelBound", channel.Id, actor);
        await db.SaveChangesAsync(ct); return (channel, code);
    }
    public async Task<string> RenewVerificationAsync(Guid account, Guid actor, CancellationToken ct)
    {
        Require(account); await RequireAdminAsync(actor, ct);
        await using var gate = await ConversationLock.AcquireAsync(db, "admin-channel:" + Tenant + ":" + account, ct);
        var c = await db.AiAdminChannels.SingleAsync(c => c.TenantId == Tenant && c.WhatsAppAccountId == account && c.AdminUserId == actor, ct);
        if (c.VerifiedAt != null) throw new AiSalesConflict("الرقم موثق بالفعل.");
        var code = "LINK-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        c.VerificationHash = Hash(code); c.VerificationExpiresAt = DateTime.UtcNow.AddMinutes(15); c.VerificationAttempts = 0; c.Version++;
        Audit("AdminChannelChallengeRenewed", c.Id, actor); await db.SaveChangesAsync(ct); return code;
    }
    public async Task<bool> IsPrivateAsync(Guid account, string rawPhone, CancellationToken ct)
    {
        if (!Options.PrivateRoutingEnabled) return false;
        string phone; try { phone = Phone(rawPhone); } catch (AiSalesValidation) { return false; }
        // Reserve even paused/unverified/disabled-user channels; never fall through into sales intake.
        return await db.AiAdminChannels.AnyAsync(c => c.TenantId == Tenant && c.WhatsAppAccountId == account && c.Phone == phone, ct);
    }
    public async Task<bool> ReceiveAsync(Guid account, JObject payload, CancellationToken ct)
    {
        var from = payload["from"]?.ToString() ?? "";
        if (!await IsPrivateAsync(account, from, ct)) return false;
        await using var gate = await ConversationLock.AcquireAsync(db, "admin-channel:" + Tenant + ":" + account, ct);
        var c = await db.AiAdminChannels.SingleAsync(c => c.TenantId == Tenant && c.WhatsAppAccountId == account && c.Phone == Phone(from), ct);
        var wamid = payload["id"]?.ToString(); if (string.IsNullOrWhiteSpace(wamid) || wamid.Length > 100) return true;
        if (await db.AiAdminMessages.AnyAsync(m => m.WhatsAppMessageId == wamid, ct)) return true;
        var text = (payload["text"] as JObject)?["body"]?.ToString() ?? "[رسالة غير نصية؛ أرسل الإجابة كتابة]";
        if (text.Length > 3000) text = "[الإجابة أطول من الحد؛ أرسلها من لوحة المراجعة]";
        var now = DateTime.UtcNow;
        var at = long.TryParse(payload["timestamp"]?.ToString(), out var timestamp) && timestamp > 0 && timestamp <= new DateTimeOffset(now.AddSeconds(5)).ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime : (DateTime?)null;
        // Store redacted private text, not a reusable instruction or customer message.
        var message = new AiAdminMessage { ChannelId = c.Id, WhatsAppMessageId = wamid, OperationKey = "in:" + wamid,
            Incoming = true, Text = Regex.IsMatch(text, "LINK-", RegexOptions.IgnoreCase) ? "[رمز توثيق]" : AiLearningSourceService.Redact(text, []), State = "Received" };
        db.AiAdminMessages.Add(message);
        var active = await db.Users.AnyAsync(u => u.Id == c.AdminUserId && u.IsActive && u.Role == 1, ct);
        if (!active) { await db.SaveChangesAsync(ct); return true; }
        if (at != null && at > c.LastInboundAt.GetValueOrDefault()) c.LastInboundAt = at;
        if (c.VerifiedAt == null)
        {
            // Pick the code out of the message: phones add invisible direction marks or surrounding text when it is copied
            // from the Arabic page. Only messages that contain a code use up one of the five attempts.
            var candidate = Regex.Match(text, @"LINK-[A-F0-9]{24}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).Value.ToUpperInvariant();
            if (candidate.Length == 0) { c.Version++; await db.SaveChangesAsync(ct); return true; }
            c.VerificationAttempts++;
            if (at != null && c.VerificationAttempts <= 5 && now < c.VerificationExpiresAt &&
                CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(candidate)), Encoding.ASCII.GetBytes(c.VerificationHash)))
            {
                c.VerifiedAt = now; c.VerificationHash = ""; c.Paused = false;
                Queue(c, "تم توثيق قناة المدير. الأسئلة هنا خاصة، ولا تُرسل الإجابات للعملاء قبل المراجعة. للإيقاف اكتب: إيقاف التدريب", "verified:" + c.Id);
                Audit("AdminChannelVerified", c.Id, c.AdminUserId);
            }
            c.Version++; await db.SaveChangesAsync(ct); return true;
        }
        if (!Options.Enabled || !Options.ApprovedAccountIds.Contains(account)) { await db.SaveChangesAsync(ct); return true; }
        if (text.Trim() is "إيقاف التدريب" or "ايقاف التدريب" or "pause") { c.Paused = true; c.Version++; Audit("AdminCoachingPaused", c.Id, c.AdminUserId); }
        else if (text.Trim() is "استئناف التدريب" or "resume") { c.Paused = false; c.Version++; Audit("AdminCoachingResumed", c.Id, c.AdminUserId); }
        else if (!c.Paused)
        {
            AiAdminClarification? request = null;
            var replyId = (payload["context"] as JObject)?["id"]?.ToString();
            if (replyId != null)
            {
                var referenced = await db.AiAdminMessages.AsNoTracking().SingleOrDefaultAsync(m => m.ChannelId == c.Id && !m.Incoming && m.WhatsAppMessageId == replyId && !m.IsTemplate, ct);
                if (referenced?.RequestId != null) request = await db.AiAdminClarifications.SingleOrDefaultAsync(r => r.Id == referenced.RequestId && r.ChannelId == c.Id, ct);
            }
            var code = Regex.Match(text, @"\bAI-[A-Fa-f0-9]{12}\b").Value.ToUpperInvariant();
            if (code.Length > 0)
            {
                var byCode = await db.AiAdminClarifications.SingleOrDefaultAsync(r => r.ChannelId == c.Id && r.Code == code, ct);
                if (request != null && request.Id != byCode?.Id) request = null;
                else request = byCode;
            }
            else if (replyId == null)
            {
                var open = await db.AiAdminClarifications.Where(r => r.ChannelId == c.Id && (r.State == AiAdminRequestState.Queued || r.State == AiAdminRequestState.AwaitingAdmin)).Take(2).ToArrayAsync(ct);
                // A natural reply can only answer a question the Admin was actually sent; never an unseen queued one.
                if (open.Length == 1 && open[0].State == AiAdminRequestState.AwaitingAdmin && await db.AiAdminMessages.AnyAsync(m =>
                        m.ChannelId == c.Id && m.RequestId == open[0].Id && m.OperationKey == "question:" + open[0].Id &&
                        (m.State == "Sent" || m.State == "Delivered" || m.State == "Read"), ct))
                    request = open[0];
            }
            if (request != null && at != null && at >= request.CreatedAt.AddSeconds(-1) && request.Deadline > now &&
                request.State is AiAdminRequestState.Queued or AiAdminRequestState.AwaitingAdmin or AiAdminRequestState.AnswerReceived)
            {
                request.Answer = AiLearningSourceService.Redact(Regex.Replace(text, @"\bAI-[A-Fa-f0-9]{12}\b", "").Trim(), []);
                request.AnswerMessageId = message.Id; message.RequestId = request.Id;
                request.AnswerScope = AiAdminAnswerScope.Incomplete;
                Change(request, AiAdminRequestState.AnswerReceived, c.AdminUserId);
                Queue(c, $"[{request.Code}] استلمت الإجابة. راجع معناها وحدد هل تخص العميل فقط أم قاعدة عامة من لوحة تدريب المساعد. لم تُنشر ولم تُرسل للعميل.", "answer:" + message.Id, request.Id);
            }
            else Queue(c, "للرد على سؤال: استخدم الرد على رسالته أو اكتب رقم AI- الخاص به. الرسائل العامة محفوظة هنا للمراجعة ولا تغير سياسات الشركة تلقائيًا.", "unmatched:" + message.Id);
        }
        await db.SaveChangesAsync(ct); return true;
    }
    private AiAdminMessage Queue(AiAdminChannel c, string text, string key, Guid? request = null)
    {
        var message = new AiAdminMessage { ChannelId = c.Id, RequestId = request, Text = text, OperationKey = key };
        db.AiAdminMessages.Add(message); return message;
    }

    public async Task<AiAdminClarification?> AskAsync(Guid conversation, string? question, Guid? actor, CancellationToken ct)
    {
        if (!Options.Enabled || !Options.PrivateRoutingEnabled) return null;
        var chat = await db.WhatsAppConversations.AsNoTracking().Include(c => c.Contact).SingleAsync(c => c.Id == conversation, ct);
        if (!Options.ApprovedAccountIds.Contains(chat.WhatsAppAccountId)) return null;
        Require(chat.WhatsAppAccountId); if (actor != null) await RequireAdminAsync(actor.Value, ct);
        // One Admin number serves every company number: use this number's own channel if it has one, otherwise the company's linked channel.
        var channel = await db.AiAdminChannels.Where(c => c.TenantId == Tenant && c.VerifiedAt != null && !c.Paused && Options.ApprovedAccountIds.Contains(c.WhatsAppAccountId))
            .OrderByDescending(c => c.WhatsAppAccountId == chat.WhatsAppAccountId).ThenBy(c => c.VerifiedAt).FirstOrDefaultAsync(ct);
        if (channel == null || !await db.Users.AnyAsync(u => u.Id == channel.AdminUserId && u.Role == 1 && u.IsActive, ct)) return null;
        await using var gate = await ConversationLock.AcquireAsync(db, "admin-channel:" + Tenant + ":" + chat.WhatsAppAccountId, ct);
        var control = await db.ConversationControls.AsNoTracking().SingleOrDefaultAsync(c => c.TenantId == Tenant && c.ConversationId == conversation, ct);
        if (chat.AssignedAiSalesAgentId == null || control?.Controller != ConversationControllerMode.Assisted) return null;
        var message = await db.WhatsAppMessages.AsNoTracking().Where(m => m.ConversationId == conversation && m.Direction == 1).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).FirstOrDefaultAsync(ct);
        if (message == null || message.MessageType != (byte)WhatsAppMessageType.Text) return null;
        var normalized = IntakeScript.Normalize(message.TextBody ?? "");
        if (IntakeScript.NotInterested(normalized) || IntakeScript.WantsPerson(normalized)) return null;
        var previous = await db.AiAdminClarifications.SingleOrDefaultAsync(r => r.TenantId == Tenant && r.ConversationId == conversation && r.CustomerMessageId == message.Id, ct);
        if (previous != null) return previous;
        if (await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == conversation && m.Direction == 2 && m.CreatedAt >= message.CreatedAt, ct)) return null;
        if (await db.AiAdminClarifications.CountAsync(r => r.TenantId == Tenant && r.CreatedAt >= DateTime.UtcNow.Date, ct) >= Options.MaxRequestsPerDay) return null;
        var leadName = chat.LeadId == null ? null : await db.Leads.Where(l => l.Id == chat.LeadId).Select(l => l.FullName).SingleOrDefaultAsync(ct);
        var source = question ?? "محتاج تأكيد المعلومة عشان أرد على سؤال العميل: " + message.TextBody;
        source = AiLearningSourceService.Redact(source, [leadName, chat.Contact.Name, chat.IntakeName]);
        if (string.IsNullOrWhiteSpace(source) || source.Length > 1800) throw new AiSalesValidation("السؤال مطلوب وبحد أقصى 1800 حرف بعد إخفاء البيانات.");
        var r = new AiAdminClarification { TenantId = Tenant, ChannelId = channel.Id, AgentId = chat.AssignedAiSalesAgentId.Value,
            ConversationId = conversation, CustomerMessageId = message.Id, OwnershipVersion = control.OwnershipVersion,
            Question = source, Deadline = DateTime.UtcNow.AddHours(Options.DeadlineHours) };
        db.AiAdminClarifications.Add(r); Change(r, AiAdminRequestState.Queued, actor);
        await db.SaveChangesAsync(ct); return r;
    }
    public async Task ValidateAnswerAsync(Guid id, Guid actor, long version, string answer, AiAdminAnswerScope scope,
        DateTime until, bool publish, string title, Guid? replaces, CancellationToken ct)
    {
        await RequireAdminAsync(actor, ct);
        var r = await db.AiAdminClarifications.SingleOrDefaultAsync(r => r.Id == id && r.TenantId == Tenant, ct) ?? throw new AiSalesValidation("السؤال غير متاح.");
        var channel = await db.AiAdminChannels.SingleAsync(c => c.Id == r.ChannelId, ct); Require(channel.WhatsAppAccountId);
        await using var gate = await ConversationLock.AcquireAsync(db, "admin-channel:" + Tenant + ":" + channel.WhatsAppAccountId, ct);
        await db.Entry(r).ReloadAsync(ct);
        // A request escalated only because the Admin deadline passed can still be validated explicitly; the resumer re-checks customer freshness.
        var lateButOpen = r.State == AiAdminRequestState.Escalated && r.ErrorCode == "AdminDeadlineExceeded";
        if (r.Version != version || !lateButOpen && r.State is not (AiAdminRequestState.AnswerReceived or AiAdminRequestState.AwaitingAdmin or AiAdminRequestState.Queued)) throw new AiSalesConflict("السؤال تغير؛ حدّث الصفحة.");
        if (channel.VerifiedAt == null || channel.AdminUserId != actor) throw new AiSalesValidation("يلزم المدير الموثق لهذه القناة.");
        if (string.IsNullOrWhiteSpace(answer) || answer.Length > 3000 || scope == AiAdminAnswerScope.Incomplete || !Enum.IsDefined(scope) ||
            until <= DateTime.UtcNow || until > DateTime.UtcNow.AddDays(90)) throw new AiSalesValidation("حدد إجابة واضحة ونطاقها وتاريخ انتهاء خلال 90 يومًا.");
        if (publish && (scope == AiAdminAnswerScope.CustomerOnly || string.IsNullOrWhiteSpace(title) || title.Length > 150))
            throw new AiSalesValidation("لا يجوز نشر استثناء عميل كسياسة عامة. عنوان المعلومة مطلوب للنشر.");
        if (replaces != null && (!publish || scope != AiAdminAnswerScope.Correction)) throw new AiSalesValidation("الاستبدال يحتاج تصحيحًا منشورًا.");
        await using var knowledgeGate = await ConversationLock.AcquireAsync(db, "knowledge-account:" + Tenant + ":" + channel.WhatsAppAccountId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (replaces != null)
        {
            // Knowledge is shared by all company numbers, so a correction may replace a fact stored on any of them.
            var old = await db.AiSalesKnowledge.SingleOrDefaultAsync(k => k.Id == replaces && k.TenantId == Tenant && k.Status == AiKnowledgeStatus.Approved, ct)
                ?? throw new AiSalesValidation("المعلومة المراد تصحيحها غير متاحة.");
            old.Status = AiKnowledgeStatus.Retired; old.Version++;
        }
        r.Answer = AiLearningSourceService.Redact(answer.Trim(), []); r.AnswerScope = scope; r.AnswerValidUntil = until; r.ValidatedById = actor;
        if (publish)
        {
            var k = new AiSalesKnowledge { TenantId = Tenant, WhatsAppAccountId = channel.WhatsAppAccountId, Kind = AiKnowledgeKind.Fact,
                Title = title.Trim(), Content = r.Answer, Status = AiKnowledgeStatus.Approved, ValidFrom = DateTime.UtcNow, ValidUntil = until,
                CreatedById = actor, ApprovedById = actor, ApprovedAt = DateTime.UtcNow, SourceReference = "ADMIN " + channel.Id + " " + r.Code + " message " + r.AnswerMessageId };
            db.AiSalesKnowledge.Add(k); r.KnowledgeId = k.Id; Audit("AdminKnowledgePublished", k.Id, actor);
        }
        Change(r, AiAdminRequestState.Validated, actor); r.ErrorCode = null; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public void Change(AiAdminClarification r, AiAdminRequestState state, Guid? actor)
    {
        r.State = state; r.Version++; Audit("AdminClarification" + state, r.Id, actor);
    }
    public void Audit(string action, Guid id, Guid? actor) => db.AuditLogs.Add(new() { Id = Guid.NewGuid(), ActorId = actor,
        ActorType = actor == null ? (byte)2 : (byte)1, EntityType = "AiAdminLearning", EntityId = id, EventType = action, CreatedAt = DateTime.UtcNow });

    public async Task DeliverAsync(Guid id, CancellationToken ct)
    {
        if (!Options.Enabled || !Options.PrivateRoutingEnabled || !Options.OutboundApproved) return;
        var m = await db.AiAdminMessages.SingleAsync(m => m.Id == id, ct);
        var c = await db.AiAdminChannels.SingleAsync(c => c.Id == m.ChannelId && c.TenantId == Tenant, ct);
        Require(c.WhatsAppAccountId);
        await using var gate = await ConversationLock.AcquireAsync(db, "admin-channel:" + Tenant + ":" + c.WhatsAppAccountId, ct);
        await db.Entry(m).ReloadAsync(ct); await db.Entry(c).ReloadAsync(ct);
        if (m.State == "Sending") { m.State = "Unknown"; m.ErrorCode = "InterruptedTransport"; await db.SaveChangesAsync(ct); return; }
        if (m.State != "Queued" || m.Incoming || c.Paused || c.VerifiedAt == null ||
            !await db.Users.AnyAsync(u => u.Id == c.AdminUserId && u.IsActive && u.Role == 1, ct)) return;
        if (m.OperationKey.StartsWith("question:") && !await db.AiAdminClarifications.AnyAsync(r => r.Id == m.RequestId &&
            r.State == AiAdminRequestState.AwaitingAdmin && r.Deadline > DateTime.UtcNow, ct))
        { m.State = "Cancelled"; await db.SaveChangesAsync(ct); return; }
        var now = DateTime.UtcNow;
        if (!m.IsTemplate && (c.LastInboundAt == null || c.LastInboundAt < now.AddHours(-24).AddMinutes(5) || c.LastInboundAt > now.AddSeconds(5))) return;
        if (m.IsTemplate && string.IsNullOrWhiteSpace(Options.NotificationTemplate)) return;
        var account = await db.WhatsAppAccounts.SingleAsync(a => a.Id == c.WhatsAppAccountId && a.IsActive, ct);
        m.State = "Sending"; await db.SaveChangesAsync(ct);
        WhatsAppSendResult? result = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { result = m.IsTemplate ? await transport.SendTemplateMessageAsync(account.PhoneNumberId, c.Phone, Options.NotificationTemplate, Options.TemplateLanguage, [], timeout.Token)
            : await transport.SendTextMessageAsync(account.PhoneNumberId, c.Phone, m.Text, timeout.Token); } catch (Exception) { }
        var accepted = result?.Success == true && !string.IsNullOrWhiteSpace(result.WhatsAppMessageId) && result.WhatsAppMessageId.Length <= 100;
        m.State = accepted ? "Sent" : "Unknown"; m.WhatsAppMessageId = accepted ? result!.WhatsAppMessageId : null;
        m.ErrorCode = accepted ? null : "TransportOutcomeUnknown";
        await db.SaveChangesAsync(CancellationToken.None);
    }
    public async Task QueueQuestionsAsync(Guid channelId, CancellationToken ct)
    {
        var c = await db.AiAdminChannels.SingleAsync(c => c.Id == channelId && c.TenantId == Tenant, ct);
        await using var gate = await ConversationLock.AcquireAsync(db, "admin-channel:" + Tenant + ":" + c.WhatsAppAccountId, ct);
        await db.Entry(c).ReloadAsync(ct);
        if (c.Paused || c.VerifiedAt == null || !Options.ApprovedAccountIds.Contains(c.WhatsAppAccountId)) return;
        var openWindow = c.LastInboundAt >= DateTime.UtcNow.AddHours(-24).AddMinutes(5);
        if (!openWindow)
        {
            if (string.IsNullOrWhiteSpace(Options.NotificationTemplate) || c.LastNotificationAt > DateTime.UtcNow.AddHours(-24) ||
                !await db.AiAdminClarifications.AnyAsync(r => r.ChannelId == c.Id && r.State == AiAdminRequestState.Queued, ct)) return;
            c.LastNotificationAt = DateTime.UtcNow;
            Queue(c, "إشعار أسئلة معلقة؛ أجب لفتح نافذة المحادثة", "notify:" + Guid.NewGuid()).IsTemplate = true;
        }
        else
        {
            var pending = await db.AiAdminClarifications.Where(r => r.ChannelId == c.Id && r.State == AiAdminRequestState.Queued && r.Deadline > DateTime.UtcNow).OrderBy(r => r.CreatedAt).Take(1).ToArrayAsync(ct);
            foreach (var r in pending)
            {
                Queue(c, $"[{r.Code}] {r.Question}\nرد على الرسالة أو اكتب رقم السؤال مع الإجابة. الإجابة ستحتاج مراجعة قبل استخدامها.", "question:" + r.Id, r.Id);
                Change(r, AiAdminRequestState.AwaitingAdmin, null);
            }
        }
        await db.SaveChangesAsync(ct);
    }
    public async Task<bool> StatusAsync(Guid account, JToken status, CancellationToken ct)
    {
        if (!Options.PrivateRoutingEnabled) return false;
        var id = status["id"]?.ToString(); if (id == null) return false;
        var m = await db.AiAdminMessages.SingleOrDefaultAsync(m => m.WhatsAppMessageId == id &&
            db.AiAdminChannels.Any(c => c.Id == m.ChannelId && c.TenantId == Tenant && c.WhatsAppAccountId == account), ct); if (m == null) return false;
        var state = status["status"]?.ToString();
        if (state == "read" || state == "delivered" && m.State != "Read") { m.State = state == "read" ? "Read" : "Delivered"; m.DeliveredAt = DateTime.UtcNow; }
        else if (state == "failed" && m.State is not ("Read" or "Delivered")) m.State = "Failed";
        await db.SaveChangesAsync(ct); return true;
    }
}
