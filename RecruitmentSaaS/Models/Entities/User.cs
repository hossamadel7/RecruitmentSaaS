using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("Users", Schema = "demorecruitment")]
[Index("BranchId", Name = "IX_demorecruitment_Us_Br")]
[Index("Email", Name = "UQ_demorecruitment_Us_Email", IsUnique = true)]
public partial class User
{
    [Key]
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    [StringLength(200)]
    public string FullName { get; set; } = null!;

    [StringLength(255)]
    public string Email { get; set; } = null!;

    [StringLength(500)]
    public string PasswordHash { get; set; } = null!;

    public byte Role { get; set; }

    public bool IsActive { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [Precision(0)]
    public DateTime? LastLoginAt { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal BaseSalary { get; set; }

    [ForeignKey("BranchId")]
    [InverseProperty("Users")]
    public virtual Branch Branch { get; set; } = null!;

    [InverseProperty("CreatedBy")]
    public virtual ICollection<Campaign> Campaigns { get; set; } = new List<Campaign>();

    [InverseProperty("CreatedBy")]
    public virtual ICollection<CandidateActivity> CandidateActivities { get; set; } = new List<CandidateActivity>();

    [InverseProperty("AssignedSales")]
    public virtual ICollection<Candidate> CandidateAssignedSales { get; set; } = new List<Candidate>();

    [InverseProperty("RegisteredBy")]
    public virtual ICollection<Candidate> CandidateRegisteredBies { get; set; } = new List<Candidate>();

    [InverseProperty("ChangedBy")]
    public virtual ICollection<CandidateStageHistory> CandidateStageHistories { get; set; } = new List<CandidateStageHistory>();

    [InverseProperty("ApprovedBy")]
    public virtual ICollection<Commission> CommissionApprovedBies { get; set; } = new List<Commission>();

    [InverseProperty("PaidBy")]
    public virtual ICollection<Commission> CommissionPaidBies { get; set; } = new List<Commission>();

    [InverseProperty("SalesUser")]
    public virtual ICollection<Commission> CommissionSalesUsers { get; set; } = new List<Commission>();

    [InverseProperty("CreatedBy")]
    public virtual ICollection<CommissionTier> CommissionTiers { get; set; } = new List<CommissionTier>();

    [InverseProperty("CreatedBy")]
    public virtual ICollection<Company> Companies { get; set; } = new List<Company>();

    [InverseProperty("UploadedBy")]
    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();

    [InverseProperty("AssignedTo")]
    public virtual ICollection<FollowUpReminder> FollowUpReminderAssignedTos { get; set; } = new List<FollowUpReminder>();

    [InverseProperty("CreatedBy")]
    public virtual ICollection<FollowUpReminder> FollowUpReminderCreatedBies { get; set; } = new List<FollowUpReminder>();

    [InverseProperty("CreatedBy")]
    public virtual ICollection<LeadActivity> LeadActivities { get; set; } = new List<LeadActivity>();

    [InverseProperty("AssignedOfficeSales")]
    public virtual ICollection<Lead> LeadAssignedOfficeSales { get; set; } = new List<Lead>();

    [InverseProperty("AssignedSales")]
    public virtual ICollection<Lead> LeadAssignedSales { get; set; } = new List<Lead>();

    [InverseProperty("CalledBy")]
    public virtual ICollection<LeadCallLog> LeadCallLogs { get; set; } = new List<LeadCallLog>();

    [InverseProperty("ChangedBy")]
    public virtual ICollection<LeadFunnelHistory> LeadFunnelHistories { get; set; } = new List<LeadFunnelHistory>();

    [InverseProperty("RegisteredBy")]
    public virtual ICollection<Lead> LeadRegisteredBies { get; set; } = new List<Lead>();

    [InverseProperty("AssignedSalesUser")]
    public virtual ICollection<LeadVisit> LeadVisitAssignedSalesUsers { get; set; } = new List<LeadVisit>();

    [InverseProperty("ReceptionUser")]
    public virtual ICollection<LeadVisit> LeadVisitReceptionUsers { get; set; } = new List<LeadVisit>();

    [InverseProperty("User")]
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    [InverseProperty("DownloadedBy")]
    public virtual ICollection<PassportDownloadLog> PassportDownloadLogs { get; set; } = new List<PassportDownloadLog>();

    [InverseProperty("ApprovedBy")]
    public virtual ICollection<Payment> PaymentApprovedBies { get; set; } = new List<Payment>();

    [InverseProperty("RecordedBy")]
    public virtual ICollection<Payment> PaymentRecordedBies { get; set; } = new List<Payment>();

    [InverseProperty("User")]
    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    [InverseProperty("ExecutedBy")]
    public virtual ICollection<Refund> RefundExecutedBies { get; set; } = new List<Refund>();

    [InverseProperty("RequestedBy")]
    public virtual ICollection<Refund> RefundRequestedBies { get; set; } = new List<Refund>();

    [InverseProperty("ReviewedBy")]
    public virtual ICollection<Refund> RefundReviewedBies { get; set; } = new List<Refund>();

    [InverseProperty("ApprovedBy")]
    public virtual ICollection<SalaryPayment> SalaryPaymentApprovedBies { get; set; } = new List<SalaryPayment>();

    [InverseProperty("CreatedBy")]
    public virtual ICollection<SalaryPayment> SalaryPaymentCreatedBies { get; set; } = new List<SalaryPayment>();

    [InverseProperty("PaidBy")]
    public virtual ICollection<SalaryPayment> SalaryPaymentPaidBies { get; set; } = new List<SalaryPayment>();

    [InverseProperty("User")]
    public virtual ICollection<SalaryPayment> SalaryPaymentUsers { get; set; } = new List<SalaryPayment>();

    [InverseProperty("SalesUser")]
    public virtual ICollection<SalesGoogleSheetUser> SalesGoogleSheetUsers { get; set; } = new List<SalesGoogleSheetUser>();

    [InverseProperty("CreatedBy")]
    public virtual ICollection<SalesGoogleSheet> SalesGoogleSheets { get; set; } = new List<SalesGoogleSheet>();

    [InverseProperty("CompletedBy")]
    public virtual ICollection<StageActionCompletion> StageActionCompletions { get; set; } = new List<StageActionCompletion>();

    [InverseProperty("RequestedBy")]
    public virtual ICollection<StageApprovalRequest> StageApprovalRequestRequestedBies { get; set; } = new List<StageApprovalRequest>();

    [InverseProperty("ReviewedBy")]
    public virtual ICollection<StageApprovalRequest> StageApprovalRequestReviewedBies { get; set; } = new List<StageApprovalRequest>();

    [InverseProperty("UploadedBy")]
    public virtual ICollection<VisaUpload> VisaUploads { get; set; } = new List<VisaUpload>();
}
