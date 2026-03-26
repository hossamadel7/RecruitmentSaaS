using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
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
    });

builder.Services.AddControllersWithViews();

builder.Services.AddHttpClient();

builder.Services.AddScoped<IGoogleSheetsLeadImportService, GoogleSheetsLeadImportService>();
builder.Services.AddHostedService<GoogleSheetsImportBackgroundService>();

builder.Services.AddScoped<RecruitmentSaaS.Services.INotificationService,
                           RecruitmentSaaS.Services.NotificationService>();

builder.Services.AddScoped<RecruitmentSaaS.Services.IVisaParserService,
                           RecruitmentSaaS.Services.VisaParserService>();

builder.Services.AddHostedService<RecruitmentSaaS.Services.AppointmentReminderService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication(); // ← must be before UseAuthorization
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();