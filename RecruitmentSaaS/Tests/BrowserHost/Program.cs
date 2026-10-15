using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Controllers;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using RecruitmentSaaS.Tests;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Moq;

// Local validation only. This executable is excluded from the production project.
// The fixture refuses all but the dedicated loopback test cluster.
var fixture = new DatabaseFixture();
await fixture.InitializeAsync();
try
{
    var officeDemo = args.Contains("--office-demo");
    var learningDemo = args.Contains("--learning-demo");
    var memoryDemo = args.Contains("--memory-demo");
    var dispatchDemo = args.Contains("--dispatch-demo");
    Guid dispatchConversation = Guid.Empty;
    Guid learningAccount = Guid.Empty;
    var voiceDemo = args.Contains("--voice-demo") || officeDemo || memoryDemo;
    var root = Path.GetFullPath(args.First());
    var tenant = Guid.NewGuid();
    var admin = new User { Id = Guid.NewGuid(), FullName = "Validation Admin", Email = "admin@example.test", PasswordHash = "unused", Role = 1, IsActive = true };
    await using (var db = fixture.Create())
    {
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Validation office", IsActive = true };
        admin.Branch = branch;
        var human = new User { Id = Guid.NewGuid(), Branch = branch, FullName = "Validation Salesperson", Email = "sales@example.test", PasswordHash = "unused", Role = 3, IsActive = true };
        var account = new WhatsAppAccount { Id = Guid.NewGuid(), Name = "Validation account — no transport", PhoneNumberId = "synthetic", WabaId = "synthetic", DisplayPhoneNumber = "201000000000", IsActive = true };
        learningAccount = account.Id;
        var contact = new WhatsAppContact { Id = Guid.NewGuid(), Name = "عميل التحقق", WhatsAppPhoneNumber = "201000000001" };
        var lead = new Lead { Id = Guid.NewGuid(), Branch = branch, RegisteredBy = admin, AssignedSales = human, FullName = contact.Name, Phone = contact.WhatsAppPhoneNumber, Age = 48, InterestedJobTitle = "لحام", Status = 1, LeadSource = 5 };
        var chat = new WhatsAppConversation { Id = Guid.NewGuid(), WhatsAppAccount = account, Contact = contact, Lead = lead, AssignedSalesAgent = human, IntakeStatus = (byte)IntakeStatus.Completed };
        db.AddRange(admin, human, chat);
        db.WhatsAppMessages.Add(new() { Id = Guid.NewGuid(), Conversation = chat, WhatsAppAccount = account,
            Direction = (byte)MessageDirection.Incoming, MessageType = (byte)WhatsAppMessageType.Text,
            TextBody = "ممكن أعرف التفاصيل؟", Status = (byte)MessageStatus.Received, MessageSource = (byte)MessageSource.Customer,
            CreatedAt = DateTime.UtcNow, WhatsAppTimestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();
        if (dispatchDemo)
        {
            dispatchConversation = chat.Id;
            var agent = new AiSalesAgent { TenantId = tenant, WhatsAppAccountId = account.Id, DisplayName = "Synthetic approved sending", Mode = AiSalesMode.Shadow, Paused = false };
            db.Add(agent); await db.SaveChangesAsync();
            await new AiSalesControlService(db, Options.Create(new AiSalesOptions { Enabled = true, TenantId = tenant }))
                .AssignAsync(lead.Id, chat.Id, agent.Id, admin.Id, 0, "Synthetic dispatch browser test", "synthetic-dispatch-assignment", default);
            var fact = new AiSalesKnowledge { TenantId = tenant, WhatsAppAccountId = account.Id, Kind = AiKnowledgeKind.Fact,
                Status = AiKnowledgeStatus.Approved, Title = "Synthetic fact", Content = "Fictional office information", SourceReference = "Synthetic browser test",
                CreatedById = admin.Id, ApprovedById = admin.Id, ApprovedAt = DateTime.UtcNow, ValidFrom = DateTime.UtcNow.AddDays(-1), ValidUntil = DateTime.UtcNow.AddDays(1) };
            db.Add(fact); await db.SaveChangesAsync();
            var context = await new AiSalesContextService(db).ReadAsync(tenant, chat, default);
            db.Add(new AiSalesDraft { TenantId = tenant, ConversationId = chat.Id, AgentId = agent.Id, RequestedById = admin.Id,
                RequestKey = "synthetic-browser-draft", Status = AiDraftStatus.ReviewRequired, OwnershipVersion = 1, MemoryVersion = context.Customer.MemoryVersion,
                ContextFingerprint = AiSalesContextService.Fingerprint(context), KnowledgeSnapshotJson = System.Text.Json.JsonSerializer.Serialize(context.Knowledge.Select(k => new { k.Id, k.Version })),
                SourceIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { fact.Id }), Reply = "رد تجريبي لاختبار اعتماد وإرسال فقط.", Intent = "answer", NeedsHuman = false, Model = "synthetic" });
            await db.SaveChangesAsync();
        }
        if (learningDemo)
        {
            var message = await db.WhatsAppMessages.SingleAsync(m => m.ConversationId == chat.Id);
            message.TextBody = "المرتب التجريبي 900 دولار — قيمة خيالية لاختبار المراجعة فقط";
            message.CreatedAt = message.WhatsAppTimestamp = DateTime.UtcNow.AddDays(-1);
            db.AiSalesKnowledge.Add(new() { TenantId = tenant, WhatsAppAccountId = account.Id, Kind = AiKnowledgeKind.Fact,
                Status = AiKnowledgeStatus.Approved, Title = "راتب تجريبي سابق", Content = "الراتب التجريبي 700 دولار — معلومة خيالية",
                SourceReference = "Synthetic browser validation only", ValidFrom = DateTime.UtcNow.AddDays(-1), ValidUntil = DateTime.UtcNow.AddDays(1),
                CreatedById = admin.Id, ApprovedById = admin.Id, ApprovedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        if (voiceDemo)
        {
            var agent = new AiSalesAgent { Id = Guid.NewGuid(), TenantId = tenant, DisplayName = "Synthetic voice review",
                WhatsAppAccountId = account.Id, Mode = AiSalesMode.Shadow, Paused = false, MaxActiveConversations = 10 };
            db.AiSalesAgents.Add(agent); await db.SaveChangesAsync();
            var controls = new AiSalesControlService(db, Options.Create(new AiSalesOptions { Enabled = true, TenantId = tenant }));
            await controls.AssignAsync(lead.Id, chat.Id, agent.Id, admin.Id, 0, "Synthetic voice browser validation", "voice-demo-assignment", default);
            if (memoryDemo)
            {
                db.AiCustomerMemories.Add(new() { ConversationId = chat.Id, TenantId = tenant, ReviewedById = admin.Id,
                    ReviewedSummary = "عميل تجريبي يفضل التعبئة والتغليف؛ ينتظر تجديد الجواز.",
                    ReviewedDetailsJson = new AiCustomerMemoryDetails(AiPassportReadiness.Renewing, "التعبئة والتغليف", "ينتظر الجواز", "مراجعة زيارة المكتب", "الخميس؛ غير مؤكد").ToJson() });
                db.AiSalesDrafts.Add(new() { TenantId = tenant, ConversationId = chat.Id, AgentId = agent.Id, RequestedById = admin.Id,
                    RequestKey = "synthetic-improvement-demo", Status = AiDraftStatus.Rejected, OwnershipVersion = 1,
                    Reply = "مسودة خيالية لاختبار شاشة تحسين الأسلوب فقط.", Intent = "answer", ReviewNote = "مثال تجريبي، وليس ردًا لعميل حقيقي.",
                    Model = "synthetic-no-provider", ReviewedById = admin.Id, ReviewedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            var audio = new WhatsAppMessage { Id = Guid.NewGuid(), ConversationId = chat.Id, WhatsAppAccountId = account.Id,
                Direction = (byte)MessageDirection.Incoming, MessageType = (byte)WhatsAppMessageType.Audio, MediaId = "123456",
                Status = (byte)MessageStatus.Received, MessageSource = (byte)MessageSource.Customer, CreatedAt = DateTime.UtcNow, WhatsAppTimestamp = DateTime.UtcNow };
            var turn = new AiVoiceTurn { TenantId = tenant, ConversationId = chat.Id, AgentId = agent.Id, OwnershipVersion = 1,
                Status = AiVoiceStatus.ReadyForReview, Transcript = "ممكن أعرف عنوان المكتب وموعد الساعة 3؟", NeedsClarification = true,
                LastMessageAt = DateTime.UtcNow, AvailableAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, RequestedById = admin.Id };
            db.AddRange(audio, turn, new AiVoicePart { MessageId = audio.Id, TurnId = turn.Id, MediaId = audio.MediaId,
                Status = AiVoicePartStatus.Completed, Transcript = turn.Transcript, Model = "synthetic-no-provider-call" });
            await db.SaveChangesAsync();
            if (officeDemo)
            {
                db.AiOfficePolicies.Add(new() { TenantId = tenant, WhatsAppAccountId = account.Id, BranchId = branch.Id,
                    OpenDaysMask = 127, OpensAtMinute = 0, ClosesAtMinute = 1439, Enabled = true, ValidUntil = DateTime.UtcNow.AddDays(30),
                    ApprovedById = admin.Id, SourceReference = "Synthetic office hours for isolated browser validation" });
                db.WhatsAppMessages.Add(new() { Id = Guid.NewGuid(), ConversationId = chat.Id, WhatsAppAccountId = account.Id,
                    Direction = (byte)MessageDirection.Incoming, MessageType = (byte)WhatsAppMessageType.Text, MessageSource = (byte)MessageSource.Customer,
                    Status = (byte)MessageStatus.Received, TextBody = "ايوه موافق على الفرع التجريبي غدا الساعة 12 لاستشارة عامة من غير ضمان وظيفة",
                    CreatedAt = DateTime.UtcNow, WhatsAppTimestamp = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }
    }

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        EnvironmentName = "Testing", ContentRootPath = Path.GetTempPath(),
        WebRootPath = Path.Combine(root, "wwwroot"), ApplicationName = typeof(AiSalesAdminController).Assembly.GetName().Name
    });
    builder.Configuration.Sources.Clear();
    builder.Configuration.AddInMemoryCollection();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.WebHost.UseUrls("http://127.0.0.1:5189");
    builder.Services.AddDbContext<RecruitmentCrmContext>(o => o.UseNpgsql(fixture.ConnectionString));
    builder.Services.Configure<AiSalesOptions>(o => { o.Enabled = true; o.TenantId = tenant; });
    builder.Services.AddScoped<AiSalesControlService>();
    builder.Services.Configure<AiSalesEngineOptions>(o => o.DraftsEnabled = true);
    builder.Services.AddScoped<AiSalesContextService>();
    builder.Services.AddScoped<AiSalesDraftService>();
    builder.Services.AddScoped<AiSalesImprovementService>();
    builder.Services.Configure<AiSalesDispatchOptions>(o => { o.Enabled = dispatchDemo; o.ReleaseApproved = dispatchDemo;
        o.ApprovedAccountIds = dispatchDemo ? [learningAccount] : []; o.ApprovedConversationIds = dispatchDemo ? [dispatchConversation] : []; });
    builder.Services.AddScoped<AiSalesDispatchService>();
    var fakeTransport = new Moq.Mock<IWhatsAppCloudApiService>(Moq.MockBehavior.Strict);
    if (dispatchDemo) fakeTransport.Setup(x => x.SendTextMessageAsync(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<CancellationToken>()))
        .ReturnsAsync(new WhatsAppSendResult { Success = true, WhatsAppMessageId = "wamid.synthetic-browser-only" });
    builder.Services.AddSingleton(fakeTransport.Object);
    builder.Services.AddSingleton(Moq.Mock.Of<IInboxRealtimeNotifier>());
    builder.Services.Configure<AiChatLearningOptions>(o => { o.Enabled = learningDemo; o.ApprovedAccountIds = learningDemo ? [learningAccount] : [];
        o.PseudonymKey = learningDemo ? "synthetic-browser-pseudonym-secret-not-live" : ""; });
    builder.Services.AddScoped<AiChatLearningService>();
    builder.Services.AddScoped<AiChatFactService>();
    builder.Services.Configure<AiOfficeBookingOptions>(o => o.Enabled = true);
    builder.Services.AddScoped<AiOfficeBookingService>();
    builder.Services.AddScoped<AiSalesOperationsService>();
    builder.Services.Configure<AiVoiceOptions>(o => { o.Enabled = voiceDemo; o.ExternalProcessingApproved = voiceDemo; });
    builder.Services.AddScoped<AiVoiceService>();
    builder.Services.AddSingleton<IAiVoiceMediaDownloader>(Moq.Mock.Of<IAiVoiceMediaDownloader>(Moq.MockBehavior.Strict));
    builder.Services.AddSingleton<IAiVoiceTranscriber>(Moq.Mock.Of<IAiVoiceTranscriber>(Moq.MockBehavior.Strict));
    builder.Services.AddSingleton<IAiSalesDraftProvider>(Moq.Mock.Of<IAiSalesDraftProvider>(Moq.MockBehavior.Strict));
    builder.Services.AddScoped<ConversationMutationFilter>();
    builder.Services.AddScoped<INotificationService, NotificationService>();
    builder.Services.AddControllersWithViews(o => o.Filters.Add<ConversationMutationFilter>()).AddApplicationPart(typeof(AiSalesAdminController).Assembly);
    builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
    builder.Services.AddAuthorization();
    await using var app = builder.Build();
    app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
    app.MapGet("/validation/login", async (HttpContext context) =>
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()), new Claim(ClaimTypes.Role, "1"), new Claim(ClaimTypes.Name, admin.FullName) };
        await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        return Results.Redirect("/AiSalesAdmin");
    });
    app.MapGet("/validation/health", () => Results.Ok("Synthetic database only; no hosted workers or outbound transport registered."));
    app.MapPost("/validation/stop", (IHostApplicationLifetime lifetime) => { lifetime.StopApplication(); return Results.Ok(); });
    app.MapControllerRoute("default", "{controller=AiSalesAdmin}/{action=Index}/{id?}");
    Console.WriteLine("Validation login: http://127.0.0.1:5189/validation/login");
    Console.WriteLine("Disposable database: " + new Npgsql.NpgsqlConnectionStringBuilder(fixture.ConnectionString).Database);
    await app.RunAsync();
}
finally { await fixture.DisposeAsync(); }
