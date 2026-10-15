namespace RecruitmentSaaS.Models.Entities;

public enum AiOfficeBookingStatus : byte { Confirmed = 0, Cancelled = 1, Visited = 2 }
public sealed class AiOfficePolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WhatsAppAccountId { get; set; }
    public Guid BranchId { get; set; }
    public int OpenDaysMask { get; set; }
    public int OpensAtMinute { get; set; }
    public int ClosesAtMinute { get; set; }
    public DateTime ValidUntil { get; set; }
    public bool Enabled { get; set; }
    public long Version { get; set; } = 1;
    public string SourceReference { get; set; } = "";
    public Guid ApprovedById { get; set; }
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;
}
public sealed class AiOfficeBooking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid LeadId { get; set; }
    public Guid AgentId { get; set; }
    public Guid? TeamManagerId { get; set; }
    public Guid BranchId { get; set; }
    public Guid PolicyId { get; set; }
    public long PolicyVersion { get; set; }
    public AiOfficeBookingStatus Status { get; set; }
    public long Version { get; set; } = 1;
    public DateTime ArrivalLocal { get; set; }
    public string Purpose { get; set; } = "";
    public Guid ConfirmationMessageId { get; set; }
    public Guid? ConfirmationVoiceTurnId { get; set; }
    public string ConfirmationFingerprint { get; set; } = "";
    public Guid? ReminderId { get; set; }
    public Guid? VerifiedVisitId { get; set; }
    public Guid ReviewedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class AiOfficeBookingEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BookingId { get; set; }
    public long Version { get; set; }
    public string Action { get; set; } = "";
    public Guid ActorId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public DateTime ArrivalLocal { get; set; }
    public Guid BranchId { get; set; }
    public string Purpose { get; set; } = "";
    public Guid? ConfirmationMessageId { get; set; }
    public string ConfirmationFingerprint { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid? FunnelHistoryId { get; set; }
}
