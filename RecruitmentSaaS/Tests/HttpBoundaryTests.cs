using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Controllers.Api;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Services;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    private async Task<WebApplication> StartHttpAsync(Seed seed)
    {
        // Deliberately do not run production Program or load its settings/workers.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = Path.GetTempPath() });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<RecruitmentCrmContext>(o => o.UseNpgsql(fixture.ConnectionString));
        builder.Services.Configure<AiSalesOptions>(o => { o.Enabled = true; o.TenantId = seed.Tenant; });
        builder.Services.AddScoped<AiSalesControlService>();
        builder.Services.Configure<AiSalesEngineOptions>(o => o.DraftsEnabled = true);
        builder.Services.AddScoped<AiSalesContextService>();
        builder.Services.AddScoped<AiSalesDraftService>();
        builder.Services.AddScoped<AiSalesImprovementService>();
        builder.Services.Configure<AiSalesDispatchOptions>(_ => { });
        builder.Services.AddScoped<AiSalesDispatchService>();
        builder.Services.Configure<AiChatLearningOptions>(_ => { });
        builder.Services.AddScoped<AiChatLearningService>();
        builder.Services.AddScoped<AiChatFactService>();
        builder.Services.AddSingleton<IWhatsAppCloudApiService>(Moq.Mock.Of<IWhatsAppCloudApiService>(Moq.MockBehavior.Strict));
        builder.Services.AddSingleton<IInboxRealtimeNotifier>(Moq.Mock.Of<IInboxRealtimeNotifier>());
        builder.Services.AddSingleton<INotificationService>(Moq.Mock.Of<INotificationService>());
        builder.Services.AddScoped<CustomerOutcomeService>();
        builder.Services.Configure<AiOfficeBookingOptions>(o => o.Enabled = true);
        builder.Services.AddScoped<AiOfficeBookingService>();
        builder.Services.AddScoped<AiSalesOperationsService>();
        builder.Services.Configure<AiVoiceOptions>(_ => { });
        builder.Services.AddScoped<AiVoiceService>();
        builder.Services.AddSingleton<IAiVoiceMediaDownloader>(Moq.Mock.Of<IAiVoiceMediaDownloader>(Moq.MockBehavior.Strict));
        builder.Services.AddSingleton<IAiVoiceTranscriber>(Moq.Mock.Of<IAiVoiceTranscriber>(Moq.MockBehavior.Strict));
        builder.Services.AddSingleton<IAiSalesDraftProvider>(Moq.Mock.Of<IAiSalesDraftProvider>());
        builder.Services.AddScoped<ConversationMutationFilter>();
        builder.Services.AddControllersWithViews(o =>
            {
                o.Filters.Add<MonitoringReadOnlyFilter>(int.MinValue);
                o.Filters.AddService<ConversationMutationFilter>();
            })
            .AddApplicationPart(typeof(AiSalesController).Assembly);
        builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, SyntheticAuth>("Test", _ => { });
        builder.Services.AddAuthorization();
        var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); app.MapDefaultControllerRoute();
        app.MapGet("/test-token", (HttpContext ctx, IAntiforgery antiforgery) =>
            Results.Text(antiforgery.GetAndStoreTokens(ctx).RequestToken!));
        await app.StartAsync(); return app;
    }

    private static async Task Authenticate(HttpClient client, Guid actor, string role)
    {
        client.DefaultRequestHeaders.Remove("X-Test-User"); client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Remove("RequestVerificationToken"); client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("X-Test-User", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var response = await client.GetAsync("/test-token"); response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Cookie", response.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_searches_across_accounts_and_filters_without_expanding_team_roles()
    {
        var first = await SeedAsync(); var second = await SeedAsync();
        var search = "CrossAccount-" + Guid.NewGuid().ToString("N");
        await using (var db = fixture.Create())
        {
            (await db.Leads.FindAsync(first.Lead))!.FullName = search;
            (await db.Leads.FindAsync(second.Lead))!.FullName = search;
            await db.SaveChangesAsync();
        }
        await using var app = await StartHttpAsync(first); using var client = app.GetTestClient();
        await Authenticate(client, first.Admin, "1");
        var all = await client.GetFromJsonAsync<JsonElement>("/api/ai-sales/candidates?search=" + search);
        Assert.Contains(all.EnumerateArray(), c => c.GetProperty("conversationId").GetGuid() == first.Chat);
        Assert.Contains(all.EnumerateArray(), c => c.GetProperty("conversationId").GetGuid() == second.Chat);
        var selected = all.EnumerateArray().Single(c => c.GetProperty("conversationId").GetGuid() == second.Chat);
        var account = selected.GetProperty("whatsAppAccountId").GetGuid();
        var filtered = await client.GetFromJsonAsync<JsonElement>("/api/ai-sales/candidates?accountId=" + account);
        Assert.All(filtered.EnumerateArray(), c => Assert.Equal(account, c.GetProperty("whatsAppAccountId").GetGuid()));
        Assert.Contains(filtered.EnumerateArray(), c => c.GetProperty("conversationId").GetGuid() == second.Chat);
        await Authenticate(client, first.Human, "7");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai-sales/candidates")).StatusCode);
    }

    [Fact]
    public async Task Http_enforces_auth_role_csrf_versions_and_control_routes()
    {
        var s = await SeedAsync(); await using var app = await StartHttpAsync(s); using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ai-sales/agents")).StatusCode);
        await Authenticate(client, s.Human, "3");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai-sales/agents")).StatusCode);
        await Authenticate(client, s.Admin, "1");
        var url = $"/api/ai-sales/leads/{s.Lead}/assign";
        var assignment = new { agentId = s.Agent, conversationId = s.Chat, expectedVersion = 0, reason = "HTTP pilot" };
        var token = client.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "http-assign");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, assignment)).StatusCode);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        var assigned = await client.PostAsJsonAsync(url, assignment); Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(url, assignment)).StatusCode);
        client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", "http-takeover");
        var takeover = await client.PostAsJsonAsync($"/api/ai-sales/conversations/{s.Chat}/takeover", new { expectedVersion = 1, reason = "HTTP takeover" });
        Assert.Equal(HttpStatusCode.OK, takeover.StatusCode);
        client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", "http-stale");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/ai-sales/conversations/{s.Chat}/resume", new { expectedVersion = 1, reason = "Stale request" })).StatusCode);
        await Authenticate(client, s.Human, "8");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/ai-sales/conversations/{s.Chat}/release", new { expectedVersion = 2, reason = "Forbidden release" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/ai-sales/conversations/{s.Chat}")).StatusCode);
    }

    [Fact]
    public async Task Http_agent_creation_is_idempotent_and_autonomous_mode_is_rejected()
    {
        var s = await SeedAsync(); await using var app = await StartHttpAsync(s); using var client = app.GetTestClient();
        await Authenticate(client, s.Admin, "1"); await using var db = fixture.Create();
        var account = (await db.WhatsAppConversations.FindAsync(s.Chat))!.WhatsAppAccountId;
        client.DefaultRequestHeaders.Add("Idempotency-Key", "agent-create");
        var request = new { displayName = "HTTP agent", whatsAppAccountId = account, mode = 1, paused = true, maxActiveConversations = 2, expectedVersion = 0 };
        var first = await client.PostAsJsonAsync("/api/ai-sales/agents", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var replay = await client.PostAsJsonAsync("/api/ai-sales/agents", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var a = await first.Content.ReadFromJsonAsync<JsonElement>(); var b = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.GetProperty("id").GetGuid(), b.GetProperty("id").GetGuid());
        client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", "agent-autonomous");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/ai-sales/agents", new { displayName = "Unsafe", whatsAppAccountId = account, mode = 2, paused = false, maxActiveConversations = 2, expectedVersion = 0 })).StatusCode);
        var features = await client.GetFromJsonAsync<JsonElement>("/api/ai-sales/agents");
        Assert.False(features.GetProperty("autonomousSendingAvailable").GetBoolean());
        Assert.False(features.GetProperty("rotationAvailable").GetBoolean());
    }
}

public sealed class SyntheticAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Guid.TryParse(Request.Headers["X-Test-User"], out var id)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, Request.Headers["X-Test-Role"].ToString()) }, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
