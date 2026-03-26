using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class User
{
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public byte Role { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public decimal BaseSalary { get; set; }

    public virtual Branch Branch { get; set; } = null!;

    public virtual ICollection<Campaign> Campaigns { get; set; } = new List<Campaign>();

    public virtual ICollection<CandidateActivity> CandidateActivities { get; set; } = new List<CandidateActivity>();

    public virtual ICollection<Candidate> CandidateAssignedSales { get; set; } = new List<Candidate>();

    public virtual ICollection<Candidate> CandidateRegisteredBies { get; set; } = new List<Candidate>();

    public virtual ICollection<CandidateStageHistory> CandidateStageHistories { get; set; } = new List<CandidateStageHistory>();

    public virtual ICollection<Commission> CommissionApprovedBies { get; set; } = new List<Commission>();

    public virtual ICollection<Commission> CommissionPaidBies { get; set; } = new List<Commission>();

    public virtual ICollection<Commission> CommissionSalesUsers { get; set; } = new List<Commission>();

    public virtual ICollection<CommissionTier> CommissionTiers { get; set; } = new List<CommissionTier>();

    public virtual ICollection<Company> Companies { get; set; } = new List<Company>();

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();

    public virtual ICollection<FollowUpReminder> FollowUpReminderAssignedTos { get; set; } = new List<FollowUpReminder>();

    public virtual ICollection<FollowUpReminder> FollowUpReminderCreatedBies { get; set; } = new List<FollowUpReminder>();

    public virtual ICollection<LeadActivity> LeadActivities { get; set; } = new List<LeadActivity>();

    public virtual ICollection<Lead> LeadAssignedOfficeSales { get; set; } = new List<Lead>();

    public virtual ICollection<Lead> LeadAssignedSales { get; set; } = new List<Lead>();

    public virtual ICollection<LeadCallLog> LeadCallLogs { get; set; } = new List<LeadCallLog>();

    public virtual ICollection<LeadFunnelHistory> LeadFunnelHistories { get; set; } = new List<LeadFunnelHistory>();

    public virtual ICollection<Lead> LeadRegisteredBies { get; set; } = new List<Lead>();

    public virtual ICollection<LeadVisit> LeadVisitAssignedSalesUsers { get; set; } = new List<LeadVisit>();

    public virtual ICollection<LeadVisit> LeadVisitReceptionUsers { get; set; } = new List<LeadVisit>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<PassportDownloadLog> PassportDownloadLogs { get; set; } = new List<PassportDownloadLog>();

    public virtual ICollection<Payment> PaymentApprovedBies { get; set; } = new List<Payment>();

    public virtual ICollection<Payment> PaymentRecordedBies { get; set; } = new List<Payment>();

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public virtual ICollection<Refund> RefundExecutedBies { get; set; } = new List<Refund>();

    public virtual ICollection<Refund> RefundRequestedBies { get; set; } = new List<Refund>();

    public virtual ICollection<Refund> RefundReviewedBies { get; set; } = new List<Refund>();

    public virtual ICollection<SalaryPayment> SalaryPaymentApprovedBies { get; set; } = new List<SalaryPayment>();

    public virtual ICollection<SalaryPayment> SalaryPaymentCreatedBies { get; set; } = new List<SalaryPayment>();

    public virtual ICollection<SalaryPayment> SalaryPaymentPaidBies { get; set; } = new List<SalaryPayment>();

    public virtual ICollection<SalaryPayment> SalaryPaymentUsers { get; set; } = new List<SalaryPayment>();

    public virtual ICollection<SalesGoogleSheetUser> SalesGoogleSheetUsers { get; set; } = new List<SalesGoogleSheetUser>();

    public virtual ICollection<SalesGoogleSheet> SalesGoogleSheets { get; set; } = new List<SalesGoogleSheet>();

    public virtual ICollection<StageActionCompletion> StageActionCompletions { get; set; } = new List<StageActionCompletion>();

    public virtual ICollection<StageApprovalRequest> StageApprovalRequestRequestedBies { get; set; } = new List<StageApprovalRequest>();

    public virtual ICollection<StageApprovalRequest> StageApprovalRequestReviewedBies { get; set; } = new List<StageApprovalRequest>();

    public virtual ICollection<VisaUpload> VisaUploads { get; set; } = new List<VisaUpload>();
}
