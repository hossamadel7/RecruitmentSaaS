using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace RecruitmentSaaS.Tests;
public sealed partial class FoundationTests
{
    private static AiSalesLearningOptions LearningOptions(Guid account) => new() { Enabled = true, ExternalProcessingApproved = true,
        PrivacyApproved = true, ApprovedAccountIds = [account], SemanticRetrievalEnabled = true, EmbeddingDimensions = 64, HoldoutPercentage = 0 };
    private static AiLearningResponse Extraction(AiLearningMessage[] messages) => new(new("CONVERTED", "en", ["A role enquiry"],
        ["What are the fees?"], ["Price concern"], ["Clarify the question"], [], [], [], [], [], ["Review current facts"],
        [new(AiKnowledgeCategory.Strategy, "Clarification", "Ask one relevant clarification without inventing offer facts.", .7, [messages[0].Id])]), 40, 10);
    private static Mock<IAiLearningProvider> LearningProvider()
    {
        var p = new Mock<IAiLearningProvider>(MockBehavior.Strict);
        p.Setup(x => x.AnalyseAsync(It.IsAny<AiLearningMessage[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiLearningMessage[] messages, CancellationToken _) => Extraction(messages));
        p.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AiEmbeddingResponse([1, ..new float[63]], 5));
        return p;
    }
    private static AiSalesLearningService Learning(RecruitmentCrmContext db, Seed s, AiSalesLearningOptions o, IAiLearningProvider p) =>
        new(db, Control(db, s), Options.Create(o), new AiLearningSourceService(db, Options.Create(o), Control(db, s)), p);
    private async Task<(Seed Seed, Guid Account)> LearningSeedAsync(int messages = 2)
    {
        var (s, account) = await CorpusSeedAsync(messages); await using var db = fixture.Create();
        db.AiLearningAccounts.Add(new() { TenantId = s.Tenant, WhatsAppAccountId = account, Paused = false, IncludeOpenHistory = true });
        await db.SaveChangesAsync(); return (s, account);
    }
    [Fact]
    public async Task Continuous_learning_is_scoped_idempotent_and_never_promotes_inferred_conversion()
    {
        var (s, account) = await LearningSeedAsync(); await using var db = fixture.Create(); var p = LearningProvider(); var o = LearningOptions(account); var service = Learning(db, s, o, p.Object);
        Assert.Equal(1, await service.ScanAsync(account, default)); Assert.Equal(0, await service.ScanAsync(account, default));
        var job = await db.AiConversationAnalyses.SingleAsync(a => a.TenantId == s.Tenant);
        await service.ProcessAsync(job.Id, default);
        Assert.Equal(AiLearningState.Completed, job.State); Assert.Equal("CONVERTED", job.InferredStage); Assert.Equal("UNKNOWN", job.VerifiedOutcome);
        var k = await db.AiSalesKnowledge.SingleAsync(k => k.TenantId == s.Tenant); Assert.Equal(AiKnowledgeStatus.Draft, k.Status);
        Assert.Null(k.ApprovedById); Assert.Null(k.CreatedById); Assert.Contains(job.Fingerprint, k.SourceReference);
        Assert.Empty(await db.WhatsAppMessages.Where(m => m.ConversationId == s.Chat && m.Direction == 2).ToListAsync());
        Assert.Empty(await db.AiOutboundIntents.Where(i => i.ConversationId == s.Chat).ToListAsync());
        await service.ProcessAsync(job.Id, default); p.Verify(x => x.AnalyseAsync(It.IsAny<AiLearningMessage[]>(), It.IsAny<CancellationToken>()), Times.Once);
        await Assert.ThrowsAsync<AiSalesValidation>(() => service.ScanAsync(Guid.NewGuid(), default));
    }
    [Fact]
    public async Task Changed_source_invalidates_approved_learning_and_creates_a_new_revision()
    {
        var (s, account) = await LearningSeedAsync(); await using var db = fixture.Create(); var o = LearningOptions(account); var p = LearningProvider(); var service = Learning(db, s, o, p.Object);
        await service.ScanAsync(account, default); var job = await db.AiConversationAnalyses.SingleAsync(a => a.TenantId == s.Tenant); await service.ProcessAsync(job.Id, default);
        var k = await db.AiSalesKnowledge.SingleAsync(k => k.TenantId == s.Tenant);
        await Assert.ThrowsAsync<AiSalesValidation>(() => service.ReviewAsync(k.Id, k.Version, true, k.Title, k.Content, DateTime.UtcNow.AddDays(2), false, s.Admin, default));
        await service.ReviewAsync(k.Id, k.Version, true, k.Title, k.Content, DateTime.UtcNow.AddDays(2), true, s.Admin, default);
        var message = await db.WhatsAppMessages.FirstAsync(m => m.ConversationId == s.Chat); message.TextBody = "Changed question"; await db.SaveChangesAsync();
        Assert.False(await service.SourceCurrentAsync(job.Id, default));
        Assert.Equal(1, await service.ScanAsync(account, default));
        Assert.Equal(AiKnowledgeStatus.Retired, k.Status); Assert.Equal(AiLearningState.Stale, job.State);
        Assert.Equal(2, await db.AiConversationAnalyses.CountAsync(a => a.TenantId == s.Tenant));
    }
    [Fact]
    public async Task Interrupted_learning_resumes_saved_chunks_and_retains_failed_usage_receipts()
    {
        var (s, account) = await LearningSeedAsync(25); await using var db = fixture.Create(); var o = LearningOptions(account); var p = LearningProvider(); var calls = 0;
        p.Setup(x => x.AnalyseAsync(It.IsAny<AiLearningMessage[]>(), It.IsAny<CancellationToken>())).ReturnsAsync((AiLearningMessage[] messages, CancellationToken _) => {
            calls++; if (calls == 2) throw new AiSalesValidation("Synthetic provider interruption"); return Extraction(messages);
        });
        var service = Learning(db, s, o, p.Object); await service.ScanAsync(account, default);
        var id = await db.AiConversationAnalyses.Where(a => a.TenantId == s.Tenant).Select(a => a.Id).SingleAsync();
        await service.ProcessAsync(id, default); db.ChangeTracker.Clear(); var failed = await db.AiConversationAnalyses.SingleAsync(a => a.Id == id);
        Assert.Equal(AiLearningState.Failed, failed.State); Assert.Contains("Clarification", failed.InsightsJson);
        failed.AvailableAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(); await service.ProcessAsync(id, default);
        Assert.Equal(AiLearningState.Completed, failed.State); Assert.Equal(3, calls);
        Assert.Equal(3, await db.AiLearningUsage.CountAsync(u => u.TenantId == s.Tenant));
    }
    [Fact]
    public async Task Semantic_index_never_indexes_unapproved_knowledge_and_filters_changed_sources()
    {
        var (s, account) = await LearningSeedAsync(); await using var db = fixture.Create(); var o = LearningOptions(account); var p = LearningProvider(); var learning = Learning(db, s, o, p.Object);
        await learning.ScanAsync(account, default); var job = await db.AiConversationAnalyses.SingleAsync(a => a.TenantId == s.Tenant); await learning.ProcessAsync(job.Id, default);
        var semantic = new AiSemanticKnowledgeService(db, Control(db, s), learning, p.Object, Options.Create(o));
        await semantic.IndexAsync(account, default); p.Verify(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var k = await db.AiSalesKnowledge.SingleAsync(k => k.TenantId == s.Tenant);
        await learning.ReviewAsync(k.Id, k.Version, true, k.Title, k.Content, DateTime.UtcNow.AddDays(2), true, s.Admin, default);
        await semantic.IndexAsync(account, default);
        var match = Assert.Single(await semantic.RankAsync(s.Tenant, account, "clarification", [k], default)); Assert.Equal(1, match.SemanticScore);
        Assert.Equal(64, (await db.AiKnowledgeIndexes.SingleAsync(i => i.KnowledgeId == k.Id)).Embedding.Length);
        var m = await db.WhatsAppMessages.FirstAsync(m => m.ConversationId == s.Chat); m.TextBody = "Edited source"; await db.SaveChangesAsync();
        Assert.Empty(await semantic.RankAsync(s.Tenant, account, "clarification", [k], default));
        await Assert.ThrowsAsync<AiSalesValidation>(() => semantic.RankAsync(Guid.NewGuid(), account, "test", [k], default));
    }
    [Fact]
    public async Task Learning_budget_reserves_attempts_before_provider_calls()
    {
        var (s, account) = await LearningSeedAsync(); await using var db = fixture.Create(); var o = LearningOptions(account); o.MaxCallsPerDay = 1;
        var service = Learning(db, s, o, LearningProvider().Object); await service.ReserveAsync("analysis", "synthetic", default);
        await Assert.ThrowsAsync<AiSalesValidation>(() => service.ReserveAsync("embedding", "synthetic", default));
    }
    [Fact]
    public async Task Learning_pause_blocks_processing_and_disallowed_accounts_never_reach_provider()
    {
        var (s, account) = await LearningSeedAsync(); await using var db = fixture.Create(); var p = LearningProvider(); var o = LearningOptions(account); var service = Learning(db, s, o, p.Object);
        await service.ScanAsync(account, default); var job = await db.AiConversationAnalyses.SingleAsync(a => a.TenantId == s.Tenant);
        var config = await db.AiLearningAccounts.SingleAsync(a => a.TenantId == s.Tenant); config.Paused = true; await db.SaveChangesAsync();
        await service.ProcessAsync(job.Id, default); Assert.Equal(AiLearningState.Pending, job.State);
        p.Verify(x => x.AnalyseAsync(It.IsAny<AiLearningMessage[]>(), It.IsAny<CancellationToken>()), Times.Never);
        o.ExternalProcessingApproved = false; await Assert.ThrowsAsync<AiSalesValidation>(() => service.ProcessAsync(job.Id, default));
    }
    [Fact]
    public async Task Controlled_crm_actions_require_human_approval_and_are_idempotent_without_transport()
    {
        var (s, account) = await LearningSeedAsync(); await using var db = fixture.Create(); var o = LearningOptions(account); var learning = Learning(db, s, o, LearningProvider().Object);
        await Control(db, s).AssignAsync(s.Lead, s.Chat, s.Agent, s.Admin, 0, "Synthetic assignment", "learning-actions", default);
        var actions = new AiSalesCrmActionService(db, learning, Control(db, s), new AiOfficeBookingService(db, Control(db, s), Options.Create(new AiOfficeBookingOptions())));
        var r = new AiCrmActionRequest(account, "add_customer_note", Guid.NewGuid(), 1, false, "Staff-approved note");
        await Assert.ThrowsAsync<AiSalesValidation>(() => actions.ExecuteAsync(s.Chat, s.Admin, r, default)); r = r with { HumanApproved = true };
        await actions.ExecuteAsync(s.Chat, s.Admin, r, default); await actions.ExecuteAsync(s.Chat, s.Admin, r, default);
        Assert.Single(await db.ConversationNotes.Where(n => n.ConversationId == s.Chat).ToListAsync());
        await Assert.ThrowsAsync<AiSalesConflict>(() => actions.ExecuteAsync(s.Chat, s.Admin, r with { Note = "Different data" }, default));
        Assert.Empty(await db.WhatsAppMessages.Where(m => m.ConversationId == s.Chat && m.Direction == 2).ToListAsync());
    }
    [Fact]
    public async Task Evaluation_runs_without_customer_sends_and_requires_scored_fixed_baseline_pairs()
    {
        var (s, account) = await LearningSeedAsync(); await using var db = fixture.Create(); var o = LearningOptions(account); var learning = Learning(db, s, o, LearningProvider().Object);
        var provider = new Mock<IAiSalesDraftProvider>(MockBehavior.Strict);
        provider.Setup(p => p.GenerateAsync(It.IsAny<AiSalesContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AiDraftProposal("A staff member can review that.", "handoff", "Synthetic", true, [], "synthetic-model", 10, 5));
        var evaluations = new AiSalesEvaluationService(db, learning, provider.Object);
        var context = new AiSalesContext(new(null, null, null, null, null, "", 0), [new(Guid.NewGuid(), "customer", "Text", "What are the fees?", DateTime.UtcNow)], [], "en");
        var request = new AiEvaluationCaseRequest(account, "fixed-v1", "price", "No invented fees", context, true);
        var item = await evaluations.AddAsync(request, s.Admin, default);
        await evaluations.RunAsync(new(account, "fixed-v1", "baseline", "baseline"), s.Admin, default);
        await Assert.ThrowsAsync<AiSalesConflict>(() => evaluations.AddAsync(request, s.Admin, default));
        await evaluations.RunAsync(new(account, "fixed-v1", "candidate", "baseline"), s.Admin, default);
        foreach (var result in await db.AiEvaluationResults.Where(r => r.TenantId == s.Tenant).ToListAsync())
            await evaluations.ScoreAsync(result.Id, new(true, "Facts, language, context and escalation reviewed."), s.Admin, default);
        var compared = await evaluations.CompareAsync(account, "fixed-v1", "baseline", "candidate", default);
        var json = System.Text.Json.JsonSerializer.Serialize(compared); Assert.Contains("\"scoredPairs\":1", json);
        Assert.Empty(await db.WhatsAppMessages.Where(m => m.ConversationId == s.Chat && m.Direction == 2).ToListAsync());
        Assert.Empty(await db.AiOutboundIntents.Where(i => i.ConversationId == s.Chat).ToListAsync());
    }
    [Fact]
    public async Task Training_http_enforces_admin_antiforgery_scope_and_erasure_exclusion()
    {
        var (s, account) = await LearningSeedAsync(); await using var app = await StartHttpAsync(s, true); using var client = app.GetTestClient();
        await Authenticate(client, s.Human, "3");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai-sales-training/status")).StatusCode);
        await Authenticate(client, s.Admin, "1"); client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/ai-sales-training/scan?accountId="+account, new {})).StatusCode);
        await Authenticate(client, s.Admin, "1");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync("/api/ai-sales-training/dashboard?accountId="+Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ai-sales-training/dashboard?accountId="+account)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/ai-sales-training/scan?accountId="+account, new {})).StatusCode);
        await using (var db = fixture.Create()) {
            var id = await db.AiConversationAnalyses.Where(a => a.TenantId == s.Tenant).Select(a => a.Id).SingleAsync();
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/ai-sales-training/analyses/{id}/process", new {})).StatusCode);
        }
        app.Services.GetRequiredService<IOptions<AiSalesLearningOptions>>().Value.Enabled = false;
        using var response = await client.DeleteAsync($"/api/ai-sales-training/conversations/{s.Chat}/derived?accountId={account}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        app.Services.GetRequiredService<IOptions<AiSalesLearningOptions>>().Value.Enabled = true;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/ai-sales-training/scan?accountId="+account, new {})).StatusCode);
        await using var check = fixture.Create();
        Assert.Empty(await check.AiConversationAnalyses.Where(a => a.TenantId == s.Tenant).ToListAsync());
        Assert.Empty(await check.AiSalesKnowledge.Where(k => k.TenantId == s.Tenant).ToListAsync());
        Assert.Equal(2, await check.WhatsAppMessages.CountAsync(m => m.ConversationId == s.Chat));
        Assert.Contains(s.Chat, (await check.AiLearningAccounts.SingleAsync(a => a.TenantId == s.Tenant)).ExcludedConversationIds);
    }
    [Fact]
    public async Task Reserved_evaluation_conversations_do_not_publish_training_candidates()
    {
        var (s, account) = await LearningSeedAsync(); await using var db = fixture.Create(); var o = LearningOptions(account); var service = Learning(db,s,o,LearningProvider().Object);
        await service.ScanAsync(account, default); var a = await db.AiConversationAnalyses.SingleAsync(a => a.TenantId == s.Tenant);
        a.IsEvaluationHoldout = true; await db.SaveChangesAsync(); await service.ProcessAsync(a.Id, default);
        Assert.Equal(AiLearningState.Completed,a.State); Assert.Empty(await db.AiSalesKnowledge.Where(k => k.TenantId == s.Tenant).ToListAsync());
    }
}

public sealed class LearningPureTests
{
    [Fact]
    public void An_approved_country_fact_allows_an_answer_while_style_alone_cannot_establish_an_offer()
    {
        var context = new AiSalesContext(new(null,null,null,null,null,"",0),
            [new(Guid.NewGuid(), "customer", "Text", "عندكم فرص السعودية؟", DateTime.UtcNow)],
            [new(Guid.NewGuid(),1,AiKnowledgeKind.Fact,"السعودية — عرض تجريبي معتمد","تفاصيل عرض السعودية للاختبار فقط",DateTime.UtcNow.AddDays(1))]);
        Assert.Null(AiSalesReplyPolicy.Create(context));
        Assert.True(AiSalesReplyPolicy.Create(context with { Knowledge = [context.Knowledge[0] with { Kind = AiKnowledgeKind.Playbook }] })!.NeedsHuman);
    }
    [Fact]
    public void Long_messages_are_processed_without_losing_text_unicode_or_source_identity()
    {
        var text = new string('a', 3999) + "🙂" + new string('ب', 5000);
        var message = new AiLearningMessage(Guid.NewGuid(), "customer", "Text", text);
        var parts = AiSalesLearningService.ChunkMessages([message]).SelectMany(x => x).ToArray();
        Assert.Equal(text, string.Concat(parts.Select(p => p.Text))); Assert.Equal(3, parts.Length);
        Assert.All(parts, part => { Assert.Equal(message.Id, part.Id); Assert.True(part.Text.Length <= 4000); Assert.False(char.IsHighSurrogate(part.Text.Last())); });
        Assert.Equal([1,2,3], parts.Select(p => p.Part).ToArray());
    }
    private sealed class LearningHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handle(request);
    }
    [Fact]
    public async Task Learning_adapter_uses_strict_scoped_evidence_schema_without_storing_or_echoing_credentials()
    {
        var id = Guid.NewGuid(); var message = new AiLearningMessage(id, "customer", "Text", "What are the fees?", DateTime.UtcNow);
        using var http = new HttpClient(new LearningHandler(async request => {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
            var text = await request.Content!.ReadAsStringAsync(); Assert.DoesNotContain("synthetic-key", text);
            using var body = System.Text.Json.JsonDocument.Parse(text);
            Assert.False(body.RootElement.GetProperty("store").GetBoolean());
            var format = body.RootElement.GetProperty("text").GetProperty("format"); Assert.True(format.GetProperty("strict").GetBoolean());
            Assert.Contains(id.ToString(), format.GetProperty("schema").GetRawText());
            var extraction = new AiLearningExtraction("DISCOVERY", "en", [], ["Fees"], [], [], [], [], [], [], [], [], [], Products: [], NegotiationPatterns: [], EffectiveResponses: []);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = "completed", model = "synthetic-extractor",
                output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = System.Text.Json.JsonSerializer.Serialize(extraction, OpenAiLearningProvider.Json) } } } },
                usage = new { input_tokens = 12, output_tokens = 8 } }) };
        }));
        var provider = new OpenAiLearningProvider(http, Options.Create(new AiSalesEngineOptions { ApiKey = "synthetic-key", Model = "synthetic-model" }),
            Options.Create(new AiSalesLearningOptions { Enabled = true, PrivacyApproved = true, ExternalProcessingApproved = true }));
        var result = await provider.AnalyseAsync([message], default);
        Assert.Equal("synthetic-extractor", result.Model); Assert.Equal(12, result.InputTokens);
    }
    [Fact]
    public void Operating_cost_estimate_uses_configured_rates_and_volume_assumptions()
    {
        var result = AiSalesCostEstimator.Estimate(new(100,1000,100,10,2000,200,10000,30),
            new AiSalesLearningOptions { InputCostPerMillion = 1, OutputCostPerMillion = 2, EmbeddingCostPerMillion = .1m });
        var json = System.Text.Json.JsonSerializer.SerializeToElement(result);
        Assert.Equal(.145m, json.GetProperty("dailyEstimatedModelCost").GetDecimal());
        Assert.Equal(4.35m, json.GetProperty("periodEstimatedModelCost").GetDecimal());
    }
    [Theory]
    [InlineData("عايز أعرف التفاصيل", "ar-EG")]
    [InlineData("أرغب في معرفة التفاصيل", "ar")]
    [InlineData("What are the fees?", "en")]
    [InlineData("ana 3ayez a3raf el tafaseel", "arabizi")]
    public void Customer_language_is_detected_without_a_provider(string text, string language) => Assert.Equal(language, AiSalesLanguage.Detect(text));
    [Fact]
    public void Cosine_rejects_incompatible_or_invalid_vectors()
    {
        Assert.Equal(1, AiSemanticKnowledgeService.Cosine([1,0], [1,0]));
        Assert.Equal(0, AiSemanticKnowledgeService.Cosine([1,0], [0,1]));
        Assert.Equal(0, AiSemanticKnowledgeService.Cosine([float.NaN], [1]));
        Assert.Equal(0, AiSemanticKnowledgeService.Cosine([1], [1,0]));
    }
    [Fact]
    public void Extracted_insights_cannot_cite_another_conversations_message()
    {
        var id = Guid.NewGuid();
        var x = new AiLearningExtraction("INTERESTED", "en", [], [], [], [], [], [], [], [], [], [],
            [new(AiKnowledgeCategory.Strategy, "Example", "Contextual response", .8, [Guid.NewGuid()])]);
        Assert.Throws<AiSalesValidation>(() => OpenAiLearningProvider.Validate(x, [id]));
    }
    [Fact]
    public void Privacy_mask_removes_known_names_contact_and_address_lines()
    {
        var text = AiLearningSourceService.Redact("Customer Name\ntest@example.test\n01012345678\nشارع النيل شقة 2", ["Customer Name"]);
        Assert.DoesNotContain("Customer", text); Assert.DoesNotContain("example.test", text); Assert.DoesNotContain("010123", text); Assert.DoesNotContain("النيل", text);
    }
}
