using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;
namespace RecruitmentSaaS.Tests;

/// <summary>Regression tests for defects found in the AI Sales Agent review (see AI_SALES_AGENT_REVIEW.md).</summary>
public sealed partial class FoundationTests
{
    [Fact]
    public async Task Natural_admin_reply_never_answers_a_question_the_admin_was_not_sent()
    {
        var (s, account, channel, options) = await AdminSeedAsync(); await using var db = fixture.Create(); var service = AdminService(db, s, options);
        var r = (await service.AskAsync(s.Chat, null, s.Admin, default))!;
        Assert.Equal(AiAdminRequestState.Queued, r.State);
        // The only open question is still queued (never delivered): an unrelated Admin message must not become its answer.
        await service.ReceiveAsync(account, AdminInbound("201099900011", "صباح الخير"), default);
        Assert.Equal(AiAdminRequestState.Queued, r.State); Assert.Equal("", r.Answer);
        // Moved to AwaitingAdmin but the transport outcome is unknown: still not provably seen.
        await service.QueueQuestionsAsync(channel, default);
        var question = await db.AiAdminMessages.SingleAsync(m => m.OperationKey == "question:" + r.Id);
        question.State = "Unknown"; await db.SaveChangesAsync();
        await service.ReceiveAsync(account, AdminInbound("201099900011", "تمام"), default);
        Assert.Equal(AiAdminRequestState.AwaitingAdmin, r.State);
        // Once delivered, a natural reply with exactly one open question is accepted.
        question.State = "Sent"; question.WhatsAppMessageId = "wamid.review-sent"; await db.SaveChangesAsync();
        await service.ReceiveAsync(account, AdminInbound("201099900011", "نعم متاح"), default);
        Assert.Equal(AiAdminRequestState.AnswerReceived, r.State); Assert.Equal("نعم متاح", r.Answer);
    }

    [Fact]
    public async Task Answer_received_before_deadline_can_still_be_validated_after_deadline_escalation()
    {
        var (s, account, channel, options) = await AdminSeedAsync(); await using var db = fixture.Create(); var service = AdminService(db, s, options);
        var r = (await service.AskAsync(s.Chat, null, s.Admin, default))!;
        await service.ReceiveAsync(account, AdminInbound("201099900011", r.Code + " متاح للعميل ده"), default);
        Assert.Equal(AiAdminRequestState.AnswerReceived, r.State);
        r.Deadline = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        var resumer = new AiAdminDraftResumer(db, service, Engine(db, s, new Mock<IAiSalesDraftProvider>(MockBehavior.Strict).Object));
        await resumer.PrepareAsync(r.Id, default);
        Assert.Equal(AiAdminRequestState.Escalated, r.State);
        await service.ValidateAnswerAsync(r.Id, s.Admin, r.Version, "متاح لهذا العميل فقط", AiAdminAnswerScope.CustomerOnly, DateTime.UtcNow.AddDays(1), false, "", null, default);
        Assert.Equal(AiAdminRequestState.Validated, r.State); Assert.Null(r.ErrorCode);
        // Escalations for any other reason are not reopened.
        r.State = AiAdminRequestState.Escalated; r.ErrorCode = "DraftUnavailable"; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.ValidateAnswerAsync(r.Id, s.Admin, r.Version, "x", AiAdminAnswerScope.CustomerOnly, DateTime.UtcNow.AddDays(1), false, "", null, default));
    }

    [Fact]
    public async Task Resumer_escalates_permanently_refused_drafts_instead_of_retrying_forever()
    {
        var (s, _, _, options) = await AdminSeedAsync(); await using var db = fixture.Create(); var service = AdminService(db, s, options);
        var r = (await service.AskAsync(s.Chat, null, s.Admin, default))!;
        await service.ValidateAnswerAsync(r.Id, s.Admin, r.Version, "إجابة", AiAdminAnswerScope.CustomerOnly, DateTime.UtcNow.AddDays(1), false, "", null, default);
        var disabled = new AiSalesDraftService(db, Control(db, s), new(db), new Mock<IAiSalesDraftProvider>(MockBehavior.Strict).Object,
            Options.Create(new AiSalesEngineOptions { DraftsEnabled = false }));
        var result = await new AiAdminDraftResumer(db, service, disabled).PrepareAsync(r.Id, default);
        Assert.Equal(AiAdminRequestState.Escalated, result.State); Assert.Equal("DraftUnavailable", result.ErrorCode);
        Assert.Equal(AiAdminRequestState.Escalated, (await db.AiAdminClarifications.AsNoTracking().SingleAsync(x => x.Id == r.Id)).State);
    }

    [Fact]
    public async Task Automatic_reply_candidates_skip_already_handled_turns_but_return_on_a_new_customer_message()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); await using var db = fixture.Create();
        var settings = new AiSalesEngineOptions { AutoReplyEnabled = true, AutoReplyAnyAssignedConversation = true,
            AutoReplyAgentIds = [s.Agent], AutoReplySinceUtc = DateTime.UtcNow.AddMinutes(-5) };
        Assert.Contains(s.Chat, await AiAutomaticReplyWorker.CandidatesAsync(db, settings, s.Tenant, default));
        var latest = await db.WhatsAppMessages.Where(m => m.ConversationId == s.Chat && m.Direction == 1).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).FirstAsync();
        var version = await db.ConversationControls.Where(c => c.ConversationId == s.Chat).Select(c => c.OwnershipVersion).SingleAsync();
        db.AiSalesDrafts.Add(new() { TenantId = s.Tenant, ConversationId = s.Chat, AgentId = s.Agent, RequestKey = $"auto:{s.Chat}:{latest.Id}:{version}",
            OwnershipVersion = version, ContextFingerprint = "x", PromptVersion = "x", KnowledgeSnapshotJson = "[]", Status = AiDraftStatus.ReviewRequired, NeedsHuman = true, Intent = "handoff" });
        await db.SaveChangesAsync();
        Assert.DoesNotContain(s.Chat, await AiAutomaticReplyWorker.CandidatesAsync(db, settings, s.Tenant, default));
        db.WhatsAppMessages.Add(new() { Id = Guid.NewGuid(), ConversationId = s.Chat, WhatsAppAccountId = latest.WhatsAppAccountId, Direction = 1, MessageType = 1,
            TextBody = "طيب؟", CreatedAt = DateTime.UtcNow.AddSeconds(1), WhatsAppTimestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Assert.Contains(s.Chat, await AiAutomaticReplyWorker.CandidatesAsync(db, settings, s.Tenant, default));
    }
}
