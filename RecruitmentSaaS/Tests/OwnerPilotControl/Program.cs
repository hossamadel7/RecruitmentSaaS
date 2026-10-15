using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;

// Private deployment helper, excluded from the web app. Input arrives via stdin;
// there is no login, hosted worker, model provider or customer messaging transport.
try
{
    var request = JsonSerializer.Deserialize<OwnerAssignment>(await Console.In.ReadToEndAsync())
        ?? throw new InvalidOperationException();
    if (!request.OwnerApproved || string.IsNullOrWhiteSpace(request.SourceReference) || request.Command is not ("assign" or "release")) throw new InvalidOperationException();
    AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    await using var db = new RecruitmentCrmContext(new DbContextOptionsBuilder<RecruitmentCrmContext>()
        .UseNpgsql(request.ConnectionString).Options);
    var agent = await db.AiSalesAgents.AsNoTracking().SingleAsync(a => a.Id == request.AgentId && a.TenantId == request.TenantId);
    var chat = await db.WhatsAppConversations.AsNoTracking().SingleAsync(c => c.Id == request.ConversationId && c.LeadId == request.LeadId);
    if (agent.WhatsAppAccountId != chat.WhatsAppAccountId) throw new InvalidOperationException();
    var controls = new AiSalesControlService(db, Options.Create(new AiSalesOptions { Enabled = true, TenantId = request.TenantId }));
    var result = request.Command == "release"
        ? await controls.ControlAsync(request.ConversationId, null, request.ExpectedVersion, "release", request.SourceReference, request.Key, default, request.SourceReference)
        : await controls.AssignAsync(request.LeadId, request.ConversationId, request.AgentId, null, 0,
            "Owner selected this conversation for the supervised pilot", request.Key, default,
            conversationOnly: true, sourceReference: request.SourceReference);
    Console.WriteLine(JsonSerializer.Serialize(new { result.ConversationId, result.OwnershipVersion, Operation = request.Command,
        ConversationOnly = request.Command == "assign" ? (bool?)true : null, UserImpersonated = false }));
}
catch (Exception ex)
{
    // No SQL, credentials, customer text or exception bodies are printed.
    Console.Error.WriteLine("Owner assignment failed: " + ex.GetType().Name);
    Environment.ExitCode = 1;
}

sealed record OwnerAssignment(string ConnectionString, Guid TenantId, Guid LeadId, Guid ConversationId,
    Guid AgentId, string Key, string SourceReference, bool OwnerApproved, string Command = "assign", long ExpectedVersion = 0);
