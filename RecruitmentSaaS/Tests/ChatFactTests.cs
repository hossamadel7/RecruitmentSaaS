using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;
public sealed partial class FoundationTests
{
    private static AiChatFactService ChatFacts(RecruitmentSaaS.Data.RecruitmentCrmContext db, Seed s, Guid account) =>
        new(db, Control(db, s), Corpus(db, s, account), Options.Create(new AiChatLearningOptions { Enabled = true, ApprovedAccountIds = [account], PseudonymKey = CorpusKey }));
    private async Task<(Seed Seed, Guid Account, Guid Source)> ChatFactSeedAsync()
    {
        var (s, account) = await CorpusSeedAsync(1); await using var db = fixture.Create();
        var source = await db.WhatsAppMessages.SingleAsync(m => m.ConversationId == s.Chat);
        source.TextBody = "المرتب 900 دولار، محمد أحمد 01012345678"; await db.SaveChangesAsync(); return (s, account, source.Id);
    }
    private static AiChatFactDecision Confirm(AiChatFactCandidate c, AiChatFactResolution[]? related = null, bool privacy = true) =>
        new(c.Version, true, "الراتب الحالي", "الراتب من 650 إلى 850 دولار أمريكي شهريًا بعد الخصومات.", privacy, DateTime.UtcNow.AddDays(7), related ?? []);
    [Fact]
    public async Task Chat_fact_extraction_is_deduplicated_and_asks_owner_without_automatically_publishing()
    {
        var (s, account, source) = await ChatFactSeedAsync(); await using var db = fixture.Create(); var service = ChatFacts(db, s, account);
        Assert.Equal(1, (await service.ExtractAsync(account, null, null, 100, s.Admin, default)).AddedCandidates);
        Assert.Equal(0, (await service.ExtractAsync(account, null, null, 100, s.Admin, default)).AddedCandidates);
        var question = (await service.NextAsync(account, default))!; Assert.True(question.SourceUnchanged);
        Assert.Equal(source, question.Candidate.SourceMessageId); Assert.Equal("salary", question.Candidate.Topic);
        Assert.Contains("900", question.Candidate.SourceStatement); Assert.DoesNotContain("01012345678", question.Candidate.SourceStatement);
        Assert.Empty(await db.AiSalesKnowledge.Where(k => k.TenantId == s.Tenant).ToListAsync());
    }
    [Fact]
    public async Task Owner_corrected_fact_is_published_with_provenance_and_readable_by_existing_context()
    {
        var (s, account, _) = await ChatFactSeedAsync(); await using var db = fixture.Create(); var service = ChatFacts(db, s, account);
        await service.ExtractAsync(account, null, null, 100, s.Admin, default); var c = (await service.NextAsync(account, default))!.Candidate;
        var result = await service.DecideAsync(c.Id, s.Admin, Confirm(c), default); Assert.Equal(AiChatFactState.Published, result.State);
        var knowledge = await db.AiSalesKnowledge.SingleAsync(k => k.Id == result.KnowledgeId);
        Assert.Equal(AiKnowledgeStatus.Approved, knowledge.Status); Assert.Equal(s.Admin, knowledge.ApprovedById);
        Assert.Contains("650 إلى 850", knowledge.Content); Assert.DoesNotContain("900", knowledge.Content); Assert.Contains(c.Id.ToString(), knowledge.SourceReference);
        var chat = await db.WhatsAppConversations.SingleAsync(ch => ch.Id == s.Chat);
        Assert.Contains((await new AiSalesContextService(db).ReadAsync(s.Tenant, chat, default)).Knowledge, k => k.Id == knowledge.Id);
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.DecideAsync(c.Id, s.Admin, Confirm(c), default));
        Assert.Single(await db.AiSalesKnowledge.Where(k => k.TenantId == s.Tenant).ToListAsync());
    }
    [Fact]
    public async Task Related_fact_requires_explicit_versioned_resolution_and_retirement_is_atomic()
    {
        var (s, account, _) = await ChatFactSeedAsync(); await using var db = fixture.Create();
        var old = new AiSalesKnowledge { TenantId = s.Tenant, WhatsAppAccountId = account, Kind = AiKnowledgeKind.Fact, Status = AiKnowledgeStatus.Approved,
            Title = "الراتب القديم", Content = "الراتب 900 دولار", SourceReference = "Synthetic old statement", CreatedById = s.Admin, ApprovedById = s.Admin,
            ApprovedAt = DateTime.UtcNow, ValidFrom = DateTime.UtcNow.AddDays(-1), ValidUntil = DateTime.UtcNow.AddDays(1) };
        db.Add(old); await db.SaveChangesAsync(); var service = ChatFacts(db, s, account); await service.ExtractAsync(account, null, null, 100, s.Admin, default);
        var q = (await service.NextAsync(account, default))!; Assert.Single(q.RelatedFacts);
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.DecideAsync(q.Candidate.Id, s.Admin, Confirm(q.Candidate), default));
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.DecideAsync(q.Candidate.Id, s.Admin, Confirm(q.Candidate, [new(old.Id, 999, true)]), default));
        await service.DecideAsync(q.Candidate.Id, s.Admin, Confirm(q.Candidate, [new(old.Id, old.Version, true)]), default);
        Assert.Equal(AiKnowledgeStatus.Retired, old.Status);
        Assert.Single(await db.AiSalesKnowledge.Where(k => k.TenantId == s.Tenant && k.Status == AiKnowledgeStatus.Approved).ToListAsync());
    }
    [Fact]
    public async Task Source_edit_blocks_publication_and_new_source_version_can_be_extracted()
    {
        var (s, account, source) = await ChatFactSeedAsync(); await using var db = fixture.Create(); var service = ChatFacts(db, s, account);
        await service.ExtractAsync(account, null, null, 100, s.Admin, default); var c = (await service.NextAsync(account, default))!.Candidate;
        await db.WhatsAppMessages.Where(m => m.Id == source).ExecuteUpdateAsync(u => u.SetProperty(m => m.TextBody, "المرتب 700 دولار"));
        Assert.False((await service.NextAsync(account, default))!.SourceUnchanged);
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.DecideAsync(c.Id, s.Admin, Confirm(c), default));
        Assert.Equal(1, (await service.ExtractAsync(account, null, null, 100, s.Admin, default)).AddedCandidates);
        await service.DecideAsync(c.Id, s.Admin, new(c.Version, false, null, null, false, DateTime.UtcNow, []), default);
        Assert.Contains("700", (await service.NextAsync(account, default))!.Candidate.SourceStatement);
    }
    [Fact]
    public async Task Deleted_source_and_missing_privacy_review_fail_closed_but_rejection_remains_possible()
    {
        var (s, account, source) = await ChatFactSeedAsync(); await using var db = fixture.Create(); var service = ChatFacts(db, s, account);
        await service.ExtractAsync(account, null, null, 100, s.Admin, default); var c = (await service.NextAsync(account, default))!.Candidate;
        await Assert.ThrowsAsync<AiSalesValidation>(() => service.DecideAsync(c.Id, s.Admin, Confirm(c, privacy: false), default));
        await db.WhatsAppMessages.Where(m => m.Id == source).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.DecideAsync(c.Id, s.Admin, Confirm(c), default));
        var result = await service.DecideAsync(c.Id, s.Admin, new(c.Version, false, null, null, false, DateTime.UtcNow, []), default);
        Assert.Equal(AiChatFactState.Rejected, result.State); Assert.Empty(await db.AiSalesKnowledge.Where(k => k.TenantId == s.Tenant).ToListAsync());
    }
    [Fact]
    public async Task Foreign_tenant_cannot_review_candidate_and_unconfirmed_system_messages_make_no_candidates()
    {
        var (s, account, source) = await ChatFactSeedAsync(); var other = await SeedAsync(); await using var db = fixture.Create(); var service = ChatFacts(db, s, account);
        await service.ExtractAsync(account, null, null, 100, s.Admin, default); var c = (await service.NextAsync(account, default))!.Candidate;
        await Assert.ThrowsAsync<AiSalesValidation>(() => ChatFacts(db, other, account).DecideAsync(c.Id, other.Admin, Confirm(c), default));
        await db.WhatsAppMessages.Where(m => m.Id == source).ExecuteUpdateAsync(u => u.SetProperty(m => m.Direction, (byte)2).SetProperty(m => m.MessageSource, (byte)4));
        Assert.Equal(0, (await service.ExtractAsync(account, null, null, 100, s.Admin, default)).AddedCandidates);
    }
    [Fact]
    public async Task Previously_reviewed_topic_is_found_even_when_owner_uses_different_wording()
    {
        var (s, account, source) = await ChatFactSeedAsync(); await using var db = fixture.Create(); var service = ChatFacts(db, s, account);
        await service.ExtractAsync(account, null, null, 100, s.Admin, default); var c = (await service.NextAsync(account, default))!.Candidate;
        await service.DecideAsync(c.Id, s.Admin, Confirm(c) with { ConfirmedTitle = "التعويض المالي", ConfirmedContent = "المبلغ النهائي 800 شهريًا وفق العرض التجريبي." }, default);
        await db.WhatsAppMessages.Where(m => m.Id == source).ExecuteUpdateAsync(u => u.SetProperty(m => m.TextBody, "المرتب 1000 دولار"));
        await service.ExtractAsync(account, null, null, 100, s.Admin, default);
        var next = (await service.NextAsync(account, default))!; Assert.Single(next.RelatedFacts);
        Assert.Equal("التعويض المالي", next.RelatedFacts[0].Title);
    }
    [Fact]
    public async Task Delivery_receipts_do_not_create_duplicate_fact_questions()
    {
        var (s, account, source) = await ChatFactSeedAsync(); await using var db = fixture.Create();
        await db.WhatsAppMessages.Where(m => m.Id == source).ExecuteUpdateAsync(u => u.SetProperty(m => m.Direction, (byte)2)
            .SetProperty(m => m.Status, (byte)MessageStatus.Sent).SetProperty(m => m.MessageSource, (byte)MessageSource.CloudApi).SetProperty(m => m.SenderUserId, (Guid?)s.Human));
        var service = ChatFacts(db, s, account); Assert.Equal(1, (await service.ExtractAsync(account, null, null, 100, s.Admin, default)).AddedCandidates);
        await db.WhatsAppMessages.Where(m => m.Id == source).ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, (byte)MessageStatus.Read));
        Assert.True((await service.NextAsync(account, default))!.SourceUnchanged);
        Assert.Equal(0, (await service.ExtractAsync(account, null, null, 100, s.Admin, default)).AddedCandidates);
    }
}
