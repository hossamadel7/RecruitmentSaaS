using System.ComponentModel.DataAnnotations;

namespace RecruitmentSaaS.Models.DTOs
{
    // =============================================================================
    //  AUTH
    // =============================================================================

    public class LoginDto
    {
        [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
        [EmailAddress(ErrorMessage = "البريد الإلكتروني غير صحيح")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "كلمة المرور مطلوبة")]
        public string Password { get; set; } = string.Empty;
    }

    // =============================================================================
    //  LEAD DTOs
    // =============================================================================

    public class CreateLeadDto
    {
        [Required(ErrorMessage = "الاسم مطلوب")]
        [MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "رقم الهاتف مطلوب")]
        [MaxLength(30)]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "مصدر العميل مطلوب")]
        public byte LeadSource { get; set; }

        public Guid? CampaignId { get; set; }
        public Guid? AssignedSalesId { get; set; }

        [MaxLength(200)]
        public string? InterestedJobTitle { get; set; }

        [MaxLength(100)]
        public string? InterestedCountry { get; set; }

        public string? Notes { get; set; }

        // Referral fields — required when LeadSource = 6
        [MaxLength(200)]
        public string? ReferredByName { get; set; }

        [MaxLength(30)]
        public string? ReferredByPhone { get; set; }
    }

    public class UpdateLeadStatusDto
    {
        [Required]
        public Guid LeadId { get; set; }

        [Required]
        public byte NewStatus { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }

    public class LeadListItemDto
    {
        public Guid Id { get; set; }
        public string LeadCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public byte LeadSource { get; set; }
        public byte Status { get; set; }
        public string? CampaignName { get; set; }
        public string? AssignedSalesName { get; set; }
        public string? AssignedOfficeSalesName { get; set; }  // ADD THIS
        public string? InterestedJobTitle { get; set; }
        public string? InterestedCountry { get; set; }
        public bool IsConverted { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastContactedAt { get; set; }
        public DateTime? AppointmentDate { get; set; }        // ADD THIS
    }

    public class LeadDetailDto
    {
        public Guid Id { get; set; }
        public string LeadCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public byte LeadSource { get; set; }
        public byte Status { get; set; }
        public string? Notes { get; set; }
        public string? InterestedJobTitle { get; set; }
        public string? InterestedCountry { get; set; }
        public string? ReferredByName { get; set; }
        public string? ReferredByPhone { get; set; }
        public bool IsConverted { get; set; }
        public DateTime? ConvertedAt { get; set; }
        public Guid? ConvertedCandidateId { get; set; }
        public DateTime? AppointmentDate { get; set; }
        public DateTime? LastContactedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        // Assigned users
        public Guid? AssignedSalesId { get; set; }
        public string? AssignedSalesName { get; set; }
        public Guid? AssignedOfficeSalesId { get; set; }
        public string? AssignedOfficeSalesName { get; set; }

        // Related info
        public string? CampaignName { get; set; }
        public string? RegisteredByName { get; set; }

        // Collections
        public List<LeadActivityDto> Activities { get; set; } = new();
        public List<LeadCallLogDto> CallLogs { get; set; } = new();
        public List<FollowUpReminderDto> Reminders { get; set; } = new();
    }

    // =============================================================================
    //  WALK-IN DTOs
    // =============================================================================

    public class WalkInSearchDto
    {
        [Required(ErrorMessage = "رقم الهاتف مطلوب")]
        public string Phone { get; set; } = string.Empty;
    }

    public class WalkInCheckInDto
    {
        public Guid LeadId { get; set; }
        public Guid? OfficeSalesId { get; set; }
        public Guid? JobPackageId { get; set; }
        public Guid? CampaignId { get; set; }
        public string? FullName { get; set; }
        public string? Phone { get; set; }
        public string? InterestedJobTitle { get; set; }
        public string? InterestedCountry { get; set; }
        public string? Notes { get; set; }
    }

    public class WalkInSearchResultDto
    {
        public Guid LeadId { get; set; }
        public string LeadCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public byte LeadSource { get; set; }
        public byte Status { get; set; }
        public bool IsConverted { get; set; }
        public Guid? ConvertedCandidateId { get; set; }
        public string? AssignedSalesName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // =============================================================================
    //  COMPLETE PROFILE DTO
    // =============================================================================

    public class CompleteProfileDto
    {
        [Required]
        public Guid CandidateId { get; set; }

        [Required(ErrorMessage = "الرقم القومي مطلوب")]
        [MaxLength(50)]
        public string NationalId { get; set; } = string.Empty;

        [Required(ErrorMessage = "السن مطلوب")]
        [Range(18, 60, ErrorMessage = "السن يجب أن يكون بين 18 و 60")]
        public int Age { get; set; }

        [Required(ErrorMessage = "المحافظة مطلوبة")]
        [MaxLength(100)]
        public string City { get; set; } = string.Empty;

        public string? Notes { get; set; }
    }

    // =============================================================================
    //  CANDIDATE DTOs
    // =============================================================================

    public class CandidateListItemDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? NationalId { get; set; }
        public string CurrentStageName { get; set; } = "—";
        public int CurrentStageOrder { get; set; }
        public byte Status { get; set; }
        public string JobPackageName { get; set; } = string.Empty;
        public string? AssignedSalesName { get; set; }
        public decimal TotalPaidEGP { get; set; }
        public bool IsProfileComplete { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CandidateDetailDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? NationalId { get; set; }
        public string? PassportNumber { get; set; }
        public DateOnly? PassportExpiry { get; set; }
        public int? Age { get; set; }
        public string? City { get; set; }
        public string? Notes { get; set; }
        public string CurrentStageName { get; set; } = "—";
        public int CurrentStageOrder { get; set; }
        public byte Status { get; set; }
        public decimal TotalPaidEGP { get; set; }
        public bool IsProfileComplete { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string JobPackageName { get; set; } = string.Empty;
        public decimal JobPackagePriceEGP { get; set; }
        public string? AssignedSalesName { get; set; }
        public string? BranchName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PassportDownloadedAt { get; set; }
        public List<CandidateStageHistoryDto> StageHistory { get; set; } = new();
        public List<PaymentDto> Payments { get; set; } = new();
        public List<DocumentDto> Documents { get; set; } = new();
    }




    public class AdvanceCandidateStageDto
    {
        [Required]
        public Guid CandidateId { get; set; }

        [Required]
        public byte ToStage { get; set; }

        public bool IsOverride { get; set; } = false;

        [MaxLength(500)]
        public string? OverrideReason { get; set; }
    }

    public class ReassignCandidateDto
    {
        [Required]
        public Guid CandidateId { get; set; }

        [Required]
        public Guid NewSalesUserId { get; set; }

        [Required(ErrorMessage = "سبب إعادة التعيين مطلوب")]
        [MaxLength(500)]
        public string Reason { get; set; } = string.Empty;
    }

    // =============================================================================
    //  ACTIVITY & CALL LOG DTOs
    // =============================================================================

    public class LeadActivityDto
    {
        public Guid Id { get; set; }
        public byte ActivityType { get; set; }
        public string? Description { get; set; }
        public string? Details { get; set; }
        public string? CreatedByName { get; set; }
        public byte ActorType { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AddNoteDto
    {
        [Required]
        public Guid LeadId { get; set; }

        [Required(ErrorMessage = "الملاحظة مطلوبة")]
        [MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        public DateOnly? NextFollowUpDate { get; set; }
    }

    public class LogCallDto
    {
        [Required]
        public Guid LeadId { get; set; }

        [Required(ErrorMessage = "قناة الاتصال مطلوبة")]
        public byte Channel { get; set; }  // 1=Phone 2=WhatsApp

        [Required(ErrorMessage = "نتيجة الاتصال مطلوبة")]
        public byte Outcome { get; set; }  // 1-5

        [MaxLength(500)]
        public string? Note { get; set; }

        public DateOnly? NextFollowUpDate { get; set; }
    }

    // =============================================================================
    //  FOLLOW-UP REMINDER DTOs
    // =============================================================================

    public class FollowUpReminderDto
    {
        public Guid Id { get; set; }
        public Guid LeadId { get; set; }
        public string LeadName { get; set; } = string.Empty;
        public string LeadPhone { get; set; } = string.Empty;
        public DateOnly ReminderDate { get; set; }
        public byte Status { get; set; }
        public DateOnly? SnoozedUntil { get; set; }
        public int SnoozeCount { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateReminderDto
    {
        [Required]
        public Guid LeadId { get; set; }

        [Required(ErrorMessage = "تاريخ التذكير مطلوب")]
        public DateOnly ReminderDate { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }

    public class SnoozeReminderDto
    {
        [Required]
        public Guid ReminderId { get; set; }

        [Required(ErrorMessage = "تاريخ التأجيل مطلوب")]
        public DateOnly SnoozedUntil { get; set; }
    }

    // =============================================================================
    //  PAYMENT DTOs
    // =============================================================================

    public class PaymentDto
    {
        public Guid Id { get; set; }
        public decimal AmountEGP { get; set; }
        public DateOnly PaymentDate { get; set; }
        public byte PaymentMethod { get; set; }
        public byte TransactionType { get; set; }
        public byte Status { get; set; }          // 1=Pending, 2=Approved, 3=Rejected
        public string? Notes { get; set; }
        public string? RecordedByName { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    public class RecordPaymentDto
    {
        [Required]
        public Guid CandidateId { get; set; }

        [Required(ErrorMessage = "المبلغ مطلوب")]
        [Range(0.01, double.MaxValue, ErrorMessage = "المبلغ يجب أن يكون أكبر من صفر")]
        public decimal AmountEGP { get; set; }

        [Required(ErrorMessage = "تاريخ الدفع مطلوب")]
        public DateOnly PaymentDate { get; set; }

        [Required(ErrorMessage = "طريقة الدفع مطلوبة")]
        public byte PaymentMethod { get; set; }  // 1=Cash 2=BankTransfer 3=Cheque

        [MaxLength(500)]
        public string? Notes { get; set; }
    }

    // =============================================================================
    //  REFUND DTOs
    // =============================================================================

    public class RequestRefundDto
    {
        [Required]
        public Guid CandidateId { get; set; }

        [Required(ErrorMessage = "مبلغ الاسترداد مطلوب")]
        [Range(0.01, double.MaxValue, ErrorMessage = "المبلغ يجب أن يكون أكبر من صفر")]
        public decimal AmountEGP { get; set; }

        [Required(ErrorMessage = "سبب الاسترداد مطلوب")]
        [MaxLength(500)]
        public string Reason { get; set; } = string.Empty;
    }

    public class ReviewRefundDto
    {
        [Required]
        public Guid RefundId { get; set; }

        [Required]
        public bool IsApproved { get; set; }

        [MaxLength(500)]
        public string? RejectReason { get; set; }
    }

    public class RefundListItemDto
    {
        public Guid Id { get; set; }
        public string CandidateName { get; set; } = string.Empty;
        public decimal AmountEGP { get; set; }
        public string Reason { get; set; } = string.Empty;
        public byte Status { get; set; }
        public string RequestedByName { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; }
        public string? ReviewedByName { get; set; }
        public DateTime? ReviewedAt { get; set; }
    }

    // =============================================================================
    //  COMMISSION DTOs
    // =============================================================================

    public class CommissionListItemDto
    {
        public Guid Id { get; set; }
        public string SalesUserName { get; set; } = string.Empty;
        public string CandidateName { get; set; } = string.Empty;
        public DateOnly CommissionMonth { get; set; }
        public decimal AmountEGP { get; set; }
        public int DealsThisMonth { get; set; }
        public byte Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ApproveCommissionDto
    {
        [Required]
        public Guid CommissionId { get; set; }
    }

    // =============================================================================
    //  DOCUMENT DTOs
    // =============================================================================

    public class DocumentDto
    {
        public Guid Id { get; set; }
        public byte DocumentType { get; set; }
        public string FileName { get; set; } = string.Empty;
        public int FileSizeBytes { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public string? UploadedByName { get; set; }
        public DateTime UploadedAt { get; set; }
    }

    public class UploadDocumentDto
    {
        [Required]
        public Guid CandidateId { get; set; }

        [Required(ErrorMessage = "نوع المستند مطلوب")]
        public byte DocumentType { get; set; }  // 1=Passport 2=Contract 3=Medical 4=Other

        [Required(ErrorMessage = "الملف مطلوب")]
        public IFormFile File { get; set; } = null!;
    }

    // =============================================================================
    //  STAGE HISTORY DTO
    // =============================================================================
    public class CandidateStageHistoryDto
    {
        public byte? FromStage { get; set; }
        public byte ToStage { get; set; }
        public int ToStageOrder { get; set; }   // used for stage timeline matching
        public bool IsOverride { get; set; }
        public string? OverrideReason { get; set; }
        public string? Notes { get; set; }
        public string? MeetingOutcome { get; set; }
        public string? ChangedByName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // =============================================================================
    //  USER MANAGEMENT DTOs
    // =============================================================================

    public class CreateUserDto
    {
        [Required(ErrorMessage = "الاسم مطلوب")]
        [MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
        [EmailAddress]
        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "كلمة المرور مطلوبة")]
        [MinLength(8, ErrorMessage = "كلمة المرور يجب أن تكون 8 أحرف على الأقل")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "الدور مطلوب")]
        public byte Role { get; set; }  // 1=Admin 2=Reception 3=Sales 4=Accountant 5=Operations

        [Required(ErrorMessage = "الفرع مطلوب")]
        public Guid BranchId { get; set; }
    }

    public class UserListItemDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public byte Role { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }

    // =============================================================================
    //  DASHBOARD DTOs
    // =============================================================================

    public class AdminDashboardDto
    {
        public int TotalLeadsToday { get; set; }
        public int TotalCandidates { get; set; }
        public int PendingRefunds { get; set; }
        public int PendingCommissions { get; set; }
        public decimal TotalRevenueThisMonth { get; set; }
        public List<SalesPerformanceDto> SalesPerformance { get; set; } = new();
        public List<LeadSourceSummaryDto> LeadSourceBreakdown { get; set; } = new();
        public List<PipelineStageSummaryDto> PipelineSummary { get; set; } = new();
    }

    public class SalesDashboardDto
    {
        public int MyLeadsTotal { get; set; }
        public int MyLeadsNew { get; set; }
        public int MyCandidatesTotal { get; set; }
        public int DealsThisMonth { get; set; }
        public List<FollowUpReminderDto> TodayReminders { get; set; } = new();
        public List<LeadListItemDto> RecentLeads { get; set; } = new();
    }

    public class SalesPerformanceDto
    {
        public string SalesName { get; set; } = string.Empty;
        public int CompletedDeals { get; set; }
        public decimal RevenueEGP { get; set; }
        public int PendingCommissions { get; set; }
    }

    public class LeadSourceSummaryDto
    {
        public byte LeadSource { get; set; }
        public int TotalLeads { get; set; }
        public int ConvertedLeads { get; set; }
    }

    public class PipelineStageSummaryDto
    {
        public byte Stage { get; set; }
        public int CandidateCount { get; set; }
    }

    // =============================================================================
    //  JOB PACKAGE DTO
    // =============================================================================

    public class JobPackageListItemDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DestinationCountry { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public decimal PriceEgp { get; set; }
        public bool IsActive { get; set; }
    }

    // =============================================================================
    //  CAMPAIGN DTO
    // =============================================================================

    public class CampaignListItemDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public byte Source { get; set; }
        public byte Status { get; set; }
        public decimal? BudgetEGP { get; set; }
        public decimal? SpendEGP { get; set; }
        public int TotalLeads { get; set; }
        public int ConvertedLeads { get; set; }
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
    }



    // =============================================================================
    //  VISIT DTO
    // =============================================================================
    public class VisitDto
    {
        public Guid Id { get; set; }
        public DateTime VisitDateTime { get; set; }
        public string ReceptionUserName { get; set; } = string.Empty;
        public string? Notes { get; set; }         // Reception note
        public string? AssignedSalesName { get; set; }
    }

    public class AccountantPaymentDto
    {
        public Guid Id { get; set; }
        public Guid CandidateId { get; set; }
        public string CandidateFullName { get; set; } = string.Empty;
        public string CandidatePhone { get; set; } = string.Empty;
        public string JobPackageName { get; set; } = string.Empty;
        public decimal AmountEGP { get; set; }
        public DateOnly PaymentDate { get; set; }
        public byte PaymentMethod { get; set; }
        public byte TransactionType { get; set; }
        public byte Status { get; set; }
        public string? Notes { get; set; }
        public string? RecordedByName { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
    }


    public class LeadFormDto
    {
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "الاسم مطلوب")]
        [MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "رقم الهاتف مطلوب")]
        [MaxLength(30)]
        public string Phone { get; set; } = string.Empty;

        [Required]
        public byte LeadSource { get; set; }

        [MaxLength(200)]
        public string? InterestedJobTitle { get; set; }

        [MaxLength(100)]
        public string? InterestedCountry { get; set; }

        public string? Notes { get; set; }

        [MaxLength(200)]
        public string? ReferredByName { get; set; }

        [MaxLength(30)]
        public string? ReferredByPhone { get; set; }

        public Guid? CampaignId { get; set; }
        public Guid? AssignedSalesId { get; set; }
        public Guid BranchId { get; set; }
    }

    public class CheckInDto
    {
        [Required]
        public Guid LeadId { get; set; }

        public Guid? AssignedOfficeSalesId { get; set; }
        public string? Notes { get; set; }
        public Guid? JobPackageId { get; set; }
    }

    public class LeadCallLogDto
    {
        public Guid Id { get; set; }
        public byte Channel { get; set; }
        public byte Outcome { get; set; }
        public string? Note { get; set; }
        public string? CalledByName { get; set; }
        public DateTime CalledAt { get; set; }
    }
}