using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace RecruitmentSaaS.Services;
public sealed record AiCrmActionRequest(Guid AccountId, [Required, MaxLength(40)] string Action, Guid RequestId,
    long ExpectedOwnershipVersion, bool HumanApproved, [MaxLength(1000)] string? Note = null,
    DateTime? DueAt = null, byte? Stage = null, OfficeBookingRequest? Booking = null);
public sealed class AiSalesCrmActionService(RecruitmentCrmContext db, AiSalesLearningService learning,
    AiSalesControlService controls, AiOfficeBookingService bookings)
{
    public async Task<object> ExecuteAsync(Guid conversationId, Guid actor, AiCrmActionRequest r, CancellationToken ct)
    {
        learning.Require(r.AccountId);
        var chat = await db.WhatsAppConversations.SingleOrDefaultAsync(c => c.Id == conversationId && c.WhatsAppAccountId == r.AccountId, ct)
            ?? throw new AiSalesValidation("Scoped conversation unavailable.");
        if (r.Action == "get_customer") return new { chat.Id, chat.LeadId, chat.LeadStage,
            customer = await db.Leads.AsNoTracking().Where(l => l.Id == chat.LeadId).Select(l => new { l.FullName, l.InterestedJobTitle, l.InterestedCountry, l.Status }).SingleOrDefaultAsync(ct) };
        if (r.Action == "get_history") return await db.WhatsAppMessages.AsNoTracking().Where(m => m.ConversationId == chat.Id)
            .OrderByDescending(m => m.CreatedAt).Select(m => new { m.Id, m.Direction, m.MessageType, m.TextBody, m.CreatedAt }).Take(50).ToArrayAsync(ct);
        if (r.Action is "get_available_products" or "get_current_pricing") return await db.JobPackages.AsNoTracking().Where(p => p.IsActive)
            .Select(p => new { p.Id, p.Name, p.JobTitle, p.DestinationCountry, p.PriceEgp, p.Description, currentOfferVerificationRequired = true }).Take(100).ToArrayAsync(ct);
        if (r.Action == "check_appointment_availability") return new {
            policies = await db.AiOfficePolicies.AsNoTracking().Where(p => p.TenantId == learning.Tenant && p.WhatsAppAccountId == r.AccountId && p.Enabled && p.ValidUntil > DateTime.UtcNow).ToArrayAsync(ct),
            note = "Policy hours are not a reservation; supervised booking validates the actual selected appointment and customer consent." };
        if (!r.HumanApproved || r.RequestId == Guid.Empty) throw new AiSalesValidation("A human approval and request identifier are required for CRM changes.");
        if (!await db.Users.AnyAsync(u => u.Id == actor && u.IsActive && u.Role == 1, ct)) throw new AiSalesValidation("Administrator unavailable.");
        if (r.Action == "request_human_handoff")
            return await controls.ControlAsync(chat.Id, actor, r.ExpectedOwnershipVersion, "takeover", r.Note ?? "Staff-approved handoff", "training-handoff:" + r.RequestId, ct);
        if (r.Action == "book_appointment")
        {
            if (r.Booking == null || r.Booking.ExpectedOwnershipVersion != r.ExpectedOwnershipVersion) throw new AiSalesValidation("Reviewed booking details required.");
            return await bookings.SaveAsync(chat.Id, null, actor, r.Booking, "training-booking:" + r.RequestId, ct);
        }
        await using var gate = await ConversationLock.AcquireAsync(db, "conversation:" + chat.Id, ct);
        var control = await db.ConversationControls.SingleOrDefaultAsync(c => c.ConversationId == chat.Id && c.TenantId == learning.Tenant, ct);
        if (control == null || control.OwnershipVersion != r.ExpectedOwnershipVersion) throw new AiSalesConflict("Conversation ownership changed.");
        var key = "training-action:" + r.RequestId;
        var hash = AiLearningSourceService.Hash(JsonSerializer.Serialize(new { conversationId, actor, request = r }));
        var existing = await db.AiControlRequests.SingleOrDefaultAsync(x => x.TenantId == learning.Tenant && x.Key == key, ct);
        if (existing != null) {
            if (existing.Fingerprint != hash) throw new AiSalesConflict("Action request identifier reused.");
            return new { applied = true, replay = true, existing.ResourceId };
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        switch (r.Action)
        {
            case "add_customer_note":
                if (string.IsNullOrWhiteSpace(r.Note)) throw new AiSalesValidation("Note is required.");
                db.ConversationNotes.Add(new ConversationNote { Id = r.RequestId, ConversationId = chat.Id, AuthorId = actor, Body = r.Note.Trim(), CreatedAt = DateTime.UtcNow });
                break;
            case "create_followup_task":
                if (r.DueAt == null || r.DueAt.Value.Kind == DateTimeKind.Unspecified || r.DueAt <= DateTime.UtcNow || r.DueAt > DateTime.UtcNow.AddDays(90))
                    throw new AiSalesValidation("Select a UTC follow-up within 90 days.");
                db.ConversationFollowUps.Add(new ConversationFollowUp { Id = r.RequestId, ConversationId = chat.Id,
                    AssignedToId = chat.AssignedSalesAgentId ?? actor, CreatedById = actor, DueAt = r.DueAt.Value.ToUniversalTime(),
                    Notes = r.Note, Status = (byte)FollowUpStatus.Pending, CreatedAt = DateTime.UtcNow });
                break;
            case "update_lead_stage":
                // Conversion/lost/booking require their authoritative CRM workflows, never inferred model labels.
                if (r.Stage is not (1 or 2 or 3 or 4 or 5)) throw new AiSalesValidation("Only non-outcome CRM lead stages can be updated here.");
                chat.LeadStage = r.Stage.Value; chat.UpdatedAt = DateTime.UtcNow; break;
            default: throw new AiSalesValidation("Unsupported controlled CRM action.");
        }
        db.AiControlRequests.Add(new AiControlRequest { TenantId = learning.Tenant, Key = key, Fingerprint = hash, ResourceId = chat.Id, Version = control.OwnershipVersion });
        learning.Audit("AiReviewedCrmAction", chat.Id, actor); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new { applied = true, replay = false, conversationId };
    }
}
