using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services;

public sealed record AiAutoReplyCheck(string Key, bool Ok, string Label, string? Fix = null);
public sealed record AiAutoReplyReport(bool WillReply, string Summary, AiAutoReplyCheck[] Checks);

/// <summary>
/// Explains, in plain Arabic, whether the AI will reply automatically to a conversation and, if not, which condition
/// blocks it. Read-only. Mirrors the gates in <see cref="AiAutomaticReplyService.RunConversationAsync"/> and
/// <see cref="AiSalesDraftService"/>; keep them in sync when those change. Never returns secrets, only yes/no states.
/// </summary>
public sealed class AiAutoReplyDiagnostics(RecruitmentCrmContext db, IOptions<AiSalesOptions> sales,
    IOptions<AiSalesEngineOptions> engine, IOptions<AiSalesDispatchOptions> dispatch)
{
    public async Task<AiAutoReplyReport> CheckAsync(Guid conversationId, CancellationToken ct)
    {
        var e = engine.Value; var d = dispatch.Value; var tenant = sales.Value.TenantId; var now = DateTime.UtcNow;
        var checks = new List<AiAutoReplyCheck>();
        void Add(string key, bool ok, string label, string fix) => checks.Add(new(key, ok, label, ok ? null : fix));

        var chat = await db.WhatsAppConversations.AsNoTracking().SingleAsync(c => c.Id == conversationId, ct);
        var agent = chat.AssignedAiSalesAgentId == null ? null
            : await db.AiSalesAgents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == chat.AssignedAiSalesAgentId && a.TenantId == tenant, ct);
        var control = await db.ConversationControls.AsNoTracking().SingleOrDefaultAsync(c => c.ConversationId == conversationId && c.TenantId == tenant, ct);
        var assignment = await db.AiLeadAssignments.AsNoTracking()
            .Where(a => a.TenantId == tenant && a.ConversationId == conversationId && a.EndedAt == null).OrderByDescending(a => a.StartedAt).FirstOrDefaultAsync(ct);
        var incoming = await db.WhatsAppMessages.AsNoTracking().Where(m => m.ConversationId == conversationId && m.Direction == 1)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).FirstOrDefaultAsync(ct);

        // Global switches.
        Add("enabled", sales.Value.Enabled, "مساعد المبيعات مفعّل في النظام", "فعّل AiSales:Enabled في إعدادات السيرفر.");
        Add("engine", e.AutoReplyEnabled && e.DraftsEnabled && e.ExternalProcessingApproved && !string.IsNullOrWhiteSpace(e.ApiKey) && !string.IsNullOrWhiteSpace(e.Model),
            "الرد التلقائي واقتراح الردود مفعّلين ومتوصلين بـ OpenAI",
            "فعّل AiSalesEngine:AutoReplyEnabled و DraftsEnabled و ExternalProcessingApproved وحدد ApiKey و Model.");
        Add("dispatch", d.Enabled && d.ReleaseApproved && d.AutomaticEnabled, "الإرسال التلقائي للعملاء مفعّل",
            "فعّل AiSalesDispatch:Enabled و ReleaseApproved و AutomaticEnabled.");
        Add("account", d.ApprovedAccountIds.Contains(chat.WhatsAppAccountId), "رقم الواتساب ده مسموح له بالإرسال التلقائي",
            "أضف رقم الواتساب ده إلى AiSalesDispatch:ApprovedAccountIds.");

        // This conversation and its agent.
        Add("assigned", agent != null, "المحادثة متعيّنة لمساعد AI", "اختار المساعد واضغط «تعيين للمساعد».");
        if (agent != null)
        {
            Add("agent-active", !agent.Paused && agent.Mode == AiSalesMode.Shadow, "المساعد شغال (مش متوقف)", "شغّل المساعد من إعدادات المساعدين.");
            Add("agent-allowed", e.AutoReplyAgentIds.Contains(agent.Id), "المساعد ده مسموح له بالرد التلقائي",
                "أضف رقم المساعد (" + agent.Id + ") إلى AiSalesEngine:AutoReplyAgentIds.");
            Add("controller", control?.Controller == ConversationControllerMode.Assisted, "المساعد هو اللي ماسك المحادثة دلوقتي",
                control?.Controller switch
                {
                    ConversationControllerMode.Human => "موظف استلم المحادثة. اضغط «استئناف المساعد» من إدارة المتابعة.",
                    ConversationControllerMode.Paused => "المتابعة متوقفة. اضغط «استئناف المساعد» من إدارة المتابعة.",
                    ConversationControllerMode.Intake => "لسه بيجمع بيانات العميل؛ استنى لما يخلص.",
                    _ => "عيّن المحادثة للمساعد."
                });
            var inScope = e.AutoReplyAnyAssignedConversation || e.ApprovedConversationIds.Contains(conversationId) && d.ApprovedConversationIds.Contains(conversationId);
            Add("scope", inScope, "المحادثة ضمن المحادثات المسموح فيها بالرد التلقائي",
                "فعّل AiSalesEngine:AutoReplyAnyAssignedConversation أو أضف المحادثة لقائمة المحادثات المسموحة.");
            if (e.AutoReplyAnyAssignedConversation)
                Add("assignment-date", assignment != null && e.AutoReplySinceUtc != null && assignment.StartedAt >= e.AutoReplySinceUtc,
                    "التعيين حصل بعد تفعيل الرد التلقائي",
                    "التعيين أقدم من تاريخ التفعيل (AutoReplySinceUtc). اضغط «إعادة للموظف» ثم عيّنها للمساعد من جديد.");
        }

        // The latest customer message.
        // Same idempotency key the automatic worker uses for this turn.
        var key = incoming == null || control == null ? null : $"auto:{conversationId}:{incoming.Id}:{control.OwnershipVersion}";
        var drafted = key == null ? null : await db.AiSalesDrafts.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.RequestKey == key).FirstOrDefaultAsync(ct);
        Add("incoming", incoming != null, "فيه رسالة من العميل", "مفيش رسالة من العميل لسه.");
        if (incoming != null)
        {
            Add("window", incoming.WhatsAppTimestamp >= now.AddHours(-24).AddMinutes(5), "آخر رسالة من العميل خلال الـ 24 ساعة",
                "عدّت 24 ساعة على آخر رسالة من العميل. واتساب مش بيسمح برد عادي؛ استخدم قالب معتمد من المحادثة.");
            var answered = await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == conversationId && m.Direction == 2 && m.CreatedAt >= incoming.CreatedAt, ct);
            Add("unanswered", !answered, "آخر رسالة من العميل لسه ماترديش عليها", "حد رد بالفعل بعد آخر رسالة من العميل؛ المساعد مستني رسالة جديدة.");
            var asked = await db.AiAdminClarifications.AnyAsync(r => r.TenantId == tenant && r.ConversationId == conversationId && r.CustomerMessageId == incoming.Id &&
                (r.State == AiAdminRequestState.Queued || r.State == AiAdminRequestState.AwaitingAdmin || r.State == AiAdminRequestState.AnswerReceived), ct);
            Add("ask-admin", !asked, "المساعد مش مستني رد من المدير", "المساعد سأل المدير عن معلومة ناقصة ومستني رده. جاوب من «تدريب مساعد المبيعات ← قناة المدير».");
            if (drafted != null) Add("draft", drafted.Status is AiDraftStatus.AutomaticallyApproved || drafted.Status == AiDraftStatus.Generating, "رد المساعد على آخر رسالة", DraftReason(drafted));
        }
        var today = await db.AiSalesDrafts.CountAsync(x => x.TenantId == tenant && x.CreatedAt >= now.Date, ct);
        Add("budget", today < e.MaxDraftsPerDay, $"الحد اليومي للردود ({today} من {e.MaxDraftsPerDay})",
            "اتعمل الحد اليومي من الردود. زوّد AiSalesEngine:MaxDraftsPerDay أو استنى لبكرة.");

        var blocking = checks.FirstOrDefault(c => !c.Ok);
        var summary = blocking == null
            ? drafted?.Status == AiDraftStatus.AutomaticallyApproved ? "المساعد رد تلقائياً على آخر رسالة." : "كل الشروط تمام. المساعد هيرد تلقائياً خلال ثواني."
            : "المساعد مش هيرد تلقائياً: " + blocking.Fix;
        return new(blocking == null, summary, checks.ToArray());
    }

    private static string DraftReason(AiSalesDraft d) => d.Status switch
    {
        _ when d.NeedsHuman => "المساعد شاف إن الرسالة دي محتاجة موظف" + (string.IsNullOrWhiteSpace(d.ReviewNote) ? "" : " (" + d.ReviewNote + ")") + ". رد على العميل بنفسك أو راجع الرد المقترح تحت.",
        AiDraftStatus.Failed => "تعذر تجهيز الرد (" + d.ErrorCode + "). " + d.ErrorCode switch
        {
            "ExternalProcessingDisabled" or "ProviderNotConfigured" => "اتأكد من إعدادات OpenAI.",
            "InterruptedGeneration" or "GenerationCancelled" => "الرد اتقطع؛ هيتجرب تاني مع رسالة جديدة من العميل.",
            _ => "رد على العميل بنفسك أو اطلب رد جديد."
        },
        AiDraftStatus.Stale => "المحادثة اتغيرت وقت تجهيز الرد فاتلغى. هيرد على الرسالة الجاية.",
        AiDraftStatus.ReviewRequired => "الرد جاهز بس محتاج مراجعة قبل الإرسال؛ راجعه تحت.",
        AiDraftStatus.Rejected => "الرد اترفض في المراجعة.",
        _ => "الرد اتعمل بس ماتبعتش."
    };
}
