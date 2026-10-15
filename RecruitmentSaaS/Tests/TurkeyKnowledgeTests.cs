using Microsoft.EntityFrameworkCore;
using Moq;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Text.Json;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    private static JsonDocument TurkeyDocument(string name) => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(Directory.GetCurrentDirectory(), "docs", "knowledge", name)));

    public static IEnumerable<object[]> TurkeyQuestions()
    {
        using var doc = TurkeyDocument("turkey-response-checks.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
            yield return [c.GetProperty("id").GetString()!, c.GetProperty("customer").GetString()!];
    }

    // Real owner claims, fictional customer/account, disposable database, and no external model.
    // These checks establish context loading and safety paths, not response semantics or model quality.
    private async Task<(Seed Seed, Dictionary<string, AiSalesKnowledge> Entries)> TurkeySeedAsync(string question)
    {
        var s = await SeedAsync(); await using var db = fixture.Create();
        await Control(db, s).AssignAsync(s.Lead, s.Chat, s.Agent, s.Admin, 0, "Isolated Turkey knowledge check", "turkey-assign:" + s.Chat, default);
        var chat = (await db.WhatsAppConversations.FindAsync(s.Chat))!;
        db.WhatsAppMessages.Add(new() { Id = Guid.NewGuid(), ConversationId = s.Chat, WhatsAppAccountId = chat.WhatsAppAccountId,
            Direction = (byte)MessageDirection.Incoming, MessageType = (byte)WhatsAppMessageType.Text, MessageSource = (byte)MessageSource.Customer,
            Status = (byte)MessageStatus.Received, TextBody = question, CreatedAt = DateTime.UtcNow.AddSeconds(-2), WhatsAppTimestamp = DateTime.UtcNow.AddSeconds(-2) });
        var entries = new Dictionary<string, AiSalesKnowledge>(); using var pack = TurkeyDocument("turkey-owner-knowledge.json");
        foreach (var e in pack.RootElement.GetProperty("entries").EnumerateArray())
        {
            var item = new AiSalesKnowledge { TenantId = s.Tenant, WhatsAppAccountId = chat.WhatsAppAccountId,
                Kind = (AiKnowledgeKind)e.GetProperty("kind").GetByte(), Title = e.GetProperty("title").GetString()!,
                Content = e.GetProperty("content").GetString()!, SourceReference = e.GetProperty("sourceReference").GetString()!,
                Status = AiKnowledgeStatus.Approved, CreatedById = s.Admin, ApprovedById = s.Admin, ApprovedAt = DateTime.UtcNow,
                ValidFrom = DateTime.UtcNow.AddDays(-1), ValidUntil = DateTime.UtcNow.AddDays(1) };
            entries.Add(e.GetProperty("key").GetString()!, item); db.AiSalesKnowledge.Add(item);
        }
        await db.SaveChangesAsync(); return (s, entries);
    }

    [Theory, MemberData(nameof(TurkeyQuestions))]
    public async Task Turkey_questions_receive_all_owner_facts_and_style_without_transport_or_semantic_quality_claim(string id, string question)
    {
        var (s, entries) = await TurkeySeedAsync(question); await using var db = fixture.Create();
        var chat = (await db.WhatsAppConversations.FindAsync(s.Chat))!;
        var context = await new AiSalesContextService(db).ReadAsync(s.Tenant, chat, default);
        Assert.Equal(6, context.Knowledge.Count(k => k.Kind == AiKnowledgeKind.Fact));
        Assert.Equal(2, context.Knowledge.Count(k => k.Kind == AiKnowledgeKind.Playbook));
        Assert.True(entries.Values.Select(e => e.Id).ToHashSet().SetEquals(context.Knowledge.Select(k => k.Id)));
        Assert.Contains("650 إلى 850", context.Knowledge.Single(k => k.Id == entries["turkey_pay_and_work"].Id).Content);
        Assert.Contains("بدون خصومات", context.Knowledge.Single(k => k.Id == entries["turkey_pay_and_work"].Id).Content);
        Assert.Contains("850 إلى 1200", context.Knowledge.Single(k => k.Id == entries["turkey_pay_and_work"].Id).Content);
        Assert.Contains("10 صباحًا", context.Knowledge.Single(k => k.Id == entries["office_visit"].Id).Content);
        Assert.Contains("35000 جنيه مصري", context.Knowledge.Single(k => k.Id == entries["fees_and_unspecified_terms"].Id).Content);
        Assert.Contains("https://maps.google.com/?q=29.991724,31.154583", context.Knowledge.Single(k => k.Id == entries["office_visit"].Id).Content);
        var provider = new Mock<IAiSalesDraftProvider>(MockBehavior.Strict); AiSalesContext? supplied = null;
        provider.Setup(p => p.GenerateAsync(It.IsAny<AiSalesContext>(), It.IsAny<CancellationToken>()))
            .Callback<AiSalesContext, CancellationToken>((c, _) => supplied = c)
            .ReturnsAsync(new AiDraftProposal("الموظف هيراجع التفاصيل مع حضرتك يا فندم.", "handoff", "Offline context test only; no semantic assessment.", true, [], "offline-context-check"));
        var engine = Engine(db, s, provider.Object);
        var draft = await engine.GenerateAsync(s.Chat, s.Admin, 1, "turkey-case:" + id, default);
        var local = AiSalesReplyPolicy.Create(context) != null;
        Assert.Equal(AiDraftStatus.ReviewRequired, draft.Status); Assert.True(draft.NeedsHuman);
        provider.Verify(p => p.GenerateAsync(It.IsAny<AiSalesContext>(), It.IsAny<CancellationToken>()), local ? Times.Never() : Times.Once());
        if (!local) Assert.True(context.Knowledge.Select(k => k.Id).ToHashSet().SetEquals(supplied!.Knowledge.Select(k => k.Id)));
        Assert.Equal(draft.Id, (await engine.GenerateAsync(s.Chat, s.Admin, 1, "turkey-case:" + id, default)).Id);
        Assert.False(await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == s.Chat && m.Direction == (byte)MessageDirection.Outgoing));
        Assert.False(await db.AiOfficeBookings.AnyAsync(b => b.ConversationId == s.Chat));
        Assert.False(await db.AiOutboundIntents.AnyAsync(i => i.ConversationId == s.Chat));
    }

    [Fact]
    public async Task Turkey_knowledge_is_account_scoped_and_retiring_owner_fact_invalidates_review()
    {
        var (s, entries) = await TurkeySeedAsync("المرتب كام؟"); var other = await SeedAsync();
        await using var db = fixture.Create(); var reader = new AiSalesContextService(db);
        var otherChat = (await db.WhatsAppConversations.FindAsync(other.Chat))!;
        // Same tenant lookup must still not expose this company's pack to another account.
        Assert.Empty((await reader.ReadAsync(s.Tenant, otherChat, default)).Knowledge);
        var provider = GoodProvider(entries["turkey_pay_and_work"].Id);
        var engine = Engine(db, s, provider.Object);
        var draft = await engine.GenerateAsync(s.Chat, s.Admin, 1, "turkey-retired-pay", default);
        await db.AiSalesKnowledge.Where(k => k.Id == entries["turkey_pay_and_work"].Id)
            .ExecuteUpdateAsync(u => u.SetProperty(k => k.Status, AiKnowledgeStatus.Retired));
        await Assert.ThrowsAsync<AiSalesConflict>(() => engine.ReviewAsync(draft.Id, s.Admin, true, default));
        Assert.Equal(AiDraftStatus.Stale, draft.Status);
    }

    [Fact]
    public void Published_owner_pack_records_approval_without_enabling_sends_or_becoming_synthetic_data()
    {
        using var pack = TurkeyDocument("turkey-owner-knowledge.json"); var root = pack.RootElement;
        Assert.Equal("published_to_paused_bakar_agent", root.GetProperty("publicationStatus").GetString());
        Assert.True(root.GetProperty("humanApproval").GetProperty("databasePublicationCompleted").GetBoolean());
        Assert.False(root.GetProperty("humanApproval").GetProperty("duplicateApprovalRequired").GetBoolean());
        foreach (var field in new[] { "tenantId", "whatsAppAccountId", "branchId", "reviewedById", "validFrom", "validUntil" })
            Assert.Equal(JsonValueKind.Null, root.GetProperty(field).ValueKind);
        Assert.False(root.GetProperty("liveSendingApproved").GetBoolean());
        Assert.Throws<InvalidOperationException>(() => SyntheticReplayHarness.Load(Path.Combine(Directory.GetCurrentDirectory(), "docs", "knowledge", "turkey-owner-knowledge.json")));
    }
}
