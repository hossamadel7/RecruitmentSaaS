using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RecruitmentSaaS.Services;

public sealed record AiContextMessage(Guid Id, string Speaker, string Type, string Text, DateTime At,
    Guid? VoiceTurnId = null, long? VoiceVersion = null, Guid[]? RecordingIds = null);
public sealed record AiContextFact(Guid Id, long Version, AiKnowledgeKind Kind, string Title, string Content, DateTime ValidUntil);
public sealed record AiCustomerContext(string? Name, byte? Age, string? Profession, byte? LeadStatus, DateTime? AppointmentDate,
    string ReviewedSummary, long MemoryVersion, AiCustomerMemoryDetails? ReviewedDetails = null);
public sealed record AiSalesContext(AiCustomerContext Customer, AiContextMessage[] RecentMessages, AiContextFact[] Knowledge);

/// <summary>Permitted CRM reads are composed by the server; the model cannot choose an arbitrary lead or SQL.</summary>
public sealed class AiSalesContextService(RecruitmentCrmContext db)
{
    public async Task<AiSalesContext> ReadAsync(Guid tenant, WhatsAppConversation chat, CancellationToken ct)
    {
        var lead = chat.LeadId == null ? null : await db.Leads.AsNoTracking().SingleAsync(l => l.Id == chat.LeadId, ct);
        var memory = await db.AiCustomerMemories.AsNoTracking().SingleOrDefaultAsync(m => m.ConversationId == chat.Id && m.TenantId == tenant, ct);
        var messages = await db.WhatsAppMessages.AsNoTracking().Where(m => m.ConversationId == chat.Id &&
            (m.Direction == (byte)MessageDirection.Incoming || m.Status == (byte)MessageStatus.Sent || m.Status == (byte)MessageStatus.Delivered || m.Status == (byte)MessageStatus.Read))
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(20)
            .Select(m => new { m.Id, m.Direction, m.MessageType, m.TextBody, m.CreatedAt }).ToListAsync(ct);
        var messageIds = messages.Select(m => m.Id).ToArray();
        var reviewed = await db.AiVoiceParts.AsNoTracking().Where(p => messageIds.Contains(p.MessageId))
            .Join(db.AiVoiceTurns.AsNoTracking().Where(t => t.TenantId == tenant && t.ConversationId == chat.Id && t.Status == AiVoiceStatus.Reviewed),
                p => p.TurnId, t => t.Id, (p, t) => new { p.MessageId, Turn = t }).ToListAsync(ct);
        var turnIds = reviewed.Select(r => r.Turn.Id).Distinct().ToArray();
        var references = await db.AiVoiceParts.AsNoTracking().Where(p => turnIds.Contains(p.TurnId)).Select(p => new { p.TurnId, p.MessageId }).ToListAsync(ct);
        var emitted = new HashSet<Guid>(); var entries = new List<AiContextMessage>();
        // Iterate newest first so a grouped recording occupies its final message's position.
        foreach (var m in messages)
        {
            var voice = reviewed.FirstOrDefault(r => r.MessageId == m.Id)?.Turn;
            if (voice != null && m.Direction == (byte)MessageDirection.Incoming && m.MessageType == (byte)WhatsAppMessageType.Audio)
            {
                if (emitted.Add(voice.Id)) entries.Add(new(m.Id, "customer", "TranscribedAudio", Redact(voice.ReviewedText), m.CreatedAt,
                    voice.Id, voice.Version, references.Where(r => r.TurnId == voice.Id).Select(r => r.MessageId).Order().ToArray()));
            }
            else entries.Add(new(m.Id, m.Direction == (byte)MessageDirection.Incoming ? "customer" : "staff_or_script",
                ((WhatsAppMessageType)m.MessageType).ToString(), m.MessageType == (byte)WhatsAppMessageType.Text ? Redact(Trim(m.TextBody, 600)) : "[Media awaits staff review]", m.CreatedAt));
        }
        var history = entries.AsEnumerable().Reverse().ToArray();
        var query = history.LastOrDefault(m => m.Speaker == "customer")?.Text ?? "";
        var terms = Regex.Matches(IntakeScript.Normalize(query), @"[\p{L}]{2,}").Select(m => m.Value).Distinct().Take(30).ToArray();
        var now = DateTime.UtcNow;
        // Bounded local lexical retrieval. Approved facts and playbook styles stay separate in the prompt.
        var candidates = await db.AiSalesKnowledge.AsNoTracking().Where(k => k.TenantId == tenant && k.WhatsAppAccountId == chat.WhatsAppAccountId
            && k.Status == AiKnowledgeStatus.Approved && k.ValidFrom <= now && k.ValidUntil > now)
            .OrderByDescending(k => k.ApprovedAt).Take(500).ToListAsync(ct);
        var ranked = candidates.OrderByDescending(k => terms.Count(t => IntakeScript.Normalize(k.Title + " " + k.Content).Contains(t)))
            .ThenByDescending(k => k.ApprovedAt).ToArray();
        var knowledge = ranked.Where(k => k.Kind == AiKnowledgeKind.Fact).Take(6)
            .Concat(ranked.Where(k => k.Kind == AiKnowledgeKind.Playbook).Take(2))
            .Select(k => new AiContextFact(k.Id, k.Version, k.Kind, k.Title, Redact(k.Content), k.ValidUntil)).ToArray();
        return new(new(Redact(Trim(string.IsNullOrWhiteSpace(lead?.FullName) ? chat.IntakeName : lead.FullName, 100)), lead?.Age ?? chat.IntakeAge,
            Redact(Trim(string.IsNullOrWhiteSpace(lead?.InterestedJobTitle) ? chat.IntakeJob : lead.InterestedJobTitle, 100)), lead?.Status, lead?.AppointmentDate,
            Redact(memory?.ReviewedSummary ?? ""), memory?.Version ?? 0,
            memory == null ? null : AiCustomerMemoryDetails.Read(memory.ReviewedDetailsJson).ForModel()), history, knowledge);
    }

    public static string Fingerprint(AiSalesContext context) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(context))));
    private static string Trim(string? value, int max) => (value ?? "")[..Math.Min(value?.Length ?? 0, max)];
    public static string Redact(string value)
    {
        value = Regex.Replace(value, @"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", "[email removed]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return Regex.Replace(value, @"(?<!\p{N})(?:\+?\p{N}[\s()-]*){8,}(?!\p{N})", "[number removed]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
