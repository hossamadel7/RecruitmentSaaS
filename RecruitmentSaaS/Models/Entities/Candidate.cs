using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace RecruitmentSaaS.Models.Entities;

public partial class Candidate
{
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public Guid AssignedSalesId { get; set; }

    public Guid JobPackageId { get; set; }

    public Guid RegisteredById { get; set; }

    public string FullName { get; set; } = null!;

    public string Phone { get; set; } = null!;

    public string? NationalId { get; set; }

    public int? Age { get; set; }

    public string? City { get; set; }

    public string? Notes { get; set; }

    public byte Status { get; set; }

    public decimal TotalPaidEgp { get; set; }

    public bool IsCompleted { get; set; }

    public DateTime? CompletedAt { get; set; }

    public bool IsProfileComplete { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? CurrentPackageStageId { get; set; }

    public string? PassportNumber { get; set; }

    public DateOnly? PassportExpiry { get; set; }

    public string? VisaNumber { get; set; }

    public DateOnly? VisaExpiry { get; set; }

    public Guid? CompanyId { get; set; }

    [Precision(0)]
    public DateTime? FlightDate { get; set; }

    [ForeignKey("AssignedSalesId")]
    [InverseProperty("CandidateAssignedSales")]
    public virtual User AssignedSales { get; set; } = null!;

    public virtual Branch Branch { get; set; } = null!;

    public virtual ICollection<CandidateActivity> CandidateActivities { get; set; } = new List<CandidateActivity>();

    public virtual ICollection<CandidateStageHistory> CandidateStageHistories { get; set; } = new List<CandidateStageHistory>();

    public virtual Commission? Commission { get; set; }

    public virtual Company? Company { get; set; }

    public virtual ICollection<ContractUpload> ContractUploads { get; set; } = new List<ContractUpload>();

    public virtual PackageStage? CurrentPackageStage { get; set; }

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();

    public virtual ICollection<FollowUpReminder> FollowUpReminders { get; set; } = new List<FollowUpReminder>();

    public virtual JobPackage JobPackage { get; set; } = null!;

    public virtual PassportDownloadedCandidate? PassportDownloadedCandidate { get; set; }

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();

    public virtual User RegisteredBy { get; set; } = null!;

    public virtual ICollection<StageActionCompletion> StageActionCompletions { get; set; } = new List<StageActionCompletion>();

    public virtual ICollection<StageApprovalRequest> StageApprovalRequests { get; set; } = new List<StageApprovalRequest>();

    public virtual ICollection<VisaUpload> VisaUploads { get; set; } = new List<VisaUpload>();
}
