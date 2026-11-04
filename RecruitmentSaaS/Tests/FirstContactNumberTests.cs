using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.DTOs;
using RecruitmentSaaS.Models.Entities;
using System.Net.Http.Json;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Client_details_show_first_inbound_number_across_chats(bool hasIncoming)
    {
        var s = await SeedAsync();
        var unrelated = await SeedAsync();
        var now = DateTime.UtcNow;
        await using (var db = fixture.Create())
        {
            var current = await db.WhatsAppConversations.SingleAsync(c => c.Id == s.Chat);
            var account = new WhatsAppAccount { Id = Guid.NewGuid(), Name = "Original number", DisplayPhoneNumber = "201111111111",
                PhoneNumberId = Guid.NewGuid().ToString("N"), WabaId = "test", IsActive = false };
            // A later-created conversation can contain older imported messages.
            var original = new WhatsAppConversation { Id = Guid.NewGuid(), ContactId = current.ContactId,
                WhatsAppAccount = account, CreatedAt = now, AssignedSalesAgentId = s.Human };
            db.WhatsAppConversations.Add(original);
            void AddMessage(Guid chat, Guid receivingAccount, MessageDirection direction, DateTime sentAt)
                => db.WhatsAppMessages.Add(new WhatsAppMessage { Id = Guid.NewGuid(), ConversationId = chat,
                    WhatsAppAccountId = receivingAccount, Direction = (byte)direction, MessageType = (byte)WhatsAppMessageType.Text,
                    MessageSource = (byte)MessageSource.CloudApi, Status = (byte)MessageStatus.Received,
                    WhatsAppTimestamp = sentAt, CreatedAt = now });
            AddMessage(current.Id, current.WhatsAppAccountId, MessageDirection.Outgoing, now.AddDays(-10));
            var other = await db.WhatsAppConversations.SingleAsync(c => c.Id == unrelated.Chat);
            AddMessage(other.Id, other.WhatsAppAccountId, MessageDirection.Incoming, now.AddDays(-20));
            if (hasIncoming)
            {
                AddMessage(current.Id, current.WhatsAppAccountId, MessageDirection.Incoming, now.AddDays(-1));
                AddMessage(original.Id, account.Id, MessageDirection.Incoming, now.AddDays(-5));
            }
            await db.SaveChangesAsync();
        }

        await using var app = await StartHttpAsync(s);
        using var client = app.GetTestClient();
        await Authenticate(client, s.Admin, "1");
        var detail = await client.GetFromJsonAsync<ConversationDetailDto>($"/api/conversations/{s.Chat}");
        Assert.NotNull(detail);
        Assert.Equal(hasIncoming ? "Original number" : null, detail.FirstContactAccountName);
        Assert.Equal(hasIncoming ? "201111111111" : null, detail.FirstContactPhoneNumber);
        Assert.Equal("Test account", detail.WhatsAppAccountName);
    }
}
