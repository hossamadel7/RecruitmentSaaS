using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Hubs;
using RecruitmentSaaS.Services;

// DateTime values are stored exactly as the app writes them (UTC, no time-zone conversion), as they were on SQL Server.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);
var isolatedValidation = StagingValidationIsolation.Validate(builder.Configuration, builder.Environment.EnvironmentName);

// Database
builder.Services.AddDbContext<RecruitmentCrmContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Keep the cookie-encryption keys on disk when a path is configured (the OVH server sets it), so logins
// survive restarts and redeploys the way they did on Azure App Service.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrEmpty(keysPath))
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
        .SetApplicationName("RecruitmentSaaS");

// Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // Log out users who were deactivated (or whose role changed) — see UserSessionValidator
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = UserSessionValidator.ValidateAsync
        };
    });

builder.Services.AddMemoryCache();

builder.Services.AddControllersWithViews(o => o.Filters.Add<ConversationMutationFilter>())
    // Dates go to the browser as UTC ("…Z") so they show in the viewer's local time
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter()));

builder.Services.AddHttpClient();

builder.Services.AddScoped<IGoogleSheetsLeadImportService, GoogleSheetsLeadImportService>();
if (!isolatedValidation) builder.Services.AddHostedService<GoogleSheetsImportBackgroundService>();
// No shared pool: unassigned leads (Facebook ads, Google Sheets, …) are handed out round-robin every minute
if (!isolatedValidation) builder.Services.AddHostedService<LeadAutoAssignService>();

builder.Services.AddScoped<IContractParserService, ContractParserService>();

builder.Services.AddScoped<RecruitmentSaaS.Services.INotificationService,
                           RecruitmentSaaS.Services.NotificationService>();

builder.Services.AddScoped<RecruitmentSaaS.Services.IVisaParserService,
                           RecruitmentSaaS.Services.VisaParserService>();

if (!isolatedValidation) builder.Services.AddHostedService<RecruitmentSaaS.Services.AppointmentReminderService>();

// WhatsApp Shared Inbox
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter()));
builder.Services.AddScoped<IWhatsAppCloudApiService, WhatsAppCloudApiService>();
builder.Services.AddScoped<LeadChatStarter>();
builder.Services.AddScoped<CustomerOutcomeService>();       // the one "حالة العميل" choice on a chat (حجز, متابعة, خسارة…)
builder.Services.AddScoped<AiIntakeService>();                // AI WhatsApp assistant (name / age / job)
builder.Services.AddScoped<AiIntakeQueue>();
builder.Services.AddScoped<DurableWorkService>();
if (!isolatedValidation) builder.Services.AddHostedService<DurableWorkWorker>();
if (!isolatedValidation) builder.Services.AddHostedService<AiIntakeTimeoutWorker>();
builder.Services.AddScoped<AiSalesControlService>();
builder.Services.AddScoped<AiSalesContextService>();
builder.Services.AddScoped<AiSalesDraftService>();
builder.Services.AddScoped<AiSalesImprovementService>();
builder.Services.AddScoped<AiSalesDispatchService>();
builder.Services.AddScoped<AiAutomaticReplyService>();
if (!isolatedValidation) builder.Services.AddHostedService<AiAutomaticReplyWorker>();
builder.Services.AddScoped<AiChatLearningService>();
builder.Services.AddScoped<AiChatFactService>();
builder.Services.AddOptions<AiChatLearningOptions>().BindConfiguration("AiChatLearning")
    .Validate(o => !o.Enabled || (o.ApprovedAccountIds.Length > 0 && o.ApprovedAccountIds.All(id => id != Guid.Empty)
        && System.Text.Encoding.UTF8.GetByteCount(o.PseudonymKey) >= 32), "Chat preparation requires selected accounts and a server-side pseudonym secret.")
    .ValidateOnStart();
builder.Services.AddOptions<AiSalesDispatchOptions>().BindConfiguration("AiSalesDispatch")
    .Validate(o => o.MaxAttemptsPerDay is >= 1 and <= 1000, "Invalid dispatch limit.")
    .Validate(o => !o.Enabled || (o.ReleaseApproved && o.ApprovedAccountIds.Length > 0 && o.ApprovedAccountIds.All(id => id != Guid.Empty)),
        "Dispatch requires explicit release approval and approved accounts.")
    .ValidateOnStart();
builder.Services.AddHttpClient<IAiSalesDraftProvider, OpenAiSalesDraftProvider>();
builder.Services.AddScoped<AiVoiceService>();
builder.Services.AddScoped<AiOfficeBookingService>();
builder.Services.AddScoped<AiSalesOperationsService>();
builder.Services.AddOptions<AiOfficeBookingOptions>().BindConfiguration("AiOfficeBooking")
    .Validate(o => o.MaxDaysAhead is >= 1 and <= 90, "Booking horizon must be between 1 and 90 days.").ValidateOnStart();
if (!isolatedValidation) builder.Services.AddHostedService<AiVoiceWorker>();
builder.Services.AddHttpClient<IAiVoiceMediaDownloader, MetaVoiceMediaDownloader>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<IAiVoiceTranscriber, OpenAiVoiceTranscriber>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddOptions<AiVoiceOptions>().BindConfiguration("AiVoice")
    .Validate(o => o.MaxBytes is >= 1024 and <= 25 * 1024 * 1024 && o.MaxPartsPerTurn is >= 1 and <= 4 &&
        o.MaxProviderAttemptsPerDay is >= 1 and <= 1000 && o.GroupGapSeconds is >= 1 and <= 120 &&
        o.QuietSeconds is >= 1 and <= 60 && o.MaxMessageAgeMinutes is >= 1 and <= 60 &&
        double.IsFinite(o.MinimumTokenProbability) && o.MinimumTokenProbability is > 0 and <= 1, "Invalid voice limits.")
    .Validate(o => !o.ExternalProcessingApproved || (o.Enabled && OpenAiVoiceTranscriber.SupportedModel(o.Model) && !string.IsNullOrWhiteSpace(o.ApiKey)),
        "External voice processing requires enabled voice handling, a supported transcription model, and an API key.")
    .ValidateOnStart();
builder.Services.AddOptions<AiSalesEngineOptions>().BindConfiguration("AiSalesEngine")
    .Validate(o => !o.AutoReplyEnabled || (o.DraftsEnabled && o.ExternalProcessingApproved && (o.AutoReplyAnyAssignedConversation || o.ApprovedConversationIds.Length is > 0 and <= 50) && o.AutoReplyAgentIds.Length > 0 && o.AutoReplySinceUtc != null), "Automatic replies require explicit assignments, agents, processing approval and a start time.")
    .Validate(o => o.MaxDraftsPerDay is >= 1 and <= 1000 && o.MaxOutputTokens is >= 200 and <= 2000, "Invalid draft limits.")
    .Validate(o => !o.ExternalProcessingApproved || (o.DraftsEnabled && !string.IsNullOrWhiteSpace(o.Model) && !string.IsNullOrWhiteSpace(o.ApiKey)), "External processing requires enabled drafts, a model, and an API key.")
    .ValidateOnStart();
builder.Services.AddScoped<ConversationMutationFilter>();
builder.Services.AddOptions<AiSalesOptions>().BindConfiguration("AiSales")
    .Validate(o => !o.Enabled || o.TenantId != Guid.Empty, "An enabled AI Sales deployment requires its tenant ID.")
    .ValidateOnStart();
if (!isolatedValidation) builder.Services.AddHostedService<LeadAutoFollowupService>();   // opening template for 45+ leads who never wrote
builder.Services.AddScoped<IWhatsAppWebhookProcessor, WhatsAppWebhookProcessor>();
builder.Services.AddScoped<IInboxRealtimeNotifier, InboxRealtimeNotifier>();
builder.Services.AddScoped<IMetaCredentialStore, MetaCredentialStore>();
builder.Services.AddScoped<IMetaAuthService, MetaAuthService>();

if (isolatedValidation)
    builder.Services.ConfigureAll<Microsoft.Extensions.Http.HttpClientFactoryOptions>(o =>
        o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = new StagingValidationIsolation.BlockOutboundHandler()));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Versioned files (asp-append-version adds ?v=<hash>) never change under the same URL:
        // browsers and Cloudflare keep them for a year instead of re-asking the server every 4 hours
        if (ctx.Context.Request.Query.ContainsKey("v"))
            ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        if (!ctx.Context.Request.Path.StartsWithSegments("/uploads")) return;
        var headers = ctx.Context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        // Anything uploaded before type checks existed (.html, .svg, …) downloads instead of running on our domain
        if (SafeUploads.IsActiveContent(ctx.File.Name))
        {
            headers["Content-Disposition"] = "attachment";
            headers["Content-Security-Policy"] = "sandbox";
        }
    }
});
app.UseRouting();

app.UseAuthentication(); // ← must be before UseAuthorization
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok("OK"));
app.MapHub<InboxHub>("/hubs/inbox");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
