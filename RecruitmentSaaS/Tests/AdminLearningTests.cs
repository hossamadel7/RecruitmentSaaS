using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.TestHost;
using Moq;
using Newtonsoft.Json.Linq;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    private static AiAdminLearningOptions AdminOptions(Guid account) => new() { Enabled = true, PrivateRoutingEnabled = true, ApprovedAccountIds = [account] };
    private static AiAdminLearningService AdminService(RecruitmentCrmContext db, Seed s, AiAdminLearningOptions options, IWhatsAppCloudApiService? transport = null) =>
        new(db, Control(db,s), Options.Create(options), transport ?? Mock.Of<IWhatsAppCloudApiService>(MockBehavior.Strict));
    private static JObject AdminInbound(string phone, string text, string? reply = null) => new() { ["from"] = phone, ["id"] = "wamid."+Guid.NewGuid(),
        ["type"] = "text", ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ["text"] = new JObject { ["body"] = text },
        ["context"] = reply == null ? null : new JObject { ["id"] = reply } };
    private async Task<(Seed S, Guid Account, Guid Channel, AiAdminLearningOptions Options)> AdminSeedAsync()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s, false);
        await using var db = fixture.Create(); var account = await db.WhatsAppConversations.Where(c => c.Id == s.Chat).Select(c => c.WhatsAppAccountId).SingleAsync();
        var options = AdminOptions(account); var service = AdminService(db,s,options);
        var bound = await service.BindAsync(account,s.Admin,"201099900011",default);
        await service.ReceiveAsync(account,AdminInbound(bound.Channel.Phone,bound.Code),default);
        return (s,account,bound.Channel.Id,options);
    }
    [Fact]
    public async Task Inbox_AI_transfer_supports_unlinked_chats_without_creating_leads_and_releases_safely()
    {
        var s=await SeedAsync();await using var db=fixture.Create();var chat=(await db.WhatsAppConversations.FindAsync(s.Chat))!;
        chat.LeadId=null;await db.SaveChangesAsync();var count=await db.Leads.CountAsync();var controls=Control(db,s);
        var assigned=await controls.AssignAsync(null,s.Chat,s.Agent,s.Admin,0,"Inbox conversation transfer","unlinked-inbox",default,true);
        Assert.Equal(assigned,await controls.AssignAsync(null,s.Chat,s.Agent,s.Admin,0,"Inbox conversation transfer","unlinked-inbox",default,true));
        Assert.Null((await db.AiLeadAssignments.SingleAsync(a=>a.ConversationId==s.Chat)).LeadId);
        Assert.Equal(count,await db.Leads.CountAsync());Assert.Equal(s.Human,(await db.Leads.FindAsync(s.Lead))!.AssignedSalesId);
        await controls.ControlAsync(s.Chat,s.Admin,1,"release","Restore chat","unlinked-release",default);
        Assert.Null(chat.AssignedAiSalesAgentId);Assert.Equal(s.Human,chat.AssignedSalesAgentId);
    }
    [Fact]
    public async Task Admin_channel_requires_proof_of_phone_and_never_creates_customer_records_even_when_paused()
    {
        var s=await SeedAsync();await using var db=fixture.Create();var account=await db.WhatsAppConversations.Where(c=>c.Id==s.Chat).Select(c=>c.WhatsAppAccountId).SingleAsync();
        var options=AdminOptions(account);var service=AdminService(db,s,options);var bound=await service.BindAsync(account,s.Admin,"+201099900012",default);
        var count=await db.WhatsAppConversations.CountAsync();var leads=await db.Leads.CountAsync();
        await service.ReceiveAsync(account,AdminInbound(bound.Channel.Phone,"wrong-code"),default);Assert.Null(bound.Channel.VerifiedAt);
        var payload=AdminInbound(bound.Channel.Phone,bound.Code);await service.ReceiveAsync(account,payload,default);await service.ReceiveAsync(account,payload,default);
        Assert.NotNull(bound.Channel.VerifiedAt);Assert.Equal(2,await db.AiAdminMessages.CountAsync(m=>m.ChannelId==bound.Channel.Id&&m.Incoming));
        Assert.DoesNotContain(bound.Code,string.Join(" ",await db.AiAdminMessages.Where(m=>m.ChannelId==bound.Channel.Id).Select(m=>m.Text).ToArrayAsync()));
        bound.Channel.Paused=true;options.Enabled=false;await db.SaveChangesAsync();
        Assert.True(await service.ReceiveAsync(account,AdminInbound(bound.Channel.Phone,"private"),default));
        Assert.False(await service.ReceiveAsync(account,AdminInbound("201099988877","I am admin"),default));
        Assert.Equal(count,await db.WhatsAppConversations.CountAsync());Assert.Equal(leads,await db.Leads.CountAsync());
        Assert.False(await service.IsPrivateAsync(Guid.NewGuid(),bound.Channel.Phone,default));
    }
    [Fact]
    public async Task Admin_code_copied_with_direction_marks_or_extra_text_still_verifies_and_chat_does_not_use_attempts()
    {
        var s=await SeedAsync();await using var db=fixture.Create();var account=await db.WhatsAppConversations.Where(c=>c.Id==s.Chat).Select(c=>c.WhatsAppAccountId).SingleAsync();
        var options=AdminOptions(account);var service=AdminService(db,s,options);var bound=await service.BindAsync(account,s.Admin,"+201099900013",default);
        for(var i=0;i<6;i++)await service.ReceiveAsync(account,AdminInbound(bound.Channel.Phone,"Test"),default);
        Assert.Equal(0,bound.Channel.VerificationAttempts);Assert.Null(bound.Channel.VerifiedAt);
        // As pasted on a phone from the Arabic page: right-to-left marks, the sentence around it and lower case.
        await service.ReceiveAsync(account,AdminInbound(bound.Channel.Phone,"‏الرمز: "+bound.Code.ToLowerInvariant()+"‏."),default);
        Assert.NotNull(bound.Channel.VerifiedAt);Assert.Equal(1,bound.Channel.VerificationAttempts);
        Assert.DoesNotContain(bound.Code,string.Join(" ",await db.AiAdminMessages.Where(m=>m.ChannelId==bound.Channel.Id).Select(m=>m.Text).ToArrayAsync()),StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task One_linked_Admin_number_receives_questions_from_every_company_number()
    {
        var (s,account,channel,options)=await AdminSeedAsync();await using var db=fixture.Create();
        // The customer writes to a second company number that has no Admin channel of its own.
        var second=new WhatsAppAccount{Id=Guid.NewGuid(),Name="Second company number",PhoneNumberId=Guid.NewGuid().ToString(),WabaId="test",DisplayPhoneNumber="201888888888",IsActive=true};
        db.WhatsAppAccounts.Add(second);(await db.WhatsAppConversations.FindAsync(s.Chat))!.WhatsAppAccountId=second.Id;await db.SaveChangesAsync();
        var service=AdminService(db,s,options);
        Assert.Null(await service.AskAsync(s.Chat,null,s.Admin,default)); // only numbers approved for Admin coaching are routed
        options.ApprovedAccountIds=[account,second.Id];
        var request=(await service.AskAsync(s.Chat,null,s.Admin,default))!;
        Assert.Equal(channel,request.ChannelId);
    }
    [Fact]
    public async Task AskAdmin_correlates_out_of_order_answers_and_never_guesses_between_pending_questions()
    {
        var (s,account,channel,options)=await AdminSeedAsync();await using var db=fixture.Create();var service=AdminService(db,s,options);
        var one=(await service.AskAsync(s.Chat,null,s.Admin,default))!;
        var chat=await db.WhatsAppConversations.AsNoTracking().SingleAsync(c=>c.Id==s.Chat);
        db.WhatsAppMessages.Add(new(){Id=Guid.NewGuid(),ConversationId=s.Chat,WhatsAppAccountId=account,Direction=1,MessageType=1,TextBody="سؤال آخر",CreatedAt=DateTime.UtcNow.AddSeconds(1),WhatsAppTimestamp=DateTime.UtcNow});await db.SaveChangesAsync();
        var two=(await service.AskAsync(s.Chat,null,s.Admin,default))!;Assert.NotEqual(one.Id,two.Id);
        await service.ReceiveAsync(account,AdminInbound("201099900011","yes"),default);Assert.Equal(AiAdminRequestState.Queued,one.State);Assert.Equal(AiAdminRequestState.Queued,two.State);
        await service.ReceiveAsync(account,AdminInbound("201099900011",two.Code+" لهذا العميل فقط"),default);
        Assert.Equal(AiAdminRequestState.AnswerReceived,two.State);Assert.Equal(AiAdminRequestState.Queued,one.State);
        await service.QueueQuestionsAsync(channel,default);
        var question=await db.AiAdminMessages.SingleAsync(m=>m.OperationKey=="question:"+one.Id);question.State="Sent";question.WhatsAppMessageId="wamid.question-test";await db.SaveChangesAsync();
        await service.ReceiveAsync(account,AdminInbound("201099900011","الإجابة الأولى",question.WhatsAppMessageId),default);
        Assert.Equal(AiAdminRequestState.AnswerReceived,one.State);Assert.Equal("الإجابة الأولى",one.Answer);
        Assert.Empty(await db.AiSalesKnowledge.Where(k=>k.TenantId==s.Tenant&&k.SourceReference.StartsWith("ADMIN ")).ToArrayAsync());
    }
    [Fact]
    public async Task Admin_answer_is_customer_scoped_requires_explicit_publication_and_prepares_only_a_review_draft_after_restart()
    {
        var (s,account,channel,options)=await AdminSeedAsync();Guid request;
        await using(var db=fixture.Create()){
            var service=AdminService(db,s,options);var r=(await service.AskAsync(s.Chat,null,s.Admin,default))!;request=r.Id;
            await service.ReceiveAsync(account,AdminInbound("201099900011",r.Code+" موافقة للعميل ده بس"),default);
            await Assert.ThrowsAsync<AiSalesValidation>(()=>service.ValidateAnswerAsync(r.Id,s.Admin,r.Version,"استثناء خاص",AiAdminAnswerScope.CustomerOnly,DateTime.UtcNow.AddDays(1),true,"سياسة",null,default));
            await service.ValidateAnswerAsync(r.Id,s.Admin,r.Version,"استثناء خاص لهذا العميل فقط",AiAdminAnswerScope.CustomerOnly,DateTime.UtcNow.AddDays(1),false,"",null,default);
        }
        await using(var db=fixture.Create()){
            var context=new AiSalesContextService(db,null,Options.Create(options));var chat=await db.WhatsAppConversations.SingleAsync(c=>c.Id==s.Chat);
            var read=await context.ReadAsync(s.Tenant,chat,default);Assert.Contains(read.Knowledge,k=>k.Id==request);
            var provider=new Mock<IAiSalesDraftProvider>(MockBehavior.Strict);provider.Setup(p=>p.GenerateAsync(It.IsAny<AiSalesContext>(),It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AiDraftProposal("تمت الموافقة على الاستثناء لحضرتك فقط.","answer","Reviewed exception",false,[request],"synthetic",1,1));
            var service=AdminService(db,s,options);var drafts=new AiSalesDraftService(db,Control(db,s),context,provider.Object,Options.Create(new AiSalesEngineOptions{DraftsEnabled=true,ExternalProcessingApproved=true}),service);
            var resume=new AiAdminDraftResumer(db,service,drafts);var result=await resume.PrepareAsync(request,default);
            Assert.Equal(AiAdminRequestState.ReviewReady,result.State);var draft=await db.AiSalesDrafts.SingleAsync(d=>d.Id==result.ReplyDraftId);
            Assert.Equal(AiDraftStatus.ReviewRequired,draft.Status);Assert.Null(draft.ReviewedById);Assert.True(await drafts.IsCurrentAsync(draft,default));
            Assert.Equal(result.ReplyDraftId,(await resume.PrepareAsync(request,default)).ReplyDraftId);
            Assert.Empty(await db.AiSalesKnowledge.Where(k=>k.TenantId==s.Tenant&&k.SourceReference.StartsWith("ADMIN ")).ToArrayAsync());
            Assert.False(await db.WhatsAppMessages.AnyAsync(m=>m.ConversationId==s.Chat&&m.Direction==2));Assert.False(await db.AiOutboundIntents.AnyAsync(i=>i.ConversationId==s.Chat));
        }
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Late_admin_answer_never_prepares_a_customer_reply_after_takeover_or_new_message(bool takeover)
    {
        var (s,account,_,options)=await AdminSeedAsync();await using var db=fixture.Create();var service=AdminService(db,s,options);var r=(await service.AskAsync(s.Chat,null,s.Admin,default))!;
        await service.ValidateAnswerAsync(r.Id,s.Admin,r.Version,"إجابة محددة",AiAdminAnswerScope.CustomerOnly,DateTime.UtcNow.AddDays(1),false,"",null,default);
        if(takeover)await Control(db,s).ControlAsync(s.Chat,s.Admin,1,"takeover","Staff owns it","takeover-admin",default);
        else {db.WhatsAppMessages.Add(new(){Id=Guid.NewGuid(),ConversationId=s.Chat,WhatsAppAccountId=account,Direction=1,MessageType=1,TextBody="سؤال جديد",CreatedAt=DateTime.UtcNow.AddSeconds(2),WhatsAppTimestamp=DateTime.UtcNow});await db.SaveChangesAsync();}
        var resumer=new AiAdminDraftResumer(db,service,Engine(db,s,new Mock<IAiSalesDraftProvider>(MockBehavior.Strict).Object));
        var result=await resumer.PrepareAsync(r.Id,default);Assert.Equal(AiAdminRequestState.Cancelled,result.State);Assert.Null(result.ReplyDraftId);
    }
    [Fact]
    public async Task Admin_notification_uses_template_outside_window_only_once_and_never_retries_unknown_transport()
    {
        var (s,account,channel,options)=await AdminSeedAsync();options.OutboundApproved=true;options.NotificationTemplate="synthetic_admin_notice";
        await using var db=fixture.Create();var transport=new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        transport.Setup(t=>t.SendTemplateMessageAsync(It.IsAny<string>(),"201099900011",options.NotificationTemplate,"ar",It.IsAny<IReadOnlyList<string>>(),It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("uncertain"));
        var service=AdminService(db,s,options,transport.Object);await service.AskAsync(s.Chat,null,s.Admin,default);
        var c=await db.AiAdminChannels.SingleAsync(c=>c.Id==channel);c.LastInboundAt=DateTime.UtcNow.AddDays(-2);await db.SaveChangesAsync();
        await service.QueueQuestionsAsync(channel,default);await service.QueueQuestionsAsync(channel,default);
        var notice=await db.AiAdminMessages.SingleAsync(m=>m.ChannelId==channel&&m.IsTemplate);await service.DeliverAsync(notice.Id,default);await service.DeliverAsync(notice.Id,default);
        Assert.Equal("Unknown",notice.State);transport.Verify(t=>t.SendTemplateMessageAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<IReadOnlyList<string>>(),It.IsAny<CancellationToken>()),Times.Once);
        transport.Verify(t=>t.SendTextMessageAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>()),Times.Never);
        notice.WhatsAppMessageId="wamid.status-scope";await db.SaveChangesAsync();
        var status=new JObject{["id"]=notice.WhatsAppMessageId,["status"]="delivered"};
        Assert.False(await service.StatusAsync(Guid.NewGuid(),status,default));
        Assert.True(await service.StatusAsync(account,status,default));Assert.Equal("Delivered",notice.State);
    }
    [Fact]
    public async Task Reviewed_admin_correction_retires_old_fact_and_timeout_remains_visible_for_human_followup()
    {
        var (s,account,_,options)=await AdminSeedAsync();await using var db=fixture.Create();var service=AdminService(db,s,options);var r=(await service.AskAsync(s.Chat,null,s.Admin,default))!;
        var old=new AiSalesKnowledge{TenantId=s.Tenant,WhatsAppAccountId=account,Status=AiKnowledgeStatus.Approved,Title="Old policy",Content="Old",SourceReference="Synthetic source",ApprovedById=s.Admin,ApprovedAt=DateTime.UtcNow,ValidFrom=DateTime.UtcNow.AddDays(-1),ValidUntil=DateTime.UtcNow.AddDays(1)};db.Add(old);await db.SaveChangesAsync();
        await service.ValidateAnswerAsync(r.Id,s.Admin,r.Version,"السياسة المصححة",AiAdminAnswerScope.Correction,DateTime.UtcNow.AddDays(7),true,"سياسة مصححة",old.Id,default);
        Assert.Equal(AiKnowledgeStatus.Retired,old.Status);Assert.NotNull(r.KnowledgeId);Assert.Equal(AiKnowledgeStatus.Approved,(await db.AiSalesKnowledge.FindAsync(r.KnowledgeId))!.Status);
        var other=await SeedAsync();await EngineSeedAsync(other); // separate isolated account request
        var otherAccount=await db.WhatsAppConversations.Where(c=>c.Id==other.Chat).Select(c=>c.WhatsAppAccountId).SingleAsync();var otherOptions=AdminOptions(otherAccount);var otherService=AdminService(db,other,otherOptions);
        var bound=await otherService.BindAsync(otherAccount,other.Admin,"201099900015",default);await otherService.ReceiveAsync(otherAccount,AdminInbound(bound.Channel.Phone,bound.Code),default);
        var pending=(await otherService.AskAsync(other.Chat,null,other.Admin,default))!;pending.Deadline=DateTime.UtcNow.AddHours(-1);await db.SaveChangesAsync();
        await new AiAdminDraftResumer(db,otherService,Engine(db,other,new Mock<IAiSalesDraftProvider>(MockBehavior.Strict).Object)).PrepareAsync(pending.Id,default);
        Assert.Equal(AiAdminRequestState.Escalated,pending.State);Assert.Equal("AdminDeadlineExceeded",pending.ErrorCode);
    }
    [Fact]
    public async Task Admin_learning_HTTP_requires_admin_CSRF_and_inbox_transfer_respects_account_and_versions()
    {
        var s=await SeedAsync();await using var app=await StartHttpAsync(s,adminCoaching:true);using var client=app.GetTestClient();
        await Authenticate(client,s.Human,"7");Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/ai-sales/conversations/"+s.Chat+"/transfer-options")).StatusCode);
        await Authenticate(client,s.Admin,"1");var options=await client.GetFromJsonAsync<JsonElement>("/api/ai-sales/conversations/"+s.Chat+"/transfer-options");Assert.Equal(0,options.GetProperty("version").GetInt64());
        var token=client.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/ai-sales/conversations/"+s.Chat+"/assign",new{agentId=s.Agent,expectedVersion=0})).StatusCode);
        client.DefaultRequestHeaders.Add("RequestVerificationToken",token);client.DefaultRequestHeaders.Add("Idempotency-Key","inbox-http-transfer");
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/ai-sales/conversations/"+s.Chat+"/assign",new{agentId=s.Agent,expectedVersion=0})).StatusCode);
        await using var db=fixture.Create();Assert.Equal(s.Human,(await db.Leads.FindAsync(s.Lead))!.AssignedSalesId);
        await Authenticate(client,s.Human,"7");Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/ai-admin-learning/status?accountId="+Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Missing_facts_enqueue_one_private_request_without_provider_or_customer_send()
    {
        var (s,_,_,options)=await AdminSeedAsync();await using var db=fixture.Create();var service=AdminService(db,s,options);
        var provider=new Mock<IAiSalesDraftProvider>(MockBehavior.Strict);
        var drafts=new AiSalesDraftService(db,Control(db,s),new AiSalesContextService(db,null,Options.Create(options)),provider.Object,
            Options.Create(new AiSalesEngineOptions{DraftsEnabled=true}),service);
        var draft=await drafts.GenerateAsync(s.Chat,s.Admin,1,"gap-example",default);
        Assert.True(draft.NeedsHuman);var r=await db.AiAdminClarifications.SingleAsync(r=>r.TenantId==s.Tenant);
        Assert.Equal(r.Id,(await service.AskAsync(s.Chat,null,s.Admin,default))!.Id);
        Assert.False(await db.WhatsAppMessages.AnyAsync(m=>m.ConversationId==s.Chat&&m.Direction==2));
        provider.Verify(p=>p.GenerateAsync(It.IsAny<AiSalesContext>(),It.IsAny<CancellationToken>()),Times.Never);
    }
    [Fact]
    public async Task Private_admin_webhook_routes_before_customer_creation_assignment_echoes_and_history()
    {
        var (s,account,channel,options)=await AdminSeedAsync();await using var db=fixture.Create();var service=AdminService(db,s,options);
        var a=await db.WhatsAppAccounts.SingleAsync(a=>a.Id==account);var conversations=await db.WhatsAppConversations.CountAsync();var messages=await db.WhatsAppMessages.CountAsync();
        var processor=new WhatsAppWebhookProcessor(db,Mock.Of<IInboxRealtimeNotifier>(),Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppWebhookProcessor>.Instance,
            Mock.Of<IWhatsAppCloudApiService>(MockBehavior.Strict),Mock.Of<INotificationService>(),null!,null!,Control(db,s),null!,service);
        var value=new JObject{["metadata"]=new JObject{["phone_number_id"]=a.PhoneNumberId},["messages"]=new JArray(AdminInbound("201099900011","إيقاف التدريب")),
            ["message_echoes"]=new JArray(new JObject{["id"]="wamid.private-echo",["to"]="201099900011",["type"]="text",["text"]=new JObject{["body"]="Private reply"}}),
            ["history"]=new JArray(new JObject{["threads"]=new JArray(new JObject{["id"]="201099900011",["messages"]=new JArray(AdminInbound("201099900011","Private history"))})})};
        await processor.ProcessAsync(new JObject{["entry"]=new JArray(new JObject{["changes"]=new JArray(new JObject{["field"]="history",["value"]=value})})});
        Assert.Equal(conversations,await db.WhatsAppConversations.CountAsync());Assert.Equal(messages,await db.WhatsAppMessages.CountAsync());
        Assert.True((await db.AiAdminChannels.SingleAsync(c=>c.Id==channel)).Paused);
    }
}
