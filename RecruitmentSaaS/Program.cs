using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Hubs;
using RecruitmentSaaS.Services;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<RecruitmentCrmContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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

builder.Services.AddControllersWithViews();

builder.Services.AddHttpClient();

builder.Services.AddScoped<IGoogleSheetsLeadImportService, GoogleSheetsLeadImportService>();
builder.Services.AddHostedService<GoogleSheetsImportBackgroundService>();

builder.Services.AddScoped<IContractParserService, ContractParserService>();

builder.Services.AddScoped<RecruitmentSaaS.Services.INotificationService,
                           RecruitmentSaaS.Services.NotificationService>();

builder.Services.AddScoped<RecruitmentSaaS.Services.IVisaParserService,
                           RecruitmentSaaS.Services.VisaParserService>();

builder.Services.AddHostedService<RecruitmentSaaS.Services.AppointmentReminderService>();

// WhatsApp Shared Inbox
builder.Services.AddSignalR();
builder.Services.AddScoped<IWhatsAppCloudApiService, WhatsAppCloudApiService>();
builder.Services.AddScoped<IWhatsAppWebhookProcessor, WhatsAppWebhookProcessor>();
builder.Services.AddScoped<IInboxRealtimeNotifier, InboxRealtimeNotifier>();
builder.Services.AddScoped<IMetaCredentialStore, MetaCredentialStore>();
builder.Services.AddScoped<IMetaAuthService, MetaAuthService>();

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