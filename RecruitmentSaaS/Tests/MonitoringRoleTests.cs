using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RecruitmentSaaS.Migrations;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    [Fact]
    public async Task Monitoring_role_reads_all_teams_chats_and_media_metadata_but_cannot_modify_them()
    {
        var s = await SeedAsync(); var other = await SeedAsync();
        Guid monitorId;
        await using (var db = fixture.Create())
        {
            var human = (await db.Users.FindAsync(s.Human))!;
            var monitor = new User { Id = Guid.NewGuid(), BranchId = human.BranchId, FullName = "Monitoring",
                Email = Guid.NewGuid() + "@example.test", PasswordHash = "unused", Role = 9, IsActive = true };
            db.Users.Add(monitor); monitorId = monitor.Id;
            var leader = new User { Id = Guid.NewGuid(), BranchId = human.BranchId, FullName = "Other team",
                Email = Guid.NewGuid() + "@example.test", PasswordHash = "unused", Role = 7, IsActive = true };
            db.Users.Add(leader);
            (await db.Users.FindAsync(other.Human))!.ManagerId = leader.Id;
            var waiting = (await db.WhatsAppConversations.FindAsync(other.Chat))!;
            waiting.AssignedSalesAgentId = null; waiting.PendingTeamManagerId = leader.Id;
            (await db.WhatsAppConversations.FindAsync(s.Chat))!.UnreadCount = 3;
            await db.SaveChangesAsync();
        }
        await using var app = await StartHttpAsync(s); using var client = app.GetTestClient();
        await Authenticate(client, s.Human, "3");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/conversations/{other.Chat}")).StatusCode);
        await Authenticate(client, monitorId, "9");
        var landing = await client.GetAsync("/Auth/Login");
        Assert.Equal(HttpStatusCode.Redirect, landing.StatusCode);
        Assert.Contains("Monitoring", landing.Headers.Location!.ToString());
        var chats = await client.GetFromJsonAsync<JsonElement>("/api/conversations?pageSize=200");
        var json = chats.ToString();
        Assert.Contains(s.Chat.ToString(), json); Assert.Contains(other.Chat.ToString(), json);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/conversations/{other.Chat}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/conversations/{s.Chat}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/conversations/{s.Chat}/notes")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/whatsapp/accounts/agents")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/whatsapp/dashboard/teams")).StatusCode);
        var monitoring = await client.GetAsync("/Monitoring/Index");
        Assert.Equal(HttpStatusCode.OK, monitoring.StatusCode);
        Assert.Contains("monitoring.js", await monitoring.Content.ReadAsStringAsync());
        var inbox = await client.GetStringAsync("/Inbox/Index");
        Assert.Contains("readOnly: true", inbox);
        Assert.DoesNotContain("id=\"chat-transfer-btn\"", inbox);
        foreach (var path in new[] { "/Admin/Users", "/Admin/WhatsAppAccounts", "/Accountant/Index", "/api/ai-sales/agents" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);

        foreach (var action in new[] { "messages", "read", "assign", "outcome", "notes", "forward" })
        {
            var response = await client.PostAsJsonAsync($"/api/conversations/{s.Chat}/{action}", new { agentId = other.Human, outcome = "lost", reason = "blocked", body = "blocked", textBody = "blocked" });
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, action + ": " + response.StatusCode + " " + await response.Content.ReadAsStringAsync());
        }
        await using var verify = fixture.Create();
        var preserved = (await verify.WhatsAppConversations.FindAsync(s.Chat))!;
        Assert.Equal(s.Human, preserved.AssignedSalesAgentId); Assert.Equal(3, preserved.UnreadCount);
        Assert.Empty(await verify.WhatsAppMessages.Where(m => m.ConversationId == s.Chat).ToListAsync());
        Assert.Empty(await verify.ConversationControls.Where(c => c.ConversationId == s.Chat).ToListAsync());
    }

    [Fact]
    public async Task Monitoring_role_can_be_created_by_admin_and_database_upgrade_preserves_existing_roles()
    {
        var s = await SeedAsync(); await using var db = fixture.Create();
        var migration = new AllowTeamMonitoringRole { ActiveProvider = db.Database.ProviderName! };
        var sql = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in sql.Generate(migration.UpOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        Assert.Contains("20261022030000_AllowTeamMonitoringRole", db.Database.GetMigrations());
        await using var app = await StartHttpAsync(s); using var client = app.GetTestClient();
        await Authenticate(client, s.Admin, "1");
        var email = Guid.NewGuid() + "@example.test";
        var admin = (await db.Users.FindAsync(s.Admin))!;
        var response = await client.PostAsync("/Admin/CreateUser", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["fullName"] = "Team monitoring", ["email"] = email, ["password"] = "Synthetic123!", ["role"] = "9", ["branchId"] = admin.BranchId.ToString()
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var created = await db.Users.SingleAsync(u => u.Email == email);
        Assert.Equal((byte)9, created.Role); Assert.Null(created.ManagerId);
        Assert.Equal((byte)1, admin.Role);
        Assert.False(WhatsAppAuthorization.IsAdmin("9"));
        Assert.False(WhatsAppAuthorization.CanAssignOrTransfer("9"));
        Assert.False(WhatsAppAuthorization.CanManageAccounts("9"));
        Assert.False(WhatsAppAuthorization.CanCreateHandoff("9"));
    }
}
