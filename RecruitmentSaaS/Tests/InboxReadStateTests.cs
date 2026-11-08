using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    [Fact]
    public async Task Inbox_transfer_of_an_AI_chat_to_a_salesperson_ends_the_AI_assignment_in_one_step()
    {
        var s = await SeedAsync();
        await using (var db = fixture.Create())
            await Control(db, s).AssignAsync(s.Lead, s.Chat, s.Agent, s.Admin, 0, "AI pilot", "ai-before-transfer", default);
        await using var app = await StartHttpAsync(s); using var client = app.GetTestClient();
        await Authenticate(client, s.Admin, "1");

        var aiChats = await client.GetFromJsonAsync<JsonElement>("/api/conversations?assigned=ai&pageSize=200");
        Assert.Contains(s.Chat.ToString(), aiChats.ToString());

        var response = await client.PostAsJsonAsync($"/api/conversations/{s.Chat}/assign", new { agentId = s.Human });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verify = fixture.Create();
        var chat = (await verify.WhatsAppConversations.FindAsync(s.Chat))!;
        Assert.Null(chat.AssignedAiSalesAgentId); Assert.Equal(s.Human, chat.AssignedSalesAgentId);
        Assert.NotNull((await verify.AiLeadAssignments.SingleAsync(a => a.ConversationId == s.Chat)).EndedAt);
        Assert.Null((await verify.Leads.FindAsync(s.Lead))!.AssignedAiSalesAgentId);
        Assert.DoesNotContain(s.Chat.ToString(), (await client.GetFromJsonAsync<JsonElement>("/api/conversations?assigned=ai&pageSize=200")).ToString());

        // A salesperson still cannot move chats.
        await Authenticate(client, s.Human, "3");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/conversations/{s.Chat}/assign", new { agentId = s.Admin })).StatusCode);
    }

    [Fact]
    public async Task Admin_opening_an_assigned_chat_keeps_it_unread_until_its_salesperson_opens_it()
    {
        var s = await SeedAsync();
        await using (var db = fixture.Create())
        {
            var chat = (await db.WhatsAppConversations.FindAsync(s.Chat))!;
            chat.AssignedSalesAgentId = s.Human; chat.UnreadCount = 2;
            await db.SaveChangesAsync();
        }
        await using var app = await StartHttpAsync(s); using var client = app.GetTestClient();

        async Task<(bool Cleared, int Stored)> Read()
        {
            var response = await client.PostAsync($"/api/conversations/{s.Chat}/read", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            await using var verify = fixture.Create();
            return (body.GetProperty("cleared").GetBoolean(), (await verify.WhatsAppConversations.FindAsync(s.Chat))!.UnreadCount);
        }

        await Authenticate(client, s.Admin, "1");
        Assert.Equal((false, 2), await Read());

        await Authenticate(client, s.Human, "3");
        Assert.Equal((true, 0), await Read());

        // A chat with no salesperson yet is still cleared by whoever opens it.
        await using (var db = fixture.Create())
        {
            var chat = (await db.WhatsAppConversations.FindAsync(s.Chat))!;
            chat.AssignedSalesAgentId = null; chat.UnreadCount = 1;
            await db.SaveChangesAsync();
        }
        await Authenticate(client, s.Admin, "1");
        Assert.Equal((true, 0), await Read());
    }
}
