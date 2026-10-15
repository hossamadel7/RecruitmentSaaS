using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    private const string CorpusKey = "synthetic-pseudonym-key-not-for-production-123";
    private static AiChatLearningService Corpus(RecruitmentSaaS.Data.RecruitmentCrmContext db, Seed s, Guid account, bool enabled = true) =>
        new(db, Control(db, s), Options.Create(new AiChatLearningOptions { Enabled = enabled, ApprovedAccountIds = [account], PseudonymKey = CorpusKey }));
    private async Task<(Seed Seed, Guid Account)> CorpusSeedAsync(int count = 5)
    {
        var s = await SeedAsync(); await using var db = fixture.Create();
        var account = await db.WhatsAppConversations.Where(c => c.Id == s.Chat).Select(c => c.WhatsAppAccountId).SingleAsync();
        for (var n = 0; n < count; n++) db.WhatsAppMessages.Add(new() { Id = Guid.NewGuid(), ConversationId = s.Chat, WhatsAppAccountId = account,
            Direction = 1, MessageType = 1, MessageSource = 3, Status = 1, TextBody = "Synthetic customer: 01012345678 / abc@example.test / https://example.test/customer/123", CreatedAt = DateTime.UtcNow.AddDays(-n-1), WhatsAppTimestamp = DateTime.UtcNow.AddDays(-n-1) });
        await db.SaveChangesAsync(); return (s, account);
    }
    [Fact]
    public async Task Chat_corpus_pagination_covers_full_history_without_other_accounts_or_external_ready_claim()
    {
        var (s, account) = await CorpusSeedAsync(7); var other = await CorpusSeedAsync(3);
        await using var db = fixture.Create(); var service = Corpus(db, s, account); var refs = new HashSet<Guid>();
        Guid? cursor = null; DateTime? boundary = null; string? token = null;
        do
        {
            var page = await service.PrepareAsync(account, cursor, boundary, 2, s.Admin, default);
            boundary = page.SnapshotBefore; Assert.False(page.TrainingReady); Assert.Equal("UnreviewedLocalPreparationOnly", page.ReviewStatus);
            foreach (var item in page.Items)
            {
                Assert.True(refs.Add(item.InternalMessageReference)); token ??= item.ConversationToken; Assert.Equal(token, item.ConversationToken);
                Assert.Contains("ManualPrivacyReviewRequired", item.ReviewFlags); Assert.True(item.CandidateForReview);
                Assert.DoesNotContain("01012345678", item.ReviewText); Assert.DoesNotContain("abc@example.test", item.ReviewText);
                Assert.DoesNotContain("https://", item.ReviewText); Assert.DoesNotContain("Synthetic customer", item.ReviewText);
            }
            cursor = page.NextAfterMessageId; if (!page.HasMore) break;
        } while (true);
        Assert.Equal(7, refs.Count);
        Assert.Empty(await db.WhatsAppMessages.Where(m => m.WhatsAppAccountId == other.Account && refs.Contains(m.Id)).ToListAsync());
        Assert.Equal(7, await db.WhatsAppMessages.CountAsync(m => m.WhatsAppAccountId == account && m.TextBody!.Contains("01012345678")));
        Assert.Empty(await db.AiSalesKnowledge.Where(k => k.TenantId == s.Tenant).ToListAsync()); // no automatic publication
        Assert.Equal(4, await db.AuditLogs.CountAsync(a => a.EntityId == account && a.EventType == "AiChatCorpusPrepared"));
    }
    [Fact]
    public async Task Chat_corpus_marks_system_failed_staff_and_uninterpreted_media_without_downloading()
    {
        var (s, account) = await CorpusSeedAsync(0); await using var db = fixture.Create();
        WhatsAppMessage Message(byte type, byte direction, byte source, byte status) => new() { Id = Guid.NewGuid(), ConversationId = s.Chat, WhatsAppAccountId = account,
            Direction = direction, MessageType = type, MessageSource = source, Status = status, TextBody = "Private media caption", MediaId = "private-provider-id", MediaUrl = "https://secret.test/file", CreatedAt = DateTime.UtcNow.AddDays(-1), WhatsAppTimestamp = DateTime.UtcNow.AddDays(-1) };
        var ai = Message(1, 2, 4, 3); var failed = Message(1, 2, 1, 6); failed.SenderUserId = s.Human;
        var audio = Message(4, 1, 3, 1); var photo = Message(2, 1, 3, 1); db.AddRange(ai, failed, audio, photo); await db.SaveChangesAsync();
        var page = await Corpus(db, s, account).PrepareAsync(account, null, null, 10, s.Admin, default);
        Assert.All(page.Items, item => Assert.False(item.CandidateForReview));
        Assert.Contains("DoNotUseAsHumanTrainingTarget", page.Items.Single(i => i.InternalMessageReference == ai.Id).ReviewFlags);
        Assert.Contains("UnconfirmedOrFailedStaffSend", page.Items.Single(i => i.InternalMessageReference == failed.Id).ReviewFlags);
        Assert.Null(page.Items.Single(i => i.InternalMessageReference == audio.Id).ReviewText);
        Assert.Contains("AudioNeedsTranscriptionAndReview", page.Items.Single(i => i.InternalMessageReference == audio.Id).ReviewFlags);
        var json = JsonSerializer.Serialize(page); Assert.DoesNotContain("private-provider-id", json); Assert.DoesNotContain("https://secret", json);
    }
    [Fact]
    public async Task Chat_corpus_includes_closed_unassigned_historical_chats_in_the_selected_account()
    {
        var (s, account) = await CorpusSeedAsync(1); await using var db = fixture.Create();
        var oldContact = new WhatsAppContact { Id = Guid.NewGuid(), Name = "Other synthetic customer", WhatsAppPhoneNumber = Guid.NewGuid().ToString("N") };
        var oldChat = new WhatsAppConversation { Id = Guid.NewGuid(), Contact = oldContact, WhatsAppAccountId = account,
            Status = (byte)ConversationStatus.Closed, CreatedAt = new DateTime(2019, 1, 1), OpenedAt = new DateTime(2019, 1, 1) };
        var old = new WhatsAppMessage { Id = Guid.NewGuid(), Conversation = oldChat, WhatsAppAccountId = account, Direction = 2, MessageType = 1,
            MessageSource = (byte)MessageSource.WhatsAppBusinessApp, Status = (byte)MessageStatus.Sent, TextBody = "Old sales example, not a current vacancy",
            CreatedAt = new DateTime(2019, 1, 2), WhatsAppTimestamp = new DateTime(2019, 1, 2) };
        db.Add(old); await db.SaveChangesAsync();
        var page = await Corpus(db, s, account).PrepareAsync(account, null, null, 10, s.Admin, default);
        var item = page.Items.Single(i => i.InternalMessageReference == old.Id); Assert.True(item.CandidateForReview);
        Assert.Equal("staff_or_device", item.Speaker); Assert.Contains("HumanAuthorshipNeedsReview", item.ReviewFlags);
        Assert.Contains("HistoricalClaimsNotCurrentFacts", item.ReviewFlags); Assert.Equal(2, page.Items.Select(i => i.ConversationToken).Distinct().Count());
    }
    [Fact]
    public async Task Chat_corpus_disabled_and_unselected_account_fail_closed_and_continuation_needs_boundary()
    {
        var (s, account) = await CorpusSeedAsync(); await using var db = fixture.Create();
        await Assert.ThrowsAsync<AiSalesValidation>(() => Corpus(db, s, account, false).PrepareAsync(account, null, null, 10, s.Admin, default));
        await Assert.ThrowsAsync<AiSalesValidation>(() => Corpus(db, s, Guid.NewGuid()).PrepareAsync(account, null, null, 10, s.Admin, default));
        await Assert.ThrowsAsync<AiSalesValidation>(() => Corpus(db, s, account).PrepareAsync(account, Guid.NewGuid(), null, 10, s.Admin, default));
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityId == account && a.EventType == "AiChatCorpusPrepared").ToListAsync());
    }
    [Theory]
    [InlineData("محمد أحمد ٠١٠١٢٣٤٥٦٧٨", "محمد أحمد")]
    [InlineData("passport 12345678901234", "NoName")]
    [InlineData("customer abc@example.test", "NoName")]
    [InlineData("https://example.test/private", "NoName")]
    public void Chat_corpus_masks_common_identifiers_but_does_not_claim_complete_anonymization(string text, string name)
    {
        var masked = AiChatLearningService.Mask(text, [name]); Assert.NotEqual(text, masked);
        if (name == "محمد أحمد") { Assert.DoesNotContain("محمد", masked); Assert.DoesNotContain("أحمد", masked); Assert.DoesNotContain("٠١٠", masked); }
    }
    [Fact]
    public async Task Chat_corpus_api_requires_admin_and_csrf_and_never_returns_masking_key()
    {
        var (s, account) = await CorpusSeedAsync(); await using var app = await StartHttpAsync(s); using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ai-chat-learning/status")).StatusCode);
        await Authenticate(client, s.Admin, "7"); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai-chat-learning/status")).StatusCode);
        await Authenticate(client, s.Admin, "1");
        var status = await client.GetStringAsync("/api/ai-chat-learning/status"); Assert.DoesNotContain("pseudonymKey", status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/ai-chat-learning/prepare", new { accountId = account })).StatusCode);
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/ai-chat-learning/prepare", new { accountId = account })).StatusCode);
    }
}
