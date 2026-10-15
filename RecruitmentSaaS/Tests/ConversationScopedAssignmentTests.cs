using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    [Fact]
    public async Task Conversation_only_assignment_preserves_shared_lead_and_restores_cross_team_chat_owner()
    {
        var s = await SeedAsync(); await using var db = fixture.Create();
        var lead = (await db.Leads.FindAsync(s.Lead))!;
        lead.TeamManagerId = s.Admin; // Different from the account's general (null) team.
        (await db.Users.FindAsync(s.Human))!.ManagerId = s.Admin;
        var selected = (await db.WhatsAppConversations.FindAsync(s.Chat))!;
        var otherAccount = new WhatsAppAccount { Id = Guid.NewGuid(), Name = "Other account", PhoneNumberId = Guid.NewGuid().ToString(), WabaId = "test", DisplayPhoneNumber = "201999999999", IsActive = true };
        var other = new WhatsAppConversation { Id = Guid.NewGuid(), WhatsAppAccount = otherAccount,
            ContactId = selected.ContactId, LeadId = s.Lead, AssignedSalesAgentId = s.Human, IntakeStatus = (byte)IntakeStatus.Completed };
        db.Add(other); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AiSalesValidation>(() => Control(db, s).AssignAsync(s.Lead, s.Chat, s.Agent, s.Admin, 0, "Full lead", "shared-full", default));
        await Control(db, s).AssignAsync(s.Lead, s.Chat, s.Agent, null, 0, "Owner selected this conversation", "shared-chat", default,
            conversationOnly: true, sourceReference: "Owner's explicit chat request for this account/conversation");
        Assert.Equal(s.Human, lead.AssignedSalesId); Assert.Null(lead.AssignedAiSalesAgentId);
        Assert.Equal(s.Human, other.AssignedSalesAgentId); Assert.Null(other.AssignedAiSalesAgentId);
        Assert.Equal(s.Agent, selected.AssignedAiSalesAgentId);
        var assignment = await db.AiLeadAssignments.SingleAsync(a => a.ConversationId == s.Chat && a.EndedAt == null);
        Assert.True(assignment.ConversationOnly); Assert.Null(assignment.AssignedById); Assert.NotNull(assignment.SourceReference);
        await Control(db, s).ControlAsync(s.Chat, s.Admin, 1, "release", "Return selected conversation", "shared-release", default);
        Assert.Equal(s.Human, selected.AssignedSalesAgentId);
        Assert.Equal(s.Human, lead.AssignedSalesId); Assert.Equal(s.Admin, lead.TeamManagerId);
        Assert.Equal(s.Human, other.AssignedSalesAgentId);
    }

    [Fact]
    public async Task System_assignment_without_attribution_is_rejected()
    {
        var s = await SeedAsync(); await using var db = fixture.Create();
        await Assert.ThrowsAsync<AiSalesValidation>(() => Control(db, s).AssignAsync(s.Lead, s.Chat, s.Agent, null, 0,
            "Unattributed", "no-source", default, conversationOnly: true));
        Assert.Empty(await db.AiLeadAssignments.Where(a => a.ConversationId == s.Chat).ToListAsync());
    }

    [Fact]
    public async Task System_release_requires_and_records_owner_source_without_a_human_actor()
    {
        var s = await SeedAsync(); await using var db = fixture.Create();
        await Control(db, s).AssignAsync(s.Lead, s.Chat, s.Agent, s.Admin, 0, "Original pilot", "system-release-seed", default);
        await Assert.ThrowsAsync<AiSalesValidation>(() => Control(db, s).ControlAsync(s.Chat, null, 1, "release", "No attribution", "invalid-system-release", default));
        await Control(db, s).ControlAsync(s.Chat, null, 1, "release", "Owner selected a different test conversation", "owner-system-release", default,
            sourceReference: "Owner's explicit replacement selection");
        Assert.Equal(s.Human, (await db.Leads.FindAsync(s.Lead))!.AssignedSalesId);
        var audit = await db.AiControlAudits.SingleAsync(a => a.ConversationId == s.Chat && a.Event == "ReleasedToHumanWorkflow");
        Assert.Null(audit.ActorId); Assert.Contains("Owner", audit.Reason);
    }
}
