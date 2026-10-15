using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using RecruitmentSaaS.Controllers;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = "";
    public async Task InitializeAsync()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var configured = Environment.GetEnvironmentVariable("AI_SALES_TEST_DATABASE")
            ?? throw new InvalidOperationException("Set AI_SALES_TEST_DATABASE to an isolated local PostgreSQL instance. Production settings are never loaded.");
        var cs = new NpgsqlConnectionStringBuilder(configured);
        if (cs.Host != "127.0.0.1" || cs.Port != 55439 || cs.Username != "phase1_test")
            throw new InvalidOperationException("Tests accept only the dedicated local phase1_test instance on port 55439.");
        cs.Database = "phase1_" + Guid.NewGuid().ToString("N"); ConnectionString = cs.ConnectionString;
        await using var db = Create(); await db.Database.EnsureCreatedAsync();
    }
    public RecruitmentCrmContext Create() => new(new DbContextOptionsBuilder<RecruitmentCrmContext>().UseNpgsql(ConnectionString).Options);
    public async Task DisposeAsync()
    {
        if (ConnectionString.Length == 0) return;
        await using var db = Create(); await db.Database.EnsureDeletedAsync();
    }
}

public sealed partial class FoundationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admin_can_prepare_paused_assignment_without_starting_intake_or_sales(bool collecting)
    {
        var s = await SeedAsync(collecting); await using var db = fixture.Create();
        var agent = await db.AiSalesAgents.SingleAsync(a => a.Id == s.Agent);
        agent.Paused = true; await db.SaveChangesAsync();
        await Control(db, s).AssignAsync(s.Lead, s.Chat, s.Agent, s.Admin, 0, "Prepare only", "paused-assignment", default);
        Assert.True(agent.Paused);
        Assert.Equal(ConversationControllerMode.Paused, (await db.ConversationControls.FindAsync(s.Chat))!.Controller);
        Assert.Empty(await db.AiSalesDrafts.Where(d => d.ConversationId == s.Chat).ToListAsync());
        Assert.Empty(await db.AiOutboundIntents.Where(i => i.ConversationId == s.Chat).ToListAsync());
        await Control(db, s).ControlAsync(s.Chat, s.Admin, 1, "release", "Restore human", "paused-release", default);
        Assert.Equal(s.Human, (await db.Leads.FindAsync(s.Lead))!.AssignedSalesId);
    }
    private sealed record Seed(Guid Tenant, Guid Admin, Guid Lead, Guid Chat, Guid Agent, Guid Human);
    private async Task<Seed> SeedAsync(bool collecting = false)
    {
        await using var db = fixture.Create();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test office", IsActive = true };
        var admin = new User { Id = Guid.NewGuid(), Branch = branch, FullName = "Admin", Email = Guid.NewGuid()+"@example.test", PasswordHash = "unused", Role = 1, IsActive = true };
        var human = new User { Id = Guid.NewGuid(), Branch = branch, FullName = "Human", Email = Guid.NewGuid()+"@example.test", PasswordHash = "unused", Role = 3, IsActive = true };
        var account = new WhatsAppAccount { Id = Guid.NewGuid(), Name = "Test account", PhoneNumberId = Guid.NewGuid().ToString("N"), WabaId = "test", DisplayPhoneNumber = "201000000000", IsActive = true };
        var contact = new WhatsAppContact { Id = Guid.NewGuid(), Name = "Synthetic customer", WhatsAppPhoneNumber = Guid.NewGuid().ToString("N") };
        var lead = new Lead { Id = Guid.NewGuid(), Branch = branch, RegisteredBy = admin, AssignedSales = human,
            FullName = "Synthetic customer", Phone = contact.WhatsAppPhoneNumber, Age = 48, InterestedJobTitle = "Welder", Status = 1, LeadSource = 5 };
        var chat = new WhatsAppConversation { Id = Guid.NewGuid(), WhatsAppAccount = account, Contact = contact, Lead = lead,
            AssignedSalesAgent = human, IntakeStatus = collecting ? (byte)IntakeStatus.Collecting : (byte)IntakeStatus.Completed };
        var agent = new AiSalesAgent { TenantId = Guid.NewGuid(), WhatsAppAccountId = account.Id, Mode = AiSalesMode.Shadow, Paused = false, MaxActiveConversations = 10 };
        db.AddRange(admin, human, lead, chat, agent); await db.SaveChangesAsync();
        return new(agent.TenantId, admin.Id, lead.Id, chat.Id, agent.Id, human.Id);
    }
    private static AiSalesControlService Control(RecruitmentCrmContext db, Seed s, bool enabled = true) =>
        new(db, Options.Create(new AiSalesOptions { Enabled = enabled, TenantId = s.Tenant }));

    [Fact]
    public async Task Manual_assignment_is_audited_idempotent_and_preserves_data()
    {
        var s = await SeedAsync(); await using var db = fixture.Create(); var service = Control(db, s);
        var result = await service.AssignAsync(s.Lead,s.Chat,s.Agent,s.Admin,0,"Pilot","assign-1",default);
        var replay = await service.AssignAsync(s.Lead,s.Chat,s.Agent,s.Admin,0,"Pilot","assign-1",default);
        Assert.Equal(result,replay); Assert.Equal(1,result.OwnershipVersion);
        var lead = await db.Leads.SingleAsync(l=>l.Id==s.Lead);
        Assert.Null(lead.AssignedSalesId); Assert.Equal(s.Agent,lead.AssignedAiSalesAgentId); Assert.Equal((byte)48,lead.Age);
        Assert.Single(await db.AiLeadAssignments.Where(a=>a.LeadId==s.Lead).ToListAsync());
        Assert.Equal(s.Human,(await db.AiLeadAssignments.SingleAsync(a=>a.LeadId==s.Lead)).PreviousSalesId);
        Assert.Equal(ConversationControllerMode.Assisted,(await db.ConversationControls.FindAsync(s.Chat))!.Controller);
        Assert.False(await db.Leads.AnyAsync(l=>l.Id==s.Lead && l.AssignedSalesId==null && l.AssignedAiSalesAgentId==null));
        await Assert.ThrowsAsync<AiSalesConflict>(()=>service.AssignAsync(s.Lead,s.Chat,s.Agent,s.Admin,0,"Different","assign-1",default));
    }

    [Fact]
    public async Task Takeover_invalidates_intents_and_release_restores_human()
    {
        var s=await SeedAsync(); await using var db=fixture.Create(); var service=Control(db,s);
        await service.AssignAsync(s.Lead,s.Chat,s.Agent,s.Admin,0,"Pilot","assign",default);
        db.AiOutboundIntents.Add(new(){TenantId=s.Tenant,ConversationId=s.Chat,OwnershipVersion=1,OperationKey="old"}); await db.SaveChangesAsync();
        var takeover=await service.ControlAsync(s.Chat,s.Admin,1,"takeover","Staff takes over","take",default);
        Assert.Equal(2,takeover.OwnershipVersion);
        Assert.Equal("Invalidated",await db.AiOutboundIntents.Where(x=>x.ConversationId==s.Chat).Select(x=>x.State).SingleAsync());
        await Assert.ThrowsAsync<AiSalesConflict>(()=>service.ControlAsync(s.Chat,s.Admin,1,"resume","Stale","stale",default));
        var control=await db.ConversationControls.FindAsync(s.Chat); Assert.Contains("Welder",control!.HandoffSummary);
        await service.ControlAsync(s.Chat,s.Admin,2,"release","Back to staff","release",default);
        var lead=await db.Leads.SingleAsync(l=>l.Id==s.Lead); Assert.Equal(s.Human,lead.AssignedSalesId); Assert.Null(lead.AssignedAiSalesAgentId);
        Assert.NotNull((await db.AiLeadAssignments.SingleAsync(a=>a.LeadId==s.Lead)).EndedAt);
    }

    [Fact]
    public async Task Concurrent_assignments_have_one_winner()
    {
        var s=await SeedAsync();
        async Task<bool> Attempt(string key)
        {
            await using var db=fixture.Create();
            try { await Control(db,s).AssignAsync(s.Lead,s.Chat,s.Agent,s.Admin,0,"Pilot",key,default); return true; }
            catch(AiSalesConflict) { return false; }
        }
        var results=await Task.WhenAll(Attempt("a"),Attempt("b")); Assert.Single(results, x=>x);
    }

    [Fact]
    public async Task Disabled_wrong_tenant_and_account_assignments_are_rejected()
    {
        var s=await SeedAsync(); await using var db=fixture.Create();
        await Assert.ThrowsAsync<AiSalesValidation>(()=>Control(db,s,false).AssignAsync(s.Lead,s.Chat,s.Agent,s.Admin,0,"Pilot","disabled",default));
        await Assert.ThrowsAsync<AiSalesValidation>(()=>Control(db,s with {Tenant=Guid.NewGuid()}).AssignAsync(s.Lead,s.Chat,s.Agent,s.Admin,0,"Pilot","tenant",default));
        var other=await SeedAsync();
        await Assert.ThrowsAsync<AiSalesValidation>(()=>Control(db,s).AssignAsync(other.Lead,other.Chat,s.Agent,s.Admin,0,"Pilot","account",default));
    }

    [Fact]
    public async Task Complete_intake_keeps_AI_assignment_and_never_calls_transport()
    {
        var s=await SeedAsync(true); await using var db=fixture.Create(); var control=Control(db,s);
        await control.AssignAsync(s.Lead,s.Chat,s.Agent,s.Admin,0,"Pilot","intake",default);
        var transport=new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        var intake=new AiIntakeService(db,transport.Object,Mock.Of<IInboxRealtimeNotifier>(),Mock.Of<INotificationService>(),NullLogger<AiIntakeService>.Instance,control);
        var chat=await db.WhatsAppConversations.Include(c=>c.Contact).Include(c=>c.WhatsAppAccount).SingleAsync(c=>c.Id==s.Chat);
        await using var gate=await ConversationLock.AcquireAsync(db,"conversation:"+s.Chat);
        await intake.CompleteAsync(chat,new LeadFormSetting(),default);
        Assert.Equal((byte)IntakeStatus.Completed,chat.IntakeStatus);
        Assert.Equal(ConversationControllerMode.Assisted,(await db.ConversationControls.FindAsync(s.Chat))!.Controller);
        Assert.Equal(s.Agent,(await db.Leads.FindAsync(s.Lead))!.AssignedAiSalesAgentId);
        transport.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Durable_webhook_survives_context_restart_and_deduplicates()
    {
        var payload="{\"entry\":[],\"test\":\""+Guid.NewGuid()+"\"}";
        await using(var first=fixture.Create()) await new DurableWorkService(first).AcceptWebhookAsync(payload,default);
        await using var restarted=fixture.Create(); await new DurableWorkService(restarted).AcceptWebhookAsync(payload,default);
        Assert.Single(await restarted.DurableWorkItems.Where(w=>w.Payload==payload).ToListAsync());
    }

    [Fact]
    public async Task Missing_or_invalid_webhook_signature_is_rejected_before_persistence()
    {
        await using var db=fixture.Create();
        var controller=new WhatsAppWebhookController(new DurableWorkService(db),new ConfigurationBuilder().Build(),NullLogger<WhatsAppWebhookController>.Instance);
        controller.ControllerContext=new(){HttpContext=new DefaultHttpContext()};
        controller.Request.Body=new MemoryStream(Encoding.UTF8.GetBytes("{\"entry\":[]}"));
        Assert.IsType<UnauthorizedResult>(await controller.Receive(default));
    }

    [Fact]
    public async Task Signed_webhook_is_acknowledged_only_after_durable_receipt()
    {
        await using var db=fixture.Create(); var body="{\"entry\":[],\"signed\":\""+Guid.NewGuid()+"\"}";
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"WhatsApp:AppSecret","synthetic-secret"}}).Build();
        var controller=new WhatsAppWebhookController(new DurableWorkService(db),config,NullLogger<WhatsAppWebhookController>.Instance);
        controller.ControllerContext=new(){HttpContext=new DefaultHttpContext()};
        controller.Request.Body=new MemoryStream(Encoding.UTF8.GetBytes(body));
        controller.Request.Headers["X-Hub-Signature-256"]="sha256="+Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("synthetic-secret"),Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        Assert.IsType<OkResult>(await controller.Receive(default));
        Assert.True(await db.DurableWorkItems.AnyAsync(w=>w.Payload==body));
    }

    [Fact]
    public void Passwords_upgrade_without_accepting_stored_hash_as_password()
    {
        Assert.True(PasswordSecurity.Verify("legacy-test", "legacy-test",out var upgrade)); Assert.True(upgrade);
        var hashed=PasswordSecurity.Hash("new-test"); Assert.DoesNotContain("new-test",hashed);
        Assert.True(PasswordSecurity.Verify(hashed,"new-test",out upgrade)); Assert.False(upgrade);
        Assert.False(PasswordSecurity.Verify(hashed,hashed,out _));
        Assert.False(PasswordSecurity.Verify(hashed,"wrong",out _));
    }

    [Fact]
    public async Task Unrelated_team_cannot_see_AI_owned_conversation()
    {
        var s=await SeedAsync();await using var db=fixture.Create();await Control(db,s).AssignAsync(s.Lead,s.Chat,s.Agent,s.Admin,0,"Pilot","scope",default);
        var otherTeam=new WhatsAppVisibility(new(){Guid.NewGuid()},Guid.NewGuid(),teamAccountIds:new(){(await db.WhatsAppConversations.FindAsync(s.Chat))!.WhatsAppAccountId});
        Assert.False(await db.WhatsAppConversations.ApplyVisibility(otherTeam).AnyAsync(c=>c.Id==s.Chat));
    }

    [Fact]
    public async Task Database_rejects_simultaneous_human_and_AI_ownership()
    {
        var s=await SeedAsync();await using var db=fixture.Create();var lead=await db.Leads.FindAsync(s.Lead);lead!.AssignedAiSalesAgentId=s.Agent;
        await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
    }

    [Fact]
    public async Task Rotation_cursor_rolls_back_with_failed_assignment()
    {
        var s=await SeedAsync(); var scope="test:"+Guid.NewGuid().ToString("N");
        await using(var db=fixture.Create())
        {
            await using var tx=await db.Database.BeginTransactionAsync();
            Assert.Equal(s.Human,await LeadDistributor.NextTeleSalesAsync(db,db.Users.Where(u=>u.Id==s.Human),scope));
            await tx.RollbackAsync();
        }
        await using var verify=fixture.Create();
        Assert.False(await verify.LeadRotationStates.AnyAsync(x=>x.ScopeKey==scope));
    }

    [Fact]
    public async Task Concurrent_rotation_preserves_weighted_human_turns()
    {
        var a=await SeedAsync();var b=await SeedAsync();var scope="test:"+Guid.NewGuid().ToString("N");
        await using(var setup=fixture.Create())
        {
            setup.LeadRotationMembers.AddRange(new LeadRotationMember {ScopeKey=scope,UserId=a.Human,Position=0,Share=2},new LeadRotationMember {ScopeKey=scope,UserId=b.Human,Position=1,Share=1});
            await setup.SaveChangesAsync();
        }
        async Task<Guid?> Pick()
        {
            await using var db=fixture.Create();await using var tx=await db.Database.BeginTransactionAsync();
            var result=await LeadDistributor.NextTeleSalesAsync(db,db.Users.Where(u=>u.Id==a.Human||u.Id==b.Human),scope);
            await tx.CommitAsync();return result;
        }
        var results=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>Pick()));
        Assert.Equal(8,results.Count(x=>x==a.Human));Assert.Equal(4,results.Count(x=>x==b.Human));
        Assert.DoesNotContain(a.Agent,results);Assert.DoesNotContain(b.Agent,results);
    }

    [Fact]
    public async Task Agent_pause_invalidates_controls_and_requires_explicit_resume()
    {
        var s=await SeedAsync(true);await using var db=fixture.Create();var controls=Control(db,s);
        await controls.AssignAsync(s.Lead,s.Chat,s.Agent,s.Admin,0,"Pilot","pause-assign",default);
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            await controls.PauseAgentConversationsAsync(s.Agent,s.Admin,default);
            await db.SaveChangesAsync();await tx.CommitAsync();
        }
        Assert.Equal(ConversationControllerMode.Paused,(await db.ConversationControls.FindAsync(s.Chat))!.Controller);
        Assert.Equal((byte)IntakeStatus.StoppedByStaff,(await db.WhatsAppConversations.FindAsync(s.Chat))!.IntakeStatus);
        await Assert.ThrowsAsync<AiSalesConflict>(()=>controls.ControlAsync(s.Chat,s.Admin,1,"resume","Stale resume","pause-resume",default));
    }

    [Fact]
    public async Task Durable_worker_retries_failures_and_completes_after_restart()
    {
        var payload="{\"entry\":[],\"retry\":\""+Guid.NewGuid()+"\"}";
        await using(var db=fixture.Create())await new DurableWorkService(db).AcceptWebhookAsync(payload,default);
        var processor=new Mock<IWhatsAppWebhookProcessor>();
        processor.Setup(p=>p.ProcessAsync(It.IsAny<Newtonsoft.Json.Linq.JObject>(),It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("synthetic failure"));
        var services=new ServiceCollection().AddDbContext<RecruitmentCrmContext>(o=>o.UseNpgsql(fixture.ConnectionString))
            .AddSingleton(processor.Object).BuildServiceProvider();
        using(services)
        {
            var worker=new DurableWorkWorker(services.GetRequiredService<IServiceScopeFactory>(),NullLogger<DurableWorkWorker>.Instance);
            await worker.RunBatchAsync(default);
        }
        await using(var db=fixture.Create())
        {
            var item=await db.DurableWorkItems.SingleAsync(w=>w.Payload==payload);
            Assert.Equal(1,item.Attempts);Assert.Null(item.CompletedAt);
            item.AvailableAt=DateTime.UtcNow.AddSeconds(-1);await db.SaveChangesAsync();
        }
        processor.Setup(p=>p.ProcessAsync(It.IsAny<Newtonsoft.Json.Linq.JObject>(),It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        using var restarted=new ServiceCollection().AddDbContext<RecruitmentCrmContext>(o=>o.UseNpgsql(fixture.ConnectionString)).AddSingleton(processor.Object).BuildServiceProvider();
        await new DurableWorkWorker(restarted.GetRequiredService<IServiceScopeFactory>(),NullLogger<DurableWorkWorker>.Instance).RunBatchAsync(default);
        await using var verify=fixture.Create();Assert.False(await verify.DurableWorkItems.AnyAsync(w=>w.Payload==payload));
    }

    [Fact]
    public async Task Release_does_not_restore_a_former_owner_who_moved_teams()
    {
        var s = await SeedAsync(); await using var db = fixture.Create(); var control = Control(db, s);
        await control.AssignAsync(s.Lead, s.Chat, s.Agent, s.Admin, 0, "Pilot", "move-assign", default);
        (await db.Users.FindAsync(s.Human))!.ManagerId = s.Admin;
        await db.SaveChangesAsync();
        await control.ControlAsync(s.Chat, s.Admin, 1, "release", "Moved team", "move-release", default);
        Assert.Null((await db.Leads.FindAsync(s.Lead))!.AssignedSalesId);
        Assert.Null((await db.WhatsAppConversations.FindAsync(s.Chat))!.AssignedSalesAgentId);
    }

    [Fact]
    public async Task Queued_intake_waits_for_takeover_and_does_not_send_after_it()
    {
        var s = await SeedAsync(true);
        await using var owner = fixture.Create(); var control = Control(owner, s);
        await control.AssignAsync(s.Lead, s.Chat, s.Agent, s.Admin, 0, "Pilot", "race-assign", default);
        await using var replyDb = fixture.Create();
        var transport = new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        var intake = new AiIntakeService(replyDb, transport.Object, Mock.Of<IInboxRealtimeNotifier>(), Mock.Of<INotificationService>(), NullLogger<AiIntakeService>.Instance, Control(replyDb, s));
        Task reply;
        await using (var gate = await ConversationLock.AcquireAsync(owner, "conversation:" + s.Chat))
        {
            reply = intake.ReplyAsync(s.Chat, default);
            Assert.False(reply.IsCompleted);
            await control.TakeOverLockedAsync((await owner.WhatsAppConversations.FindAsync(s.Chat))!, s.Admin, "Staff send", default);
            await owner.SaveChangesAsync();
        }
        await reply.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal((byte)IntakeStatus.StoppedByStaff, (await replyDb.WhatsAppConversations.FindAsync(s.Chat))!.IntakeStatus);
        transport.VerifyNoOtherCalls();
    }
}
