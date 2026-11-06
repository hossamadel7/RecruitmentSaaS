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
    private static (AiSalesEngineOptions Engine, AiSalesDispatchOptions Dispatch) AllOn(Seed s, Guid account) => (
        new() { AutoReplyEnabled = true, DraftsEnabled = true, ExternalProcessingApproved = true, ApiKey = "test", Model = "test",
            AutoReplyAnyAssignedConversation = true, AutoReplyAgentIds = [s.Agent], AutoReplySinceUtc = DateTime.UtcNow.AddHours(-1) },
        new() { Enabled = true, ReleaseApproved = true, AutomaticEnabled = true, ApprovedAccountIds = [account] });
    private static AiAutoReplyDiagnostics Diagnostics(Data.RecruitmentCrmContext db, Seed s, AiSalesEngineOptions engine, AiSalesDispatchOptions dispatch) =>
        new(db, Options.Create(new AiSalesOptions { Enabled = true, TenantId = s.Tenant }), Options.Create(engine), Options.Create(dispatch));

    [Fact]
    public async Task Auto_reply_check_passes_when_everything_is_enabled_and_names_the_first_blocker_otherwise()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); await using var db = fixture.Create();
        var account = await db.WhatsAppConversations.Where(c => c.Id == s.Chat).Select(c => c.WhatsAppAccountId).SingleAsync();
        var (engine, dispatch) = AllOn(s, account);
        var ok = await Diagnostics(db, s, engine, dispatch).CheckAsync(s.Chat, default);
        Assert.True(ok.WillReply, string.Join(" | ", ok.Checks.Where(c => !c.Ok).Select(c => c.Key)));
        Assert.All(ok.Checks, c => Assert.Null(c.Fix));

        engine.AutoReplyAgentIds = [];
        var blocked = await Diagnostics(db, s, engine, dispatch).CheckAsync(s.Chat, default);
        Assert.False(blocked.WillReply);
        Assert.Equal("agent-allowed", Assert.Single(blocked.Checks, c => !c.Ok).Key);
        Assert.Contains("AutoReplyAgentIds", blocked.Summary);
        Assert.DoesNotContain("test", JsonSerializer.Serialize(blocked)); // never echoes configured secrets
    }

    [Fact]
    public async Task Auto_reply_check_explains_a_handoff_draft_and_an_old_assignment()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); await using var db = fixture.Create();
        var account = await db.WhatsAppConversations.Where(c => c.Id == s.Chat).Select(c => c.WhatsAppAccountId).SingleAsync();
        var (engine, dispatch) = AllOn(s, account);
        var latest = await db.WhatsAppMessages.Where(m => m.ConversationId == s.Chat && m.Direction == 1).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).FirstAsync();
        var version = await db.ConversationControls.Where(c => c.ConversationId == s.Chat).Select(c => c.OwnershipVersion).SingleAsync();
        db.AiSalesDrafts.Add(new() { TenantId = s.Tenant, ConversationId = s.Chat, AgentId = s.Agent, RequestKey = $"auto:{s.Chat}:{latest.Id}:{version}",
            OwnershipVersion = version, ContextFingerprint = "x", PromptVersion = "x", Status = AiDraftStatus.ReviewRequired, NeedsHuman = true, Intent = "handoff", ReviewNote = "Customer is waiting for staff" });
        await db.SaveChangesAsync();
        var report = await Diagnostics(db, s, engine, dispatch).CheckAsync(s.Chat, default);
        Assert.Equal("draft", Assert.Single(report.Checks, c => !c.Ok).Key);
        Assert.Contains("محتاجة موظف", report.Summary);

        engine.AutoReplySinceUtc = DateTime.UtcNow.AddMinutes(5);
        var old = await Diagnostics(db, s, engine, dispatch).CheckAsync(s.Chat, default);
        Assert.Equal("assignment-date", old.Checks.First(c => !c.Ok).Key);
    }

    [Fact]
    public async Task Ai_sales_page_loads_the_requested_chat_and_its_check_for_admins_only()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); await using var app = await StartHttpAsync(s); using var client = app.GetTestClient();
        await Authenticate(client, s.Admin, "1");
        var chats = await client.GetFromJsonAsync<JsonElement>("/api/ai-sales/candidates?conversationId=" + s.Chat);
        Assert.Equal(s.Chat, Assert.Single(chats.EnumerateArray()).GetProperty("conversationId").GetGuid());
        var check = await client.GetFromJsonAsync<JsonElement>("/api/ai-sales/conversations/" + s.Chat + "/auto-reply-check");
        Assert.False(check.GetProperty("willReply").GetBoolean()); // test host leaves automatic replies off
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/ai-sales/candidates?conversationId=" + Guid.NewGuid())).StatusCode);
        await Authenticate(client, s.Human, "7");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai-sales/conversations/" + s.Chat + "/auto-reply-check")).StatusCode);
    }
}
