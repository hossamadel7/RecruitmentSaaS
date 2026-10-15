using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Diagnostics;
using System.Text.Json;

namespace RecruitmentSaaS.Tests;

public sealed class SyntheticSalesPack
{
    public int Version { get; set; }
    public bool SyntheticOnly { get; set; }
    public SyntheticKnowledge[] Knowledge { get; set; } = [];
    public SyntheticSalesCase[] Cases { get; set; } = [];
}
public sealed class SyntheticKnowledge
{
    public string Key { get; set; } = "";
    public AiKnowledgeKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string SourceReference { get; set; } = "";
    public int ValidFromDaysAgo { get; set; }
    public int ValidForDays { get; set; }
}
public sealed class SyntheticSalesCase
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public SyntheticCustomer Customer { get; set; } = new();
    public SyntheticMessage[] Messages { get; set; } = [];
    public string[] KnowledgeKeys { get; set; } = [];
    public Dictionary<string, SyntheticOverride> KnowledgeOverrides { get; set; } = [];
    public string? DuringGeneration { get; set; }
    public SyntheticExpectation Expected { get; set; } = new();
}
public sealed class SyntheticCustomer
{
    public string Name { get; set; } = "";
    public byte? Age { get; set; }
    public string? Profession { get; set; }
    public string ReviewedSummary { get; set; } = "";
}
public sealed class SyntheticMessage
{
    public string Speaker { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Text { get; set; }
}
public sealed class SyntheticOverride { public string Status { get; set; } = ""; }
public sealed class SyntheticExpectation
{
    public string[] Intents { get; set; } = [];
    public string[] Must { get; set; } = [];
    public string[] MustNot { get; set; } = [];
    public string[] FactCitationsAllowed { get; set; } = [];
    public string[] FactCitationsRequired { get; set; } = [];
    public bool? NeedsHuman { get; set; }
    public bool? ProviderCallExpected { get; set; }
    public string? DraftStatus { get; set; }
}
public sealed record SyntheticReplayResult(string CaseId, int Repeat, bool StructuralPassed, string[] Checks, string? FailureCode,
    string Path, string DraftStatus, string Intent, int ProviderCalls, double ElapsedMilliseconds, string Reply,
    string[] SourceKeys, int? SimulatedInputTokens, int? SimulatedOutputTokens, string SemanticAssessment, string[] HumanReviewExpectations,
    string[] HumanReviewProhibitions);

/// <summary>Not part of the deployed application. Uses only a disposable test fixture and a fake provider.</summary>
public static class SyntheticReplayHarness
{
    public static SyntheticSalesPack Load(string path)
    {
        if (new FileInfo(path).Length > 262144) throw new InvalidOperationException("SyntheticPackTooLarge");
        var pack = JsonSerializer.Deserialize<SyntheticSalesPack>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("InvalidSyntheticPack");
        if (!pack.SyntheticOnly || pack.Version != 1 || pack.Cases.Length is < 1 or > 100 || pack.Knowledge.Length > 100 ||
            pack.Cases.Select(c => c.Id).Distinct().Count() != pack.Cases.Length || pack.Knowledge.Select(k => k.Key).Distinct().Count() != pack.Knowledge.Length)
            throw new InvalidOperationException("InvalidSyntheticPack");
        var keys = pack.Knowledge.Select(k => k.Key).ToHashSet();
        foreach (var c in pack.Cases)
            if (string.IsNullOrWhiteSpace(c.Id) || c.Id.Length > 40 || c.Messages.Length is < 1 or > 20 || c.KnowledgeKeys.Length > 8 ||
                c.KnowledgeKeys.Distinct().Count() != c.KnowledgeKeys.Length || c.KnowledgeKeys.Any(k => !keys.Contains(k)) ||
                c.Expected.FactCitationsRequired.Any(k => !c.Expected.FactCitationsAllowed.Contains(k)) || c.Expected.FactCitationsAllowed.Any(k => !c.KnowledgeKeys.Contains(k) || pack.Knowledge.Single(x => x.Key == k).Kind != AiKnowledgeKind.Fact) ||
                c.Messages.Any(m => m.Speaker is not ("customer" or "staff") || m.Type is not ("Text" or "Audio") || m.Text?.Length > 600) ||
                c.KnowledgeOverrides.Any(o => !c.KnowledgeKeys.Contains(o.Key) || o.Value.Status != "RetiredOrExpired") ||
                c.DuringGeneration != null && c.Category != "takeover_race") throw new InvalidOperationException("InvalidSyntheticCase");
        if (pack.Knowledge.Any(k => k.Kind is not (AiKnowledgeKind.Fact or AiKnowledgeKind.Playbook) || k.Content.Length > 4000 || k.Title.Length > 150 ||
            k.SourceReference.Length > 500 || k.ValidForDays is < 1 or > 90 || k.ValidFromDaysAgo is < 0 or > 90)) throw new InvalidOperationException("InvalidSyntheticKnowledge");
        return pack;
    }

    public static async Task<SyntheticReplayResult> RunAsync(DatabaseFixture fixture, SyntheticSalesPack pack, SyntheticSalesCase scenario, int repeat)
    {
        await using var db = fixture.Create();
        var checks = new List<string>(); var timer = Stopwatch.StartNew(); string? failure = null;
        AiSalesDraft? draft = null; var provider = new ContractProvider();
        var tenant = Guid.NewGuid();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Synthetic replay only", IsActive = true };
        var admin = new User { Id = Guid.NewGuid(), Branch = branch, Role = 1, FullName = "Synthetic reviewer", Email = Guid.NewGuid() + "@example.test", PasswordHash = "unused", IsActive = true };
        var human = new User { Id = Guid.NewGuid(), Branch = branch, Role = 3, FullName = "Synthetic salesperson", Email = Guid.NewGuid() + "@example.test", PasswordHash = "unused", IsActive = true };
        var account = new WhatsAppAccount { Id = Guid.NewGuid(), PhoneNumberId = Guid.NewGuid().ToString("N"), WabaId = "synthetic", Name = "Synthetic replay; no transport", DisplayPhoneNumber = "201000000000", IsActive = true };
        var contact = new WhatsAppContact { Id = Guid.NewGuid(), Name = scenario.Customer.Name, WhatsAppPhoneNumber = Guid.NewGuid().ToString("N") };
        var lead = new Lead { Id = Guid.NewGuid(), Branch = branch, RegisteredBy = admin, AssignedSales = human, FullName = scenario.Customer.Name, Phone = contact.WhatsAppPhoneNumber,
            Age = scenario.Customer.Age, InterestedJobTitle = scenario.Customer.Profession, Status = 1, LeadSource = 5 };
        var chat = new WhatsAppConversation { Id = Guid.NewGuid(), Contact = contact, Lead = lead, WhatsAppAccount = account, AssignedSalesAgent = human, IntakeStatus = (byte)IntakeStatus.Completed };
        var agent = new AiSalesAgent { Id = Guid.NewGuid(), TenantId = tenant, WhatsAppAccountId = account.Id, Mode = AiSalesMode.Shadow, Paused = false };
        db.AddRange(admin, human, chat, agent); await db.SaveChangesAsync();
        var foundation = Options.Create(new AiSalesOptions { Enabled = true, TenantId = tenant });
        var controls = new AiSalesControlService(db, foundation);
        await controls.AssignAsync(lead.Id, chat.Id, agent.Id, admin.Id, 0, "Isolated synthetic replay", "replay-assign", default);
        db.AiCustomerMemories.Add(new() { TenantId = tenant, ConversationId = chat.Id, ReviewedById = admin.Id, ReviewedSummary = scenario.Customer.ReviewedSummary });
        var knowledge = new Dictionary<string, AiSalesKnowledge>();
        foreach (var key in scenario.KnowledgeKeys)
        {
            var input = pack.Knowledge.Single(k => k.Key == key); var retired = scenario.KnowledgeOverrides.ContainsKey(key);
            var item = new AiSalesKnowledge { TenantId = tenant, WhatsAppAccountId = account.Id, Kind = input.Kind, Status = retired ? AiKnowledgeStatus.Retired : AiKnowledgeStatus.Approved,
                Title = input.Title, Content = input.Content, SourceReference = input.SourceReference, CreatedById = admin.Id, ApprovedById = admin.Id, ApprovedAt = DateTime.UtcNow,
                ValidFrom = DateTime.UtcNow.AddDays(retired ? -3 : -input.ValidFromDaysAgo), ValidUntil = DateTime.UtcNow.AddDays(retired ? -1 : input.ValidForDays) };
            knowledge[key] = item; db.AiSalesKnowledge.Add(item);
        }
        var i = 0;
        foreach (var message in scenario.Messages)
        {
            var at = DateTime.UtcNow.AddMinutes(-1).AddSeconds(i++);
            db.WhatsAppMessages.Add(new() { Id = Guid.NewGuid(), ConversationId = chat.Id, WhatsAppAccountId = account.Id,
                Direction = message.Speaker == "customer" ? (byte)MessageDirection.Incoming : (byte)MessageDirection.Outgoing,
                MessageType = message.Type == "Text" ? (byte)WhatsAppMessageType.Text : (byte)WhatsAppMessageType.Audio,
                TextBody = message.Type == "Text" ? message.Text : null, MediaId = message.Type == "Audio" ? "123456" : null,
                MessageSource = message.Speaker == "customer" ? (byte)MessageSource.Customer : (byte)MessageSource.WhatsAppBusinessApp,
                Status = message.Speaker == "customer" ? (byte)MessageStatus.Received : (byte)MessageStatus.Sent, CreatedAt = at, WhatsAppTimestamp = at });
        }
        await db.SaveChangesAsync();
        var reader = new AiSalesContextService(db); var context = await reader.ReadAsync(tenant, chat, default);
        var sources = scenario.Expected.FactCitationsAllowed.Where(k => knowledge[k].Status == AiKnowledgeStatus.Approved).Select(k => knowledge[k].Id).ToArray();
        provider.Proposal = new("دي تجربة آلية للمراجعة، والرد محتاج مراجعة الموظف.", scenario.Expected.Intents.FirstOrDefault() ?? "qualification", "Synthetic contract provider; no model-quality assessment.",
            true, sources, "synthetic-contract-provider", 10, 5);
        if (scenario.Category == "takeover_race") provider.BeforeReply = async () =>
        {
            await using var humanDb = fixture.Create();
            await new AiSalesControlService(humanDb, foundation).ControlAsync(chat.Id, admin.Id, 1, "takeover", "Deterministic synthetic race", "replay-takeover", default);
        };
        var engine = new AiSalesDraftService(db, controls, reader, provider, Options.Create(new AiSalesEngineOptions { DraftsEnabled = true, ExternalProcessingApproved = true }));
        void Check(string code, bool condition) { if (!condition) throw new ReplayCheckFailure(code); checks.Add(code); }
        try
        {
            Check("ContextFieldsPreserved", context.Customer.Name == AiSalesContextService.Redact(scenario.Customer.Name) && context.Customer.Age == scenario.Customer.Age && context.Customer.Profession == (scenario.Customer.Profession ?? ""));
            Check("ReviewedMemoryPreserved", context.Customer.ReviewedSummary == AiSalesContextService.Redact(scenario.Customer.ReviewedSummary));
            var expectedKnowledge = knowledge.Values.Where(k => k.Status == AiKnowledgeStatus.Approved).Select(k => k.Id).ToHashSet();
            Check("ApprovedScopedKnowledgeOnly", expectedKnowledge.SetEquals(context.Knowledge.Select(k => k.Id)));
            var contextJson = JsonSerializer.Serialize(context);
            Check("SyntheticIdentifiersRedacted", !contextJson.Contains("synthetic@example.test") && !contextJson.Contains("201000000001"));
            var outgoing = await db.WhatsAppMessages.CountAsync(m => m.ConversationId == chat.Id && m.Direction == (byte)MessageDirection.Outgoing);
            draft = await engine.GenerateAsync(chat.Id, admin.Id, 1, "replay-draft", default);
            Check("ExpectedDraftState", draft.Status.ToString() == (scenario.Expected.DraftStatus ?? nameof(AiDraftStatus.ReviewRequired)));
            Check("ExpectedProviderPath", provider.Calls == (scenario.Expected.ProviderCallExpected == false ? 0 : 1));
            Check("AllowedIntent", scenario.Expected.Intents.Length == 0 || scenario.Expected.Intents.Contains(draft.Intent));
            Check("HumanReviewRequired", scenario.Expected.NeedsHuman != true || draft.NeedsHuman);
            var cited = JsonSerializer.Deserialize<Guid[]>(draft.SourceIdsJson) ?? [];
            Check("OnlyApprovedAllowedCitations", cited.All(id => sources.Contains(id) && expectedKnowledge.Contains(id)));
            if (draft.Status != AiDraftStatus.Stale) Check("RequiredCitationsPresent", scenario.Expected.FactCitationsRequired.All(k => cited.Contains(knowledge[k].Id)));
            Check("IdempotentReplay", (await engine.GenerateAsync(chat.Id, admin.Id, 1, "replay-draft", default)).Id == draft.Id && provider.Calls == (scenario.Expected.ProviderCallExpected == false ? 0 : 1));
            Check("NoNewOutgoingMessage", outgoing == await db.WhatsAppMessages.CountAsync(m => m.ConversationId == chat.Id && m.Direction == (byte)MessageDirection.Outgoing));
            var persisted = await db.Leads.AsNoTracking().SingleAsync(l => l.Id == lead.Id);
            Check("NoUnauthorizedCrmMutation", persisted.FullName == scenario.Customer.Name && persisted.Age == scenario.Customer.Age && persisted.InterestedJobTitle == scenario.Customer.Profession &&
                persisted.AppointmentDate == null && persisted.Status == 1 && persisted.AssignedAiSalesAgentId == agent.Id &&
                !await db.AiOfficeBookings.AnyAsync(b => b.ConversationId == chat.Id) && !await db.LeadVisits.AnyAsync(v => v.LeadId == lead.Id));
            if (scenario.Category == "takeover_race")
            {
                Check("StaleReplyUnavailable", draft.Reply == "" && draft.InputTokens == 10 && draft.OutputTokens == 5);
                Check("TakeoverPreserved", await db.ConversationControls.AnyAsync(c => c.ConversationId == chat.Id && c.Controller == ConversationControllerMode.Human));
            }
        }
        catch (Exception ex) { failure = ex is ReplayCheckFailure ? ex.Message : ex.GetType().Name; }
        timer.Stop();
        var keys = (JsonSerializer.Deserialize<Guid[]>(draft?.SourceIdsJson ?? "[]") ?? []).Select(id => knowledge.Single(k => k.Value.Id == id).Key).ToArray();
        return new(scenario.Id, repeat, failure == null, checks.ToArray(), failure, provider.Calls == 0 ? "local-policy" : "deterministic-fake-provider",
            draft?.Status.ToString() ?? "NotGenerated", draft?.Intent ?? "", provider.Calls, timer.Elapsed.TotalMilliseconds, draft?.Reply ?? "", keys,
            draft?.InputTokens, draft?.OutputTokens, "not_run", scenario.Expected.Must, scenario.Expected.MustNot);
    }
    private sealed class ContractProvider : IAiSalesDraftProvider
    {
        public int Calls { get; private set; }
        public AiDraftProposal Proposal { get; set; } = null!;
        public Func<Task>? BeforeReply { get; set; }
        public async Task<AiDraftProposal> GenerateAsync(AiSalesContext context, CancellationToken ct)
        { Calls++; if (BeforeReply != null) await BeforeReply(); return Proposal; }
    }
    private sealed class ReplayCheckFailure(string code) : Exception(code);
}
