using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;

namespace RecruitmentSaaS.Services;

/// <summary>
/// When Gihan confirms the day a customer will come, record it exactly like a TeleSales "حجز" from the inbox
/// (lead status 5 with the appointment, a reminder the day before, and the bookings report row).
/// </summary>
public sealed class AiVisitBookingService(RecruitmentCrmContext db, CustomerOutcomeService outcomes)
{
    public const string ActorName = "جيهان (المساعد الذكي)";
    public const int OpensAtHour = 10, ClosesAtHour = 18, DefaultHour = 12, MaxDaysAhead = 30;

    /// <summary>The model's "YYYY-MM-DD" and optional "HH:mm" as a Cairo appointment, or null when unusable.</summary>
    public static DateTime? Parse(string? date, string? time, DateTime todayCairo)
    {
        if (string.IsNullOrWhiteSpace(date) || !DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", out var day)) return null;
        var today = DateOnly.FromDateTime(todayCairo);
        if (day < today || day > today.AddDays(MaxDaysAhead) || day.DayOfWeek == DayOfWeek.Friday) return null;
        var at = new TimeOnly(DefaultHour, 0);
        if (!string.IsNullOrWhiteSpace(time) && TimeOnly.TryParseExact(time.Trim(), "HH:mm", out var t) &&
            t.Hour >= OpensAtHour && t < new TimeOnly(ClosesAtHour, 0)) at = t;
        return day.ToDateTime(at);
    }

    /// <returns>null when saved, otherwise why it was not.</returns>
    public async Task<string?> BookAsync(Guid conversationId, Guid agentId, DateTime appointmentCairo, CancellationToken ct)
    {
        var chat = await db.WhatsAppConversations.Include(c => c.Contact).SingleOrDefaultAsync(c => c.Id == conversationId, ct);
        if (chat == null || chat.AssignedAiSalesAgentId != agentId) return "ChatNotOwned";
        var actor = await db.AiSalesAgents.Where(a => a.Id == agentId).Select(a => (Guid?)a.TeamManagerId).SingleOrDefaultAsync(ct);
        if (actor == null) return "AgentMissing";
        if (chat.LeadId != null && await db.Leads.AnyAsync(l => l.Id == chat.LeadId && (l.Status == 6 || l.Status == 7), ct)) return "AlreadyVisited";
        return await outcomes.ApplyAsync(chat, CustomerOutcomeService.Booked, appointmentCairo,
            "حجز من محادثة جيهان (المساعد الذكي)", actor.Value, ActorName);
    }
}
