using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;

namespace RecruitmentSaaS.Controllers.Api;
[ApiController, Route("api/ai-sales-training"), Authorize(Roles = "1"), AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiSalesTrainingController(RecruitmentCrmContext db, AiSalesLearningService learning,
    AiSemanticKnowledgeService semantic, IOptions<AiSalesLearningOptions> options, AiLearningSourceService sources,
    AiSalesEvaluationService evaluations, AiSalesCrmActionService actions) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public sealed record ConfigureRequest(Guid AccountId, bool Paused, bool IncludeOpenHistory, long ExpectedVersion);
    public sealed record ReviewRequest(long ExpectedVersion, bool Approve, [Required, MaxLength(150)] string Title,
        [Required, MaxLength(4000)] string Content, DateTime ValidUntil, bool PrivacyReviewed);
    public sealed record OfficialRequest(Guid AccountId, AiKnowledgeCategory Category, [Required, MaxLength(150)] string Title,
        [Required, MaxLength(4000)] string Content, [Required, MaxLength(500)] string SourceReference, DateTime ValidUntil, bool PrivacyReviewed);
    public sealed record FeedbackRequest(Guid Id, Guid DraftId, AiFeedbackKind Kind, [MaxLength(1000)] string? Note, [MaxLength(3000)] string? EditedReply);
    [HttpGet("status")]
    public IActionResult Status() => Ok(new { options.Value.Enabled, options.Value.BackgroundEnabled, options.Value.ExternalProcessingApproved,
        options.Value.PrivacyApproved, options.Value.SemanticRetrievalEnabled, options.Value.ApprovedAccountIds,
        options.Value.MaxCallsPerDay, options.Value.MaxIndexedKnowledge, options.Value.RetentionDays, sendsEnabledByTraining = false });
    [HttpGet("cost-estimate")]
    public Task<IActionResult> CostEstimate([FromQuery] AiSalesCostAssumptions assumptions) => Run(() => Task.FromResult<IActionResult>(Ok(AiSalesCostEstimator.Estimate(assumptions, options.Value))));
    [HttpGet("drafts")]
    public Task<IActionResult> Drafts(Guid accountId, CancellationToken ct) => Run(async () => {
        learning.Require(accountId); return Ok(await db.AiSalesDrafts.AsNoTracking().Where(d => d.TenantId == learning.Tenant &&
            db.WhatsAppConversations.Any(c => c.Id == d.ConversationId && c.WhatsAppAccountId == accountId)).OrderByDescending(d => d.CreatedAt)
            .Select(d => new { d.Id, d.ConversationId, d.Reply, d.Status, d.CreatedAt }).Take(30).ToArrayAsync(ct));
    });
    [HttpPost("knowledge/{id:guid}/disable")]
    public Task<IActionResult> Disable(Guid id, AiSalesEngineController.VersionRequest r, CancellationToken ct) => Run(async () => {
        var k = await db.AiSalesKnowledge.SingleOrDefaultAsync(k => k.Id == id && k.TenantId == learning.Tenant, ct) ?? throw new AiSalesValidation("Knowledge unavailable.");
        learning.RequireScope(k.WhatsAppAccountId);
        await using var gate = await ConversationLock.AcquireAsync(db, "knowledge-account:" + learning.Tenant + ":" + k.WhatsAppAccountId, ct);
        await db.Entry(k).ReloadAsync(ct); if (k.Version != r.ExpectedVersion) throw new AiSalesConflict("Knowledge changed.");
        k.Status = AiKnowledgeStatus.Retired; k.Version++;
        var index = await db.AiKnowledgeIndexes.SingleOrDefaultAsync(i => i.KnowledgeId == k.Id && i.TenantId == learning.Tenant, ct);
        if (index != null) index.Embedding = [];
        learning.Audit("AiLearningKnowledgeDisabled", id, Actor); await db.SaveChangesAsync(ct); return Ok();
    });
    public sealed record QuickCaseRequest(Guid AccountId, [Required, MaxLength(100)] string Dataset, [Required, MaxLength(100)] string Scenario,
        [Required, MaxLength(4000)] string Question, [Required, MaxLength(2000)] string Expected, bool PrivacyReviewed);
    [HttpPost("evaluations/quick-case")]
    public Task<IActionResult> QuickCase(QuickCaseRequest r, CancellationToken ct) => Run(async () => {
        learning.Require(r.AccountId); var now = DateTime.UtcNow;
        var facts = await db.AiSalesKnowledge.AsNoTracking().Where(k => k.TenantId == learning.Tenant && k.WhatsAppAccountId == r.AccountId &&
            k.Status == AiKnowledgeStatus.Approved && k.ValidFrom <= now && k.ValidUntil > now).OrderByDescending(k => k.ApprovedAt).Take(500).ToArrayAsync(ct);
        var matches = await semantic.RankAsync(learning.Tenant, r.AccountId, r.Question, facts, ct);
        var knowledge = matches.Where(m => m.Knowledge.Kind == AiKnowledgeKind.Fact).Take(6).Concat(matches.Where(m => m.Knowledge.Kind == AiKnowledgeKind.Playbook).Take(2))
            .Select(m => new AiContextFact(m.Knowledge.Id, m.Knowledge.Version, m.Knowledge.Kind, m.Knowledge.Title, m.Knowledge.Content, m.Knowledge.ValidUntil)).ToArray();
        var context = new AiSalesContext(new(null,null,null,null,null,"",0), [new(Guid.NewGuid(), "customer", "Text", r.Question, now)], knowledge, AiSalesLanguage.Detect(r.Question));
        return Ok(await evaluations.AddAsync(new(r.AccountId, r.Dataset, r.Scenario, r.Expected, context, r.PrivacyReviewed), Actor, ct));
    });
    public sealed record HistoricalCaseRequest(Guid AccountId, Guid AnalysisId, [Required, MaxLength(100)] string Dataset,
        [Required, MaxLength(100)] string Scenario, [Required, MaxLength(2000)] string Expected, bool PrivacyReviewed);
    [HttpPost("evaluations/from-analysis")]
    public Task<IActionResult> HistoricalCase(HistoricalCaseRequest r, CancellationToken ct) => Run(async () => {
        learning.Require(r.AccountId);
        var analysis = await db.AiConversationAnalyses.AsNoTracking().SingleOrDefaultAsync(a => a.Id == r.AnalysisId && a.TenantId == learning.Tenant && a.WhatsAppAccountId == r.AccountId && a.State == AiLearningState.Completed, ct)
            ?? throw new AiSalesValidation("Scoped completed analysis unavailable.");
        if (!analysis.IsEvaluationHoldout) throw new AiSalesValidation("Choose a reserved evaluation conversation; ordinary learning conversations must not become independent held-out cases.");
        var source = await sources.ReadAsync(r.AccountId, analysis.ConversationId, ct);
        if (source.Fingerprint != analysis.Fingerprint) throw new AiSalesConflict("Evaluation source changed; reprocess it.");
        var turns = source.Messages.Where(m => m.Type is "Text" or "TranscribedAudio").ToArray();
        var lastCustomer = Array.FindLastIndex(turns, m => m.Speaker == "customer");
        if (lastCustomer < 0) throw new AiSalesValidation("No interpreted customer text available.");
        var history = turns.Take(lastCustomer+1).TakeLast(20).Select(m => new AiContextMessage(m.Id, m.Speaker == "customer" ? "customer" : "staff_or_script", m.Type, m.Text, DateTime.UtcNow)).ToArray();
        var facts = await db.AiSalesKnowledge.AsNoTracking().Where(k => k.TenantId == learning.Tenant && k.WhatsAppAccountId == r.AccountId && k.Status == AiKnowledgeStatus.Approved && k.ValidFrom <= DateTime.UtcNow && k.ValidUntil > DateTime.UtcNow).OrderByDescending(k => k.ApprovedAt).Take(500).ToArrayAsync(ct);
        var matches = await semantic.RankAsync(learning.Tenant, r.AccountId, history.Last().Text, facts, ct);
        var knowledge = matches.Where(m => m.Knowledge.Kind == AiKnowledgeKind.Fact).Take(6).Concat(matches.Where(m => m.Knowledge.Kind == AiKnowledgeKind.Playbook).Take(2))
            .Select(m => new AiContextFact(m.Knowledge.Id, m.Knowledge.Version, m.Knowledge.Kind, m.Knowledge.Title, m.Knowledge.Content, m.Knowledge.ValidUntil)).ToArray();
        var context = new AiSalesContext(new(null,null,null,null,null,"",0), history, knowledge, analysis.Language, ((LeadStage)source.Chat.LeadStage).ToString());
        return Ok(await evaluations.AddAsync(new(r.AccountId,r.Dataset,r.Scenario,r.Expected,context,r.PrivacyReviewed,r.AnalysisId),Actor,ct));
    });
    [HttpGet("dashboard")]
    public Task<IActionResult> Dashboard(Guid accountId, CancellationToken ct) => Run(async () => {
        learning.Require(accountId);
        var latest = await LatestAnalyses(accountId).Select(a => new { a.ConversationId, a.State, a.VerifiedOutcome, a.OutcomeEvidence }).ToArrayAsync(ct);
        var signalJson = await LatestAnalyses(accountId).Where(a => a.State == AiLearningState.Completed).OrderByDescending(a => a.CompletedAt).Take(500).Select(a => a.InsightsJson).ToArrayAsync(ct);
        var knowledge = db.AiSalesKnowledge.AsNoTracking().Where(k => k.TenantId == learning.Tenant && k.WhatsAppAccountId == accountId);
        var now = DateTime.UtcNow;
        var drafts = db.AiSalesDrafts.AsNoTracking().Where(d => d.TenantId == learning.Tenant && db.WhatsAppConversations.Any(c => c.Id == d.ConversationId && c.WhatsAppAccountId == accountId));
        var usage = await db.AiLearningUsage.AsNoTracking().Where(u => u.TenantId == learning.Tenant && u.CreatedAt >= now.Date).ToListAsync(ct);
        var feedback = await db.AiSalesFeedback.AsNoTracking().Where(f => f.TenantId == learning.Tenant && drafts.Any(d => d.Id == f.DraftId)).Select(f => new { f.Kind }).ToListAsync(ct);
        var events = signalJson.SelectMany(json => JsonSerializer.Deserialize<AiLearningExtraction[]>(json, OpenAiLearningProvider.Json) ?? []).ToArray();
        var total = await db.WhatsAppConversations.CountAsync(c => c.WhatsAppAccountId == accountId, ct);
        var takeovers = await db.AiControlAudits.Where(a => a.TenantId == learning.Tenant && a.Event.ToLower().Contains("takeover") &&
            db.WhatsAppConversations.Any(c => c.Id == a.ConversationId && c.WhatsAppAccountId == accountId)).Select(a => a.ConversationId).Distinct().CountAsync(ct);
        var assignments = await db.AiLeadAssignments.Where(a => a.TenantId == learning.Tenant &&
            db.WhatsAppConversations.Any(c => c.Id == a.ConversationId && c.WhatsAppAccountId == accountId)).Select(a => a.ConversationId).Distinct().CountAsync(ct);
        var convertedIds = latest.Where(a => a.VerifiedOutcome == "CRM_CONVERSION").Select(a => a.ConversationId).ToArray();
        var assistedIds = await db.AiOutboundIntents.Where(i => i.TenantId == learning.Tenant && convertedIds.Contains(i.ConversationId) && i.State == "Sent").Select(i => i.ConversationId).Distinct().ToArrayAsync(ct);
        return Ok(new {
            totalConversations = total, processed = latest.Count(a => a.State == AiLearningState.Completed), pending = Math.Max(0, total - latest.Count(a => a.State == AiLearningState.Completed)),
            jobs = latest.GroupBy(a => a.State).Select(g => new { state = g.Key.ToString(), count = g.Count() }),
            verifiedConversions = latest.Where(a => a.VerifiedOutcome == "CRM_CONVERSION").Select(a => a.OutcomeEvidence).Distinct().Count(),
            conversionConversations = convertedIds.Length, unknownOutcomes = latest.Count(a => a.VerifiedOutcome == "UNKNOWN"),
            reviewedBookings = latest.Count(a => a.VerifiedOutcome is "REVIEWED_BOOKING" or "CRM_RECORDED_APPOINTMENT"),
            aiAssistedRecordedConversions = latest.Where(a => assistedIds.Contains(a.ConversationId)).Select(a => a.OutcomeEvidence).Distinct().Count(),
            correlationIsNotCausation = true, signalWindowConversations = signalJson.Length, signalWindowLimit = 500,
            pendingKnowledge = await knowledge.CountAsync(k => k.Status == AiKnowledgeStatus.Draft, ct),
            approvedKnowledge = await knowledge.CountAsync(k => k.Status == AiKnowledgeStatus.Approved && k.ValidUntil > now && k.ValidFrom <= now, ct),
            indexed = await db.AiKnowledgeIndexes.CountAsync(i => i.TenantId == learning.Tenant && i.WhatsAppAccountId == accountId && i.Embedding.Length > 0 &&
                knowledge.Any(k => k.Id == i.KnowledgeId && k.Status == AiKnowledgeStatus.Approved && k.Version == i.KnowledgeVersion && k.ValidUntil > now), ct),
            questions = events.SelectMany(e => e.Questions).GroupBy(x => x).OrderByDescending(g => g.Count()).Take(15).Select(g => new { text = g.Key, count = g.Count() }),
            objections = events.SelectMany(e => e.Objections).GroupBy(x => x).OrderByDescending(g => g.Count()).Take(15).Select(g => new { text = g.Key, count = g.Count() }),
            feedback = feedback.GroupBy(f => f.Kind).Select(g => new { kind = g.Key.ToString(), count = g.Count() }),
            quality = new { evaluated = feedback.Count, helpful = feedback.Count(f => f.Kind == AiFeedbackKind.Helpful), accuracy = (double?)null, reason = "Feedback is not an independent accuracy score" },
            takeovers, assignedConversations = assignments, takeoverRate = assignments > 0 ? (double?)takeovers / assignments : null, draftCount = await drafts.CountAsync(ct),
            inputTokens = usage.Sum(u => u.InputTokens), outputTokens = usage.Sum(u => u.OutputTokens), reservedOrInterruptedCalls = usage.Count(u => u.Status != "Completed"),
            estimatedCost = usage.Sum(u => u.InputTokens * (u.Kind.Contains("embedding") ? options.Value.EmbeddingCostPerMillion : options.Value.InputCostPerMillion) + u.OutputTokens * options.Value.OutputCostPerMillion) / 1_000_000m,
            costRatesConfigured = options.Value.InputCostPerMillion > 0 || options.Value.OutputCostPerMillion > 0 || options.Value.EmbeddingCostPerMillion > 0,
            settings = await db.AiLearningAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.TenantId == learning.Tenant && a.WhatsAppAccountId == accountId, ct)
        });
    });
    [HttpPost("configure")]
    public Task<IActionResult> Configure(ConfigureRequest r, CancellationToken ct) => Run(async () => {
        learning.Require(r.AccountId);
        if (!await db.WhatsAppAccounts.AnyAsync(a => a.Id == r.AccountId && a.IsActive, ct)) throw new AiSalesValidation("Account inactive.");
        await using var gate = await ConversationLock.AcquireAsync(db, "learning-scan:" + learning.Tenant + ":" + r.AccountId, ct);
        var config = await db.AiLearningAccounts.SingleOrDefaultAsync(a => a.TenantId == learning.Tenant && a.WhatsAppAccountId == r.AccountId, ct);
        if ((config?.Version ?? 0) != r.ExpectedVersion) throw new AiSalesConflict("Settings changed.");
        if (config == null) { config = new() { TenantId = learning.Tenant, WhatsAppAccountId = r.AccountId, Version = 0 }; db.Add(config); }
        config.Paused = r.Paused; config.IncludeOpenHistory = r.IncludeOpenHistory; config.Version++;
        learning.Audit(r.Paused ? "AiLearningPaused" : "AiLearningResumed", config.Id, Actor); await db.SaveChangesAsync(ct); return Ok(config);
    });
    [HttpPost("scan")]
    public Task<IActionResult> Scan(Guid accountId, CancellationToken ct) => Run(async () => Ok(new { queued = await learning.ScanAsync(accountId, ct) }));
    [HttpPost("analyses/{id:guid}/process")]
    public Task<IActionResult> Process(Guid id, CancellationToken ct) => Run(async () => { await learning.ProcessAsync(id, ct); return Ok(); });
    [HttpPost("analyses/{id:guid}/retry")]
    public Task<IActionResult> Retry(Guid id, AiSalesEngineController.VersionRequest r, CancellationToken ct) => Run(async () => {
        await using var gate = await ConversationLock.AcquireAsync(db, "learning-analysis:" + id, ct);
        var job = await db.AiConversationAnalyses.SingleOrDefaultAsync(a => a.Id == id && a.TenantId == learning.Tenant, ct) ?? throw new AiSalesValidation("Analysis unavailable.");
        learning.Require(job.WhatsAppAccountId);
        if (job.Version != r.ExpectedVersion || job.State != AiLearningState.Failed) throw new AiSalesConflict("Analysis changed or is not failed.");
        if ((await sources.ReadAsync(job.WhatsAppAccountId, job.ConversationId, ct)).Fingerprint != job.Fingerprint) throw new AiSalesConflict("Source changed; run a new scan.");
        job.Attempts = 0; job.State = AiLearningState.Pending; job.AvailableAt = DateTime.UtcNow; job.ErrorCode = ""; job.Version++;
        learning.Audit("AiLearningRetryRequested", job.Id, Actor); await db.SaveChangesAsync(ct); return Ok();
    });
    [HttpGet("analyses")]
    public Task<IActionResult> Analyses(Guid accountId, CancellationToken ct) => Run(async () => {
        learning.Require(accountId); return Ok(await db.AiConversationAnalyses.AsNoTracking().Where(a => a.TenantId == learning.Tenant && a.WhatsAppAccountId == accountId)
            .OrderByDescending(a => a.CreatedAt).Take(50).Select(a => new { a.Id, a.ConversationId, a.State, a.Attempts, a.Version,
                a.InferredStage, a.Language, a.VerifiedOutcome, a.OutcomeEvidence, a.InsightsJson, a.IsEvaluationHoldout, a.ErrorCode,
                conversationName = db.WhatsAppConversations.Where(c => c.Id == a.ConversationId).Select(c => c.Contact.Name).FirstOrDefault() }).ToListAsync(ct));
    });
    [HttpGet("knowledge")]
    public Task<IActionResult> Knowledge(Guid accountId, CancellationToken ct) => Run(async () => {
        learning.Require(accountId); return Ok(await db.AiSalesKnowledge.AsNoTracking().Where(k => k.TenantId == learning.Tenant && k.WhatsAppAccountId == accountId)
            .GroupJoin(db.AiKnowledgeIndexes.AsNoTracking(), k => k.Id, i => i.KnowledgeId, (k, indexes) => new { knowledge = k, index = indexes.FirstOrDefault() })
            .OrderByDescending(k => k.knowledge.CreatedAt).Take(100).ToListAsync(ct));
    });
    [HttpPost("knowledge/{id:guid}/review")]
    public Task<IActionResult> Review(Guid id, ReviewRequest r, CancellationToken ct) => Run(async () => {
        await learning.ReviewAsync(id, r.ExpectedVersion, r.Approve, r.Title, r.Content, r.ValidUntil, r.PrivacyReviewed, Actor, ct); return Ok();
    });
    [HttpPost("official")]
    public Task<IActionResult> Official(OfficialRequest r, CancellationToken ct) => Run(async () => {
        learning.Require(r.AccountId);
        if (!r.PrivacyReviewed || !Enum.IsDefined(r.Category) || r.ValidUntil.Kind == DateTimeKind.Unspecified || r.ValidUntil <= DateTime.UtcNow || r.ValidUntil > DateTime.UtcNow.AddDays(90))
            throw new AiSalesValidation("Review privacy/category and current validity within 90 days.");
        var k = new AiSalesKnowledge { TenantId = learning.Tenant, WhatsAppAccountId = r.AccountId, Title = r.Title.Trim(), Content = r.Content.Trim(),
            Kind = r.Category is AiKnowledgeCategory.Business or AiKnowledgeCategory.Pricing or AiKnowledgeCategory.Faq ? AiKnowledgeKind.Fact : AiKnowledgeKind.Playbook,
            SourceReference = r.SourceReference.Trim(), ValidFrom = DateTime.UtcNow, ValidUntil = r.ValidUntil.ToUniversalTime(), CreatedById = Actor };
        db.Add(k); db.Add(new AiKnowledgeIndex { KnowledgeId = k.Id, TenantId = learning.Tenant, WhatsAppAccountId = r.AccountId, Category = r.Category,
            ContentHash = AiLearningSourceService.Hash(IntakeScript.Normalize(k.Content)), Confidence = 1 });
        learning.Audit("AiOfficialKnowledgeProposed", k.Id, Actor); await db.SaveChangesAsync(ct); return Ok(k);
    });
    [HttpPost("index")]
    public Task<IActionResult> Index(Guid accountId, CancellationToken ct) => Run(async () => { await semantic.IndexAsync(accountId, ct); return Ok(); });
    [HttpGet("search")]
    public Task<IActionResult> Search(Guid accountId, [MaxLength(1000)] string query, CancellationToken ct) => Run(async () => {
        learning.Require(accountId); var now = DateTime.UtcNow;
        var k = await db.AiSalesKnowledge.AsNoTracking().Where(k => k.TenantId == learning.Tenant && k.WhatsAppAccountId == accountId &&
            k.Status == AiKnowledgeStatus.Approved && k.ValidFrom <= now && k.ValidUntil > now).OrderByDescending(k => k.ApprovedAt).Take(options.Value.MaxIndexedKnowledge).ToArrayAsync(ct);
        return Ok((await semantic.RankAsync(learning.Tenant, accountId, query, k, ct)).Take(8));
    });
    [HttpPost("feedback")]
    public Task<IActionResult> Feedback(FeedbackRequest r, CancellationToken ct) => Run(async () => {
        var draft = await db.AiSalesDrafts.AsNoTracking().SingleOrDefaultAsync(d => d.Id == r.DraftId && d.TenantId == learning.Tenant, ct)
            ?? throw new AiSalesValidation("Scoped draft unavailable.");
        var account = await db.WhatsAppConversations.Where(c => c.Id == draft.ConversationId).Select(c => c.WhatsAppAccountId).SingleAsync(ct); learning.Require(account);
        if (r.Id == Guid.Empty || !Enum.IsDefined(r.Kind) || r.Kind == AiFeedbackKind.HumanEdited && string.IsNullOrWhiteSpace(r.EditedReply)) throw new AiSalesValidation("Invalid feedback.");
        var old = await db.AiSalesFeedback.FindAsync([r.Id], ct);
        var note = AiLearningSourceService.Redact(r.Note ?? "", []);
        var edited = AiLearningSourceService.Redact(r.EditedReply ?? "", []);
        if (old != null) {
            if (old.TenantId != learning.Tenant || old.ActorId != Actor || old.DraftId != r.DraftId || old.Kind != r.Kind || old.Note != note || old.EditedReply != edited) throw new AiSalesConflict("Feedback key reused.");
            return Ok(old);
        }
        var item = new AiSalesFeedback { Id = r.Id, TenantId = learning.Tenant, DraftId = r.DraftId, ActorId = Actor, Kind = r.Kind,
            Note = note, EditedReply = edited };
        db.Add(item); learning.Audit("AiFeedbackRecorded", item.Id, Actor); await db.SaveChangesAsync(ct); return Ok(item);
    });
    [HttpGet("performance")]
    public Task<IActionResult> Performance(Guid accountId, CancellationToken ct) => Run(async () => {
        learning.Require(accountId);
        var current = await LatestAnalyses(accountId).Where(a => a.State == AiLearningState.Completed).Select(a => new { a.SalespersonId, a.Cohort, a.VerifiedOutcome, a.OutcomeEvidence }).ToArrayAsync(ct);
        var authorIds = current.Where(a => a.SalespersonId != null).Select(a => a.SalespersonId!.Value).Distinct().ToArray();
        var names = await db.Users.AsNoTracking().Where(u => authorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return Ok(new { caveat = "Descriptive CRM outcomes, not causal sales rankings. Multiple/device authors remain unattributed; compare only within cohorts. Unknown outcomes are explicit.",
            cohorts = current.GroupBy(a => new { a.SalespersonId, a.Cohort }).Select(g => new { g.Key,
                salespersonName = g.Key.SalespersonId != null ? names.GetValueOrDefault(g.Key.SalespersonId.Value, "موظف سابق") : "متحدث غير محدد",
                observed = g.Count(), conversions = g.Count(x => x.VerifiedOutcome == "CRM_CONVERSION"),
                uniqueRecordedConversions = g.Where(x => x.VerifiedOutcome == "CRM_CONVERSION").Select(x => x.OutcomeEvidence).Distinct().Count(),
                visits = g.Count(x => x.VerifiedOutcome == "VERIFIED_VISIT"), recordedLost = g.Count(x => x.VerifiedOutcome == "CRM_RECORDED_LOST"), unknown = g.Count(x => x.VerifiedOutcome == "UNKNOWN"),
                recordedBookings = g.Count(x => x.VerifiedOutcome is "REVIEWED_BOOKING" or "CRM_RECORDED_APPOINTMENT"),
                observedConversionRate = (double)g.Count(x => x.VerifiedOutcome == "CRM_CONVERSION") / g.Count() }) });
    });
    [HttpDelete("conversations/{id:guid}/derived")]
    public Task<IActionResult> Forget(Guid id, Guid accountId, CancellationToken ct) => Run(async () => {
        learning.RequireScope(accountId); if (!await db.WhatsAppConversations.AnyAsync(c => c.Id == id && c.WhatsAppAccountId == accountId, ct)) return NotFound();
        await using var gate = await ConversationLock.AcquireAsync(db, "learning-scan:" + learning.Tenant + ":" + accountId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var config = await db.AiLearningAccounts.SingleOrDefaultAsync(a => a.TenantId == learning.Tenant && a.WhatsAppAccountId == accountId, ct);
        if (config == null) { config = new AiLearningAccount { TenantId = learning.Tenant, WhatsAppAccountId = accountId }; db.Add(config); }
        config.ExcludedConversationIds = config.ExcludedConversationIds.Append(id).Distinct().ToArray(); config.Version++;
        var analyses = await db.AiConversationAnalyses.Where(a => a.TenantId == learning.Tenant && a.ConversationId == id).ToListAsync(ct);
        var ids = analyses.Select(a => a.Id).ToArray();
        var knowledgeIds = await db.AiKnowledgeIndexes.Where(i => i.TenantId == learning.Tenant && ids.Contains(i.AnalysisId!.Value)).Select(i => i.KnowledgeId).ToArrayAsync(ct);
        db.AiSalesKnowledge.RemoveRange(await db.AiSalesKnowledge.Where(k => k.TenantId == learning.Tenant && knowledgeIds.Contains(k.Id)).ToListAsync(ct));
        db.AiConversationAnalyses.RemoveRange(analyses);
        db.AiSalesFeedback.RemoveRange(await db.AiSalesFeedback.Where(f => f.TenantId == learning.Tenant && db.AiSalesDrafts.Any(d => d.Id == f.DraftId && d.ConversationId == id)).ToListAsync(ct));
        learning.Audit("AiDerivedDataErased", id, Actor); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok();
    });
    [HttpPost("actions/{id:guid}")]
    public Task<IActionResult> Action(Guid id, AiCrmActionRequest r, CancellationToken ct) => Run(async () => Ok(await actions.ExecuteAsync(id, Actor, r, ct)));
    [HttpGet("evaluations")]
    public Task<IActionResult> EvaluationCases(Guid accountId, CancellationToken ct) => Run(async () => { learning.Require(accountId); return Ok(await evaluations.ListAsync(accountId, ct)); });
    [HttpPost("evaluations")]
    public Task<IActionResult> AddCase(AiEvaluationCaseRequest r, CancellationToken ct) => Run(async () => Ok(await evaluations.AddAsync(r, Actor, ct)));
    [HttpPost("evaluations/run")]
    public Task<IActionResult> Evaluate(AiEvaluationRunRequest r, CancellationToken ct) => Run(async () => Ok(await evaluations.RunAsync(r, Actor, ct)));
    [HttpPost("evaluations/{id:guid}/score")]
    public Task<IActionResult> Score(Guid id, AiEvaluationScoreRequest r, CancellationToken ct) => Run(async () => { await evaluations.ScoreAsync(id, r, Actor, ct); return Ok(); });
    [HttpGet("evaluations/compare")]
    public Task<IActionResult> Compare(Guid accountId, string dataset, string baseline, string candidate, CancellationToken ct) => Run(async () => Ok(await evaluations.CompareAsync(accountId, dataset, baseline, candidate, ct)));
    private IQueryable<AiConversationAnalysis> LatestAnalyses(Guid account) => db.AiConversationAnalyses.FromSqlInterpolated($"""
        SELECT DISTINCT ON ("ConversationId") * FROM demorecruitment."AiConversationAnalyses"
        WHERE "TenantId" = {learning.Tenant} AND "WhatsAppAccountId" = {account}
        ORDER BY "ConversationId", "CreatedAt" DESC, "Id" DESC
        """).AsNoTracking();
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (AiSalesValidation ex) { return UnprocessableEntity(new { error = ex.Message }); }
        catch (AiSalesConflict ex) { return Conflict(new { error = ex.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "State changed; refresh." }); }
    }
}
