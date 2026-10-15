using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using Moq;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    [Fact]
    public async Task Configured_pilot_rejects_other_conversations_before_context_reads_or_provider_calls()
    {
        var s = await SeedAsync(); var fact = await EngineSeedAsync(s); await using var db = fixture.Create();
        var provider = GoodProvider(fact.Id);
        var service = new AiSalesDraftService(db, Control(db, s), new(db), provider.Object,
            Options.Create(new AiSalesEngineOptions { DraftsEnabled = true, ExternalProcessingApproved = true,
                ApprovedConversationIds = [s.Chat] }));
        await Assert.ThrowsAsync<AiSalesValidation>(() => service.GenerateAsync(Guid.NewGuid(), s.Admin, 1, "outside-pilot", default));
        provider.Verify(p => p.GenerateAsync(It.IsAny<AiSalesContext>(), It.IsAny<CancellationToken>()), Times.Never);
        var approved = await service.GenerateAsync(s.Chat, s.Admin, 1, "approved-pilot", default);
        Assert.Equal(AiDraftStatus.ReviewRequired, approved.Status);
        provider.Verify(p => p.GenerateAsync(It.IsAny<AiSalesContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    private async Task<AiSalesKnowledge> EngineSeedAsync(Seed s, bool fact = true)
    {
        await using var db = fixture.Create();
        await Control(db, s).AssignAsync(s.Lead, s.Chat, s.Agent, s.Admin, 0, "Draft pilot", "engine-assign:" + s.Chat, default);
        var chat = (await db.WhatsAppConversations.FindAsync(s.Chat))!;
        db.WhatsAppMessages.Add(new() { Id = Guid.NewGuid(), ConversationId = s.Chat, WhatsAppAccountId = chat.WhatsAppAccountId,
            Direction = (byte)MessageDirection.Incoming, MessageType = (byte)WhatsAppMessageType.Text, MessageSource = (byte)MessageSource.Customer,
            Status = (byte)MessageStatus.Received, TextBody = "ممكن أعرف عنوان المكتب؟", CreatedAt = DateTime.UtcNow, WhatsAppTimestamp = DateTime.UtcNow });
        var k = new AiSalesKnowledge { TenantId = s.Tenant, WhatsAppAccountId = chat.WhatsAppAccountId,
            Title = "عنوان المكتب", Content = "المكتب التجريبي في القاهرة. الزيارة استشارة عامة وليست ضمانًا لوظيفة.", SourceReference = "Synthetic office reference",
            Kind = fact ? AiKnowledgeKind.Fact : AiKnowledgeKind.Playbook, Status = AiKnowledgeStatus.Approved,
            CreatedById = s.Admin, ApprovedById = s.Admin, ApprovedAt = DateTime.UtcNow, ValidFrom = DateTime.UtcNow.AddDays(-1), ValidUntil = DateTime.UtcNow.AddDays(1) };
        db.AiSalesKnowledge.Add(k); await db.SaveChangesAsync(); return k;
    }
    private static AiSalesDraftService Engine(RecruitmentCrmContext db, Seed s, IAiSalesDraftProvider provider,
        bool external = true, int daily = 20) => new(db, Control(db, s), new(db), provider,
            Options.Create(new AiSalesEngineOptions { DraftsEnabled = true, ExternalProcessingApproved = external, MaxDraftsPerDay = daily }));
    private static Mock<IAiSalesDraftProvider> GoodProvider(Guid fact)
    {
        var p = new Mock<IAiSalesDraftProvider>(MockBehavior.Strict);
        p.Setup(x => x.GenerateAsync(It.IsAny<AiSalesContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(
            new AiDraftProposal("المكتب التجريبي في القاهرة، والزيارة استشارة عامة من غير ضمان وظيفة.", "answer", "Verify the office reference before using this draft.", false, [fact], "synthetic-model", 100, 40));
        return p;
    }

    [Fact]
    public async Task Draft_is_idempotent_preserves_CRM_fields_and_never_creates_an_outgoing_message()
    {
        var s = await SeedAsync(); var fact = await EngineSeedAsync(s); await using var db = fixture.Create(); var p = GoodProvider(fact.Id);
        var service = Engine(db, s, p.Object);
        var draft = await service.GenerateAsync(s.Chat, s.Admin, 1, "draft-one", default);
        var replay = await service.GenerateAsync(s.Chat, s.Admin, 1, "draft-one", default);
        Assert.Equal(draft.Id, replay.Id); Assert.Equal(AiDraftStatus.ReviewRequired, draft.Status);
        Assert.Equal(100, draft.InputTokens); Assert.Equal(40, draft.OutputTokens);
        p.Verify(x => x.GenerateAsync(It.IsAny<AiSalesContext>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal((byte)48, (await db.Leads.FindAsync(s.Lead))!.Age);
        Assert.False(await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == s.Chat && m.Direction == (byte)MessageDirection.Outgoing));
        var reviewed = await service.ReviewAsync(draft.Id, s.Admin, true, default);
        Assert.Equal(AiDraftStatus.Reviewed, reviewed.Status); Assert.Equal(s.Admin, reviewed.ReviewedById);
    }

    [Fact]
    public async Task Context_retrieval_excludes_other_tenants_accounts_unapproved_and_expired_facts()
    {
        var s = await SeedAsync(); var fact = await EngineSeedAsync(s); var other = await SeedAsync();
        await using var db = fixture.Create();
        AiSalesKnowledge Copy(Guid tenant, Guid account, AiKnowledgeStatus status, bool expired = false) => new()
        {
            TenantId = tenant, WhatsAppAccountId = account, Title = "Excluded", Content = "untrusted", SourceReference = "test", Kind = AiKnowledgeKind.Fact,
            Status = status, CreatedById = s.Admin, ApprovedById = status == AiKnowledgeStatus.Approved ? s.Admin : null,
            ApprovedAt = status == AiKnowledgeStatus.Approved ? DateTime.UtcNow : null,
            ValidFrom = DateTime.UtcNow.AddDays(-3), ValidUntil = DateTime.UtcNow.AddDays(expired ? -1 : 1)
        };
        var accountId = fact.WhatsAppAccountId;
        db.AddRange(Copy(other.Tenant, accountId, AiKnowledgeStatus.Approved), Copy(s.Tenant, (await db.WhatsAppConversations.FindAsync(other.Chat))!.WhatsAppAccountId, AiKnowledgeStatus.Approved),
            Copy(s.Tenant, accountId, AiKnowledgeStatus.Draft), Copy(s.Tenant, accountId, AiKnowledgeStatus.Approved, true));
        await db.SaveChangesAsync();
        var context = await new AiSalesContextService(db).ReadAsync(s.Tenant, (await db.WhatsAppConversations.FindAsync(s.Chat))!, default);
        Assert.Single(context.Knowledge); Assert.Equal(fact.Id, context.Knowledge[0].Id);
        Assert.Equal((byte)48, context.Customer.Age); Assert.Equal("Welder", context.Customer.Profession);
        Assert.DoesNotContain("Password", JsonSerializer.Serialize(context));
    }

    [Fact]
    public async Task Playbook_alone_does_not_enable_factual_generation_or_external_processing()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s, false); await using var db = fixture.Create();
        var provider = new Mock<IAiSalesDraftProvider>(MockBehavior.Strict);
        var draft = await Engine(db, s, provider.Object, false).GenerateAsync(s.Chat, s.Admin, 1, "no-facts", default);
        Assert.Equal(AiDraftStatus.ReviewRequired, draft.Status); Assert.True(draft.NeedsHuman); Assert.Equal("local-policy", draft.Model);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task External_processing_requires_its_separate_gate()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); await using var db = fixture.Create();
        var provider = new Mock<IAiSalesDraftProvider>(MockBehavior.Strict);
        var draft = await Engine(db, s, provider.Object, false).GenerateAsync(s.Chat, s.Admin, 1, "external-disabled", default);
        Assert.Equal(AiDraftStatus.Failed, draft.Status); Assert.Empty(draft.Reply); provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Takeover_during_provider_call_invalidates_pending_draft_without_blocking_staff()
    {
        var s = await SeedAsync(); var fact = await EngineSeedAsync(s); await using var db = fixture.Create(); var p = GoodProvider(fact.Id);
        p.Setup(x => x.GenerateAsync(It.IsAny<AiSalesContext>(), It.IsAny<CancellationToken>())).Returns(async () =>
        {
            await using var human = fixture.Create();
            await Control(human, s).ControlAsync(s.Chat, s.Admin, 1, "takeover", "Staff acts during generation", "engine-takeover", default);
            return new AiDraftProposal("old reply", "answer", "old", false, [fact.Id], "synthetic-model", 42, 22);
        });
        var draft = await Engine(db, s, p.Object).GenerateAsync(s.Chat, s.Admin, 1, "pending", default);
        Assert.Equal(AiDraftStatus.Stale, draft.Status); Assert.Empty(draft.Reply);
        Assert.Equal(42, draft.InputTokens); Assert.Equal(22, draft.OutputTokens);
        Assert.Equal(2, (await db.ConversationControls.FindAsync(s.Chat))!.OwnershipVersion);
    }

    [Fact]
    public async Task Retirement_new_messages_and_reviewed_memory_make_drafts_stale()
    {
        var s = await SeedAsync(); var fact = await EngineSeedAsync(s); await using var db = fixture.Create(); var p = GoodProvider(fact.Id); var engine = Engine(db, s, p.Object);
        var draft = await engine.GenerateAsync(s.Chat, s.Admin, 1, "retire-draft", default);
        (await db.AiSalesKnowledge.FindAsync(fact.Id))!.Status = AiKnowledgeStatus.Retired; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AiSalesConflict>(() => engine.ReviewAsync(draft.Id, s.Admin, true, default));
        Assert.Equal(AiDraftStatus.Stale, (await db.AiSalesDrafts.FindAsync(draft.Id))!.Status);
        (await db.AiSalesKnowledge.FindAsync(fact.Id))!.Status = AiKnowledgeStatus.Approved; await db.SaveChangesAsync();
        var next = await engine.GenerateAsync(s.Chat, s.Admin, 1, "memory-draft", default);
        db.AiCustomerMemories.Add(new() { ConversationId = s.Chat, TenantId = s.Tenant, ReviewedSummary = "Customer prefers an afternoon visit.", ReviewedById = s.Admin });
        await db.SaveChangesAsync(); Assert.False(await engine.IsCurrentAsync(next, default));
        var fresh = await engine.GenerateAsync(s.Chat, s.Admin, 1, "message-draft", default);
        db.WhatsAppMessages.Add(new() { Id = Guid.NewGuid(), ConversationId = s.Chat, WhatsAppAccountId = fact.WhatsAppAccountId,
            Direction = (byte)MessageDirection.Incoming, MessageType = (byte)WhatsAppMessageType.Text, TextBody = "سؤال جديد", CreatedAt = DateTime.UtcNow, WhatsAppTimestamp = DateTime.UtcNow });
        await db.SaveChangesAsync(); Assert.False(await engine.IsCurrentAsync(fresh, default));
    }

    [Fact]
    public async Task CRM_changes_and_account_deactivation_invalidate_reviewable_drafts()
    {
        var s = await SeedAsync(); var fact = await EngineSeedAsync(s); await using var db = fixture.Create(); var engine = Engine(db, s, GoodProvider(fact.Id).Object);
        var draft = await engine.GenerateAsync(s.Chat, s.Admin, 1, "crm-state", default);
        (await db.Leads.FindAsync(s.Lead))!.Age = 49; await db.SaveChangesAsync();
        Assert.False(await engine.IsCurrentAsync(draft, default));
        var context = await new AiSalesContextService(db).ReadAsync(s.Tenant, (await db.WhatsAppConversations.FindAsync(s.Chat))!, default);
        Assert.Equal((byte)49, context.Customer.Age);
        (await db.WhatsAppAccounts.FindAsync(fact.WhatsAppAccountId))!.IsActive = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AiSalesValidation>(() => engine.GenerateAsync(s.Chat, s.Admin, 1, "inactive-account", default));
    }

    [Fact]
    public async Task Invented_citation_and_daily_limit_are_rejected()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); await using var db = fixture.Create(); var p = GoodProvider(Guid.NewGuid()); var engine = Engine(db, s, p.Object, daily: 1);
        var draft = await engine.GenerateAsync(s.Chat, s.Admin, 1, "invented", default);
        Assert.Equal(AiDraftStatus.Failed, draft.Status); Assert.Equal("UnapprovedCitation", draft.ErrorCode); Assert.Empty(draft.Reply);
        var replay = await engine.GenerateAsync(s.Chat, s.Admin, 1, "invented", default);
        Assert.Equal(draft.Id, replay.Id); Assert.Equal(AiDraftStatus.Failed, replay.Status);
        await Assert.ThrowsAsync<AiSalesConflict>(() => engine.GenerateAsync(s.Chat, s.Admin, 1, "over-budget", default));
        p.Verify(x => x.GenerateAsync(It.IsAny<AiSalesContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Engine_HTTP_protects_knowledge_memory_and_review_with_role_and_csrf()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); await using var app = await StartHttpAsync(s); using var client = app.GetTestClient();
        await Authenticate(client, s.Admin, "1");
        var token = client.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single(); client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/ai-sales-engine/conversations/{s.Chat}/memory", new { expectedVersion = 0, reviewedSummary = "Staff summary" })).StatusCode);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/ai-sales-engine/conversations/{s.Chat}/memory", new { expectedVersion = 0, reviewedSummary = "Staff summary" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/ai-sales-engine/conversations/{s.Chat}/memory", new { expectedVersion = 0, reviewedSummary = "Stale" })).StatusCode);
        await Authenticate(client, s.Human, "8");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai-sales-engine/knowledge?accountId=" + Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/ai-sales-engine/conversations/{s.Chat}/drafts")).StatusCode);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("handoff")]
    public async Task Provider_stop_or_handoff_without_human_flag_is_blocked_before_review_or_dispatch(string intent)
    {
        var s = await SeedAsync(); var fact = await EngineSeedAsync(s); await using var db = fixture.Create();
        var provider = new Mock<IAiSalesDraftProvider>(MockBehavior.Strict);
        provider.Setup(p => p.GenerateAsync(It.IsAny<AiSalesContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiDraftProposal("Invalid model control result", intent, "Trial regression", false, [fact.Id], "synthetic-model"));
        var draft = await Engine(db, s, provider.Object).GenerateAsync(s.Chat, s.Admin, 1, "invalid-control:" + intent, default);
        Assert.Equal(AiDraftStatus.Failed, draft.Status); Assert.Equal("InvalidHandoff", draft.ErrorCode);
        Assert.Empty(draft.Reply);
        Assert.False(await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == s.Chat && m.Direction == (byte)MessageDirection.Outgoing));
        Assert.False(await db.AiOutboundIntents.AnyAsync(i => i.ConversationId == s.Chat));
        Assert.False(await db.AiOfficeBookings.AnyAsync(b => b.ConversationId == s.Chat));
    }
}

public sealed class ProviderContractTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request);
    }
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task Ongoing_history_or_reviewed_memory_prevents_first_reply_marker(bool priorReply, bool reviewedMemory, bool expectedFirst)
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            using var context = JsonDocument.Parse(body.RootElement.GetProperty("input")[0].GetProperty("content").GetString()!);
            Assert.Equal(expectedFirst, context.RootElement.GetProperty("ConversationState").GetProperty("IsFirstReply").GetBoolean());
            Assert.Contains("do not repeat the welcome", body.RootElement.GetProperty("instructions").GetString());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = "completed", model = "synthetic-model",
                output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = "{\"reply\":\"مراجعة\",\"intent\":\"answer\",\"reviewNote\":\"mock\",\"needsHuman\":true,\"sourceIds\":[]}" } } } } }) };
        }));
        var provider = new OpenAiSalesDraftProvider(http, Options.Create(new AiSalesEngineOptions { DraftsEnabled = true, ExternalProcessingApproved = true, ApiKey = "synthetic", Model = "synthetic" }));
        var history = new List<AiContextMessage>();
        if (priorReply) history.Add(new(Guid.NewGuid(), "staff_or_script", "Text", "أهلاً، أنا المساعد الافتراضي لشركة الفهد العربي.", DateTime.UtcNow.AddMinutes(-1)));
        history.Add(new(Guid.NewGuid(), "customer", "Text", "المرتب كام؟", DateTime.UtcNow));
        await provider.GenerateAsync(new(new(null, null, null, null, null, reviewedMemory ? "Previously introduced assistant" : "", reviewedMemory ? 1 : 0), history.ToArray(), []), default);
    }
    [Fact]
    public async Task Responses_contract_disables_storage_and_uses_strict_schema()
    {
        var factId = Guid.NewGuid(); var playbookId = Guid.NewGuid();
        var expected = new AiDraftProposal("أهلاً", "qualification", "Human review", false, [], "synthetic-model");
        var content = JsonSerializer.Serialize(new { reply = expected.Reply, intent = expected.Intent, reviewNote = expected.ReviewNote, needsHuman = expected.NeedsHuman, sourceIds = Array.Empty<string>() });
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.ToString());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("store").GetBoolean());
            Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
            var modelText = body.RootElement.GetProperty("input")[0].GetProperty("content").GetString()!;
            Assert.Contains("أجيب ورق إيه؟", modelText);
            Assert.DoesNotContain("\\u062", modelText);
            var sourceEnums = body.RootElement.GetProperty("text").GetProperty("format").GetProperty("schema")
                .GetProperty("properties").GetProperty("sourceIds").GetProperty("items").GetProperty("enum")
                .EnumerateArray().Select(x => x.GetString()!).ToArray();
            Assert.Equal([factId.ToString()], sourceEnums);
            Assert.DoesNotContain(playbookId.ToString(), sourceEnums);
            using var contextJson = JsonDocument.Parse(modelText);
            Assert.Equal("أجيب ورق إيه؟", contextJson.RootElement.GetProperty("RecentMessages")[0].GetProperty("Text").GetString());
            Assert.DoesNotContain("synthetic-secret", await request.Content.ReadAsStringAsync());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = "completed", model = "synthetic-model", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = content } } } }, usage = new { input_tokens = 12, output_tokens = 3 } }) };
        }));
        var provider = new OpenAiSalesDraftProvider(http, Options.Create(new AiSalesEngineOptions { DraftsEnabled = true, ExternalProcessingApproved = true, ApiKey = "synthetic-secret", Model = "synthetic-model" }));
        var result = await provider.GenerateAsync(new(new(null, null, null, null, null, "", 0),
            [new(Guid.NewGuid(), "customer", "Text", "أجيب ورق إيه؟", DateTime.UtcNow)],
            [new(factId, 0, AiKnowledgeKind.Fact, "الأوراق", "جواز السفر", DateTime.UtcNow.AddDays(1)),
             new(playbookId, 0, AiKnowledgeKind.Playbook, "الأسلوب", "رد باختصار", DateTime.UtcNow.AddDays(1))]), default);
        Assert.Equal("أهلاً", result.Reply); Assert.Equal(12, result.InputTokens);
    }
    [Fact]
    public async Task Refusal_is_not_misread_as_a_reply()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"no\"}]}]}", Encoding.UTF8, "application/json") })));
        var provider = new OpenAiSalesDraftProvider(http, Options.Create(new AiSalesEngineOptions { DraftsEnabled = true, ExternalProcessingApproved = true, ApiKey = "synthetic", Model = "synthetic" }));
        var ex = await Assert.ThrowsAsync<AiSalesProviderException>(() => provider.GenerateAsync(new(new(null, null, null, null, null, "", 0), [], []), default));
        Assert.Equal("ProviderRefusal", ex.Message);
    }
    [Fact]
    public void Contact_identifiers_are_removed_from_context()
    {
        var text = AiSalesContextService.Redact("Call +20 100 000 0001 or test@example.com. السن 48");
        Assert.DoesNotContain("test@example.com", text); Assert.DoesNotContain("100 000", text); Assert.Contains("48", text);
    }
}
