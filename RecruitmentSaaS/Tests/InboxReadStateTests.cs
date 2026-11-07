using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
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
