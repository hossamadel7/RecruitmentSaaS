using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

public class TeleSalesTeamMembershipTests
{
    private static RecruitmentCrmContext Create() => new(new DbContextOptionsBuilder<RecruitmentCrmContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=model_only;Username=model_only").Options);

    [Fact]
    public void Membership_has_unique_user_manager_key_and_two_user_foreign_keys()
    {
        using var db = Create();
        var entity = db.Model.FindEntityType(typeof(TeleSalesTeamMembership))!;
        Assert.Equal(new[] { "UserId", "ManagerId" }, entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(2, entity.GetForeignKeys().Count());
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(typeof(User), fk.PrincipalEntityType.ClrType));
    }

    [Fact]
    public void Shared_person_is_eligible_for_both_teams_without_duplicate_candidates()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var user = new User { ManagerId = a, TeamMemberships = new List<TeleSalesTeamMembership> { new() { ManagerId = b } } };
        var users = new[] { user }.AsQueryable();
        foreach (var team in new[] { a, b })
            Assert.Single(users.Where(u => u.ManagerId == team || u.TeamMemberships.Any(m => m.ManagerId == team)));
        Assert.Empty(users.Where(u => u.ManagerId == Guid.Empty || u.TeamMemberships.Any(m => m.ManagerId == Guid.Empty)));
        using var db = Create();
        var sql = db.Users.Where(u => u.ManagerId == b || u.TeamMemberships.Any(m => m.ManagerId == b)).ToQueryString();
        Assert.Contains("TeleSalesTeamMemberships", sql);
        Assert.Contains("EXISTS", sql);
    }

    [Fact]
    public void Shared_staff_chat_visibility_requires_the_managers_team_account()
    {
        var staff = Guid.NewGuid(); var accountA = Guid.NewGuid(); var accountB = Guid.NewGuid();
        var visibility = new WhatsAppVisibility(new() { staff }, Guid.NewGuid(), teamAccountIds: new() { accountA })
            { SharedAgents = new() { staff } };
        Assert.True(WhatsAppScope.CanSee(visibility, staff, null, accountA));
        Assert.False(WhatsAppScope.CanSee(visibility, staff, null, accountB));
        Assert.True(WhatsAppScope.CanSee(visibility, staff, null, accountB, leadTeamManagerId: visibility.TeamLeaderId));
        Assert.False(WhatsAppScope.CanSee(visibility, staff, null, accountA, leadTeamManagerId: Guid.NewGuid()));
        var chats = new[] {
            new WhatsAppConversation { AssignedSalesAgentId = staff, WhatsAppAccountId = accountA },
            new WhatsAppConversation { AssignedSalesAgentId = staff, WhatsAppAccountId = accountB }
        }.AsQueryable();
        Assert.Equal(accountA, Assert.Single(chats.ApplyVisibility(visibility)).WhatsAppAccountId);
        var self = new WhatsAppVisibility(new() { staff }, null);
        Assert.Equal(2, chats.ApplyVisibility(self).Count());
        using var db = Create();
        Assert.Contains("WhatsAppAccountId", db.WhatsAppConversations.ApplyVisibility(visibility).ToQueryString());
    }
}
