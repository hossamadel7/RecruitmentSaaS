using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RecruitmentSaaS.Services;
public sealed record AiLearningSource(WhatsAppConversation Chat, string Fingerprint, AiLearningMessage[] Messages,
    string VerifiedOutcome, string Evidence, Guid? SalespersonId, string Cohort, byte? CrmLeadStatus);

public sealed class AiLearningSourceService(RecruitmentCrmContext db, IOptions<AiSalesLearningOptions> options, AiSalesControlService controls)
{
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public async Task<AiLearningSource> ReadAsync(Guid account, Guid id, CancellationToken ct)
    {
        var chat = await db.WhatsAppConversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.WhatsAppAccountId == account, ct)
            ?? throw new AiSalesValidation("Scoped conversation unavailable.");
        var rows = await db.WhatsAppMessages.AsNoTracking().Where(m => m.ConversationId == id && m.WhatsAppAccountId == account)
            .OrderBy(m => m.WhatsAppTimestamp == DateTime.MinValue ? m.CreatedAt : m.WhatsAppTimestamp).ThenBy(m => m.CreatedAt).ThenBy(m => m.Id).Take(options.Value.MaxMessagesPerConversation + 1).ToListAsync(ct);
        if (rows.Count > options.Value.MaxMessagesPerConversation) throw new AiSalesValidation("Conversation exceeds configured processing limit; raise it or prepare separately.");
        var lead = chat.LeadId == null ? null : await db.Leads.AsNoTracking().SingleOrDefaultAsync(l => l.Id == chat.LeadId, ct);
        var contact = await db.WhatsAppContacts.AsNoTracking().SingleAsync(c => c.Id == chat.ContactId, ct);
        var authors = rows.Where(m => m.SenderUserId != null).Select(m => m.SenderUserId!.Value).Distinct().ToArray();
        var names = await db.Users.AsNoTracking().Where(u => authors.Contains(u.Id)).Select(u => u.FullName).ToListAsync(ct);
        names.Add(contact.Name ?? ""); names.Add(lead?.FullName ?? "");
        var visits = chat.LeadId == null ? [] : await db.LeadVisits.AsNoTracking().Where(v => v.LeadId == chat.LeadId).OrderBy(v => v.Id)
            .Select(v => new { v.Id, v.ConvertedCandidateId, v.VisitDateTime }).ToListAsync(ct);
        var audioIds = rows.Where(m => m.MessageType == 4 && m.Direction == 1).Select(m => m.Id).ToArray();
        var voice = await db.AiVoiceParts.AsNoTracking().Where(p => audioIds.Contains(p.MessageId))
            .Join(db.AiVoiceTurns.AsNoTracking().Where(t => t.TenantId == controls.Options.TenantId && t.ConversationId == id && t.Status == AiVoiceStatus.Reviewed),
                p => p.TurnId, t => t.Id, (p, t) => new { p.MessageId, p.MediaId, t.Id, t.Version, t.ReviewedText }).OrderBy(x => x.MessageId).ToListAsync(ct);
        voice = voice.Where(v => rows.Any(m => m.Id == v.MessageId && m.MediaId == v.MediaId)).ToList();
        var bookings = await db.AiOfficeBookings.AsNoTracking().Where(b => b.TenantId == controls.Options.TenantId && b.ConversationId == id && b.Status == AiOfficeBookingStatus.Confirmed)
            .OrderBy(b => b.Id).Select(b => new { b.Id, b.Version, b.ArrivalLocal }).ToListAsync(ct);
        var candidateId = lead?.ConvertedCandidateId;
        var converted = lead?.IsConverted == true && candidateId != null && await db.Candidates.AnyAsync(c => c.Id == candidateId && c.BranchId == lead.BranchId, ct);
        var outcome = converted ? "CRM_CONVERSION" : visits.Any() ? "VERIFIED_VISIT" : bookings.Count > 0 ? "REVIEWED_BOOKING" :
            lead?.Status == 5 && lead.AppointmentDate != null ? "CRM_RECORDED_APPOINTMENT" : lead?.Status == 8 ? "CRM_RECORDED_LOST" : "UNKNOWN";
        var evidence = converted ? "Lead.ConvertedCandidateId:" + candidateId : visits.Count > 0 ? "LeadVisit:" + visits.OrderBy(v => v.VisitDateTime).Last().Id :
            bookings.Count > 0 ? "AiOfficeBooking:" + bookings[0].Id : lead?.Status == 5 && lead.AppointmentDate != null ? "Lead.AppointmentDate; recorded, not attendance" :
            lead?.Status == 8 ? "Lead.Status=8; recorded outcome, not causal proof" : "No verified conversion or visit evidence";
        var human = rows.Where(m => m.Direction == 2 && m.MessageSource != (byte)MessageSource.System && m.SenderAiSalesAgentId == null && m.Status is 3 or 4 or 5).ToArray();
        var humanIds = human.Where(m => m.SenderUserId != null).Select(m => m.SenderUserId!.Value).Distinct().ToArray();
        var feedback = await db.AiSalesFeedback.AsNoTracking().Where(f => f.TenantId == controls.Options.TenantId &&
            db.AiSalesDrafts.Any(d => d.Id == f.DraftId && d.ConversationId == id && d.TenantId == controls.Options.TenantId))
            .OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id).Take(100).Select(f => new { f.Id, f.Kind, f.Note, f.EditedReply, f.CreatedAt }).ToListAsync(ct);
        feedback = feedback.OrderBy(f => f.CreatedAt).ThenBy(f => f.Id).ToList();
        Guid? salesperson = humanIds.Length == 1 && human.All(m => m.SenderUserId != null) ? humanIds[0] : null;
        // Do not credit the current owner for historical authorship, or count delivery receipts as content edits.
        var fingerprint = Hash(JsonSerializer.Serialize(new { pipeline = OpenAiLearningProvider.AnalysisVersion, chat.Id, chat.WhatsAppAccountId, chat.LeadId, chat.Status, chat.LeadStage,
            LeadStatus = lead?.Status, lead?.IsConverted, lead?.ConvertedCandidateId, lead?.AppointmentDate, lead?.LeadSource, lead?.CampaignId, lead?.InterestedCountry, Visits = visits, Bookings = bookings, Voice = voice, Feedback = feedback,
            Messages = rows.Select(m => new { m.Id, m.CreatedAt, m.WhatsAppTimestamp, m.MediaId, m.ReplyToMessageId, Fingerprint = AiChatFactService.Fingerprint(m) }) }));
        var seenVoice = new HashSet<Guid>();
        var messages = rows.Select(m => {
            var transcript = voice.SingleOrDefault(v => v.MessageId == m.Id);
            var body = m.MessageType == 1 ? Redact(m.TextBody ?? "", names) : transcript != null && seenVoice.Add(transcript.Id)
                ? Redact(transcript.ReviewedText, names) : transcript != null ? "[Already represented part of a reviewed voice group]" : "[Uninterpreted media; not evidence of content]";
            return new AiLearningMessage(m.Id, m.Direction == 1 ? "customer" :
            m.SenderAiSalesAgentId != null || m.MessageSource == 4 ? "ai_or_system" : m.Status is not (3 or 4 or 5) ? "unconfirmed" :
            m.SenderUserId != null ? "staff" : "device_unverified", transcript != null ? "TranscribedAudio" : ((WhatsAppMessageType)m.MessageType).ToString(), body,
            m.WhatsAppTimestamp == DateTime.MinValue ? m.CreatedAt : m.WhatsAppTimestamp);
        }).Concat(feedback.Select(f => new AiLearningMessage(f.Id, "staff_feedback", "Feedback",
            Redact($"Unverified staff feedback ({f.Kind}); not proof of sales outcome. Note: {f.Note}; proposed correction: {f.EditedReply}", names), f.CreatedAt))).ToArray();
        var cohort = (lead?.LeadSource.ToString() ?? "unknown") + ":" + (lead?.CampaignId?.ToString() ?? "none") + ":" +
            (lead?.InterestedCountry ?? chat.AdCountry ?? "unknown");
        return new(chat, fingerprint, messages, outcome, evidence, salesperson, cohort[..Math.Min(300, cohort.Length)], lead?.Status);
    }
    public static string Redact(string text, IEnumerable<string?> names)
    {
        text = AiChatLearningService.Mask(text, names);
        return Regex.Replace(text, @"(?im)^.*(?:\b(?:street|apartment|card number|iban)\b|شارع|شقه|شقة|رقم الحساب).*$",
            "[address/payment detail removed]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
