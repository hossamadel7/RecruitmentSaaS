using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    private async Task<(Seed Seed, Guid Account, AiSalesDraft Draft)> ImprovementSeedAsync()
    {
        var s = await SeedAsync(); var fact = await EngineSeedAsync(s); await using var db = fixture.Create();
        var engine = Engine(db, s, GoodProvider(fact.Id).Object);
        var draft = await engine.GenerateAsync(s.Chat, s.Admin, 1, "improvement-source:" + s.Chat, default);
        await engine.ReviewAsync(draft.Id, s.Admin, false, default);
        return (s, fact.WhatsAppAccountId, draft);
    }
    private static AiImprovementRequest ImprovementRequest(AiSalesDraft d) => new(d.Id, AiSalesImprovementService.Fingerprint(d),
        "Clarify office location", "Where is the office?", "Answer the location first, then ask whether the customer wants to visit.", "Original draft was too vague.", true);

    [Fact]
    public async Task Improvement_creates_only_a_pending_playbook_idempotently_without_changing_customer_or_current_facts()
    {
        var (s, account, draft) = await ImprovementSeedAsync(); await using var db = fixture.Create();
        var service = new AiSalesImprovementService(db, Control(db, s));
        var observed = Assert.Single(await service.ListAsync(account, default));
        Assert.Equal("Unknown", observed.OutcomeEvidence); Assert.Equal(0, observed.VerifiedVisits);
        var request = ImprovementRequest(draft);
        var item = await service.ProposeAsync(request, s.Admin, "improvement-one", default);
        var replay = await service.ProposeAsync(request, s.Admin, "improvement-one", default);
        Assert.Equal(item.Id, replay.Id); Assert.Equal(AiKnowledgeKind.Playbook, item.Kind); Assert.Equal(AiKnowledgeStatus.Draft, item.Status);
        Assert.Null(item.ApprovedById); Assert.Contains(draft.Id.ToString(), item.SourceReference);
        Assert.Single(await db.AuditLogs.Where(a => a.EntityId == item.Id && a.EventType == "AiImprovementProposed").ToListAsync());
        var context = await new AiSalesContextService(db).ReadAsync(s.Tenant, (await db.WhatsAppConversations.FindAsync(s.Chat))!, default);
        Assert.DoesNotContain(context.Knowledge, k => k.Id == item.Id);
        Assert.False(await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == s.Chat && m.Direction == (byte)MessageDirection.Outgoing));
        Assert.Null((await db.Leads.FindAsync(s.Lead))!.AppointmentDate);
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.ProposeAsync(request with { ProposedApproach = "Different reply" }, s.Admin, "improvement-one", default));
    }

    [Fact]
    public async Task Improvement_rejects_unreviewed_privacy_changed_sources_and_foreign_tenants()
    {
        var (s, account, draft) = await ImprovementSeedAsync(); var other = await SeedAsync(); await using var db = fixture.Create();
        var service = new AiSalesImprovementService(db, Control(db, s)); var request = ImprovementRequest(draft);
        await Assert.ThrowsAsync<AiSalesValidation>(() => service.ProposeAsync(request with { PrivacyReviewed = false }, s.Admin, "privacy", default));
        await Assert.ThrowsAsync<AiSalesValidation>(() => service.ProposeAsync(request with { ExpectedSourceFingerprint = "bad" }, s.Admin, "bad-digest", default));
        await db.AiSalesDrafts.Where(d => d.Id == draft.Id).ExecuteUpdateAsync(x => x.SetProperty(d => d.Reply, "Source changed"));
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.ProposeAsync(request, s.Admin, "changed", default));
        var foreign = new AiSalesImprovementService(db, Control(db, other));
        Assert.Empty(await foreign.ListAsync(account, default));
        await Assert.ThrowsAsync<AiSalesValidation>(() => foreign.ProposeAsync(request, other.Admin, "foreign", default));
        Assert.False(await db.AiSalesKnowledge.AnyAsync(k => k.TenantId == s.Tenant && k.Kind == AiKnowledgeKind.Playbook));
    }

    [Fact]
    public async Task Improvement_outcomes_require_real_matching_visit_evidence_and_never_infer_no_show()
    {
        var s = await SeedAsync(); var office = await OfficeSeedAsync(s); await using var db = fixture.Create();
        var fact = await db.AiSalesKnowledge.SingleAsync(k => k.TenantId == s.Tenant); var engine = Engine(db, s, GoodProvider(fact.Id).Object);
        var draft = await engine.GenerateAsync(s.Chat, s.Admin, 1, "outcome-source", default); await engine.ReviewAsync(draft.Id, s.Admin, false, default);
        var service = new AiSalesImprovementService(db, Control(db, s));
        var booking = await Office(db, s).SaveAsync(s.Chat, null, s.Admin, Request(office), "improvement-booking", default);
        Assert.Equal("BookedAttendanceUnknown", Assert.Single(await service.ListAsync(fact.WhatsAppAccountId, default)).OutcomeEvidence);
        draft.CreatedAt = DateTime.UtcNow.AddHours(-1); booking.CreatedAt = DateTime.UtcNow.AddMinutes(-30);
        booking.ArrivalLocal = EgyptTime.FromUtc(DateTime.UtcNow.AddMinutes(-15));
        var visit = new LeadVisit { Id = Guid.NewGuid(), LeadId = s.Lead, BranchId = office.Policy.BranchId, ReceptionUserId = s.Admin,
            MeetingOutcome = 1, CreatedAt = DateTime.UtcNow, VisitDateTime = DateTime.UtcNow.AddMinutes(-15) };
        db.LeadVisits.Add(visit); booking.VerifiedVisitId = visit.Id; booking.Status = AiOfficeBookingStatus.Visited; await db.SaveChangesAsync();
        Assert.Equal("VerifiedVisit", Assert.Single(await service.ListAsync(fact.WhatsAppAccountId, default)).OutcomeEvidence);
        visit.VisitDateTime = DateTime.UtcNow.AddDays(2); await db.SaveChangesAsync();
        Assert.Equal("BookedAttendanceUnknown", Assert.Single(await service.ListAsync(fact.WhatsAppAccountId, default)).OutcomeEvidence);
        booking.VerifiedVisitId = null; booking.Status = AiOfficeBookingStatus.Cancelled; await db.SaveChangesAsync();
        Assert.Equal("CancelledAttendanceUnknown", Assert.Single(await service.ListAsync(fact.WhatsAppAccountId, default)).OutcomeEvidence);
    }

    [Fact]
    public async Task Improvement_http_endpoints_require_admin_csrf_and_scoped_sources()
    {
        var (s, account, draft) = await ImprovementSeedAsync(); await using var app = await StartHttpAsync(s); using var client = app.GetTestClient();
        await Authenticate(client, s.Human, "8");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/ai-sales-engine/improvements?accountId={account}")).StatusCode);
        await Authenticate(client, s.Admin, "1");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/ai-sales-engine/improvements?accountId={account}")).StatusCode);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "http-improvement");
        var token = client.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single(); client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/ai-sales-engine/improvements", ImprovementRequest(draft))).StatusCode);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/ai-sales-engine/improvements", ImprovementRequest(draft))).StatusCode);
    }
}
