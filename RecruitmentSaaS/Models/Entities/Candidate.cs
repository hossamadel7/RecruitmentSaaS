using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("Candidates", Schema = "demorecruitment")]
[Index("Phone", Name = "IX_demorecruitment_Cnd_Ph")]
[Index("IsProfileComplete", Name = "IX_demorecruitment_Cnd_Prf")]
[Index("AssignedSalesId", Name = "IX_demorecruitment_Cnd_Sa")]
public partial class Candidate
{
    [Key]
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public Guid AssignedSalesId { get; set; }

    public Guid JobPackageId { get; set; }

    public Guid RegisteredById { get; set; }

    [StringLength(200)]
    public string FullName { get; set; } = null!;

    [StringLength(30)]
    public string Phone { get; set; } = null!;

    [StringLength(50)]
    public string? NationalId { get; set; }

    public int? Age { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    public string? Notes { get; set; }

    public byte Status { get; set; }

    [Column("TotalPaidEGP", TypeName = "decimal(18, 2)")]
    public decimal TotalPaidEgp { get; set; }

    public bool IsCompleted { get; set; }

    [Precision(0)]
    public DateTime? CompletedAt { get; set; }

    public bool IsProfileComplete { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public Guid? CurrentPackageStageId { get; set; }

    [StringLength(50)]
    public string? PassportNumber { get; set; }

    public DateOnly? PassportExpiry { get; set; }

    [StringLength(100)]
    public string? VisaNumber { get; set; }

    public DateOnly? VisaExpiry { get; set; }

    public Guid? CompanyId { get; set; }

    [ForeignKey("AssignedSalesId")]
    [InverseProperty("CandidateAssignedSales")]
    public virtual User AssignedSales { get; set; } = null!;

    [ForeignKey("BranchId")]
    [InverseProperty("Candidates")]
    public virtual Branch Branch { get; set; } = null!;

    [InverseProperty("Candidate")]
    public virtual ICollection<CandidateActivity> CandidateActivities { get; set; } = new List<CandidateActivity>();

    [InverseProperty("Candidate")]
    public virtual ICollection<CandidateStageHistory> CandidateStageHistories { get; set; } = new List<CandidateStageHistory>();

    [InverseProperty("Candidate")]
    public virtual Commission? Commission { get; set; }

    [ForeignKey("CompanyId")]
    [InverseProperty("Candidates")]
    public virtual Company? Company { get; set; }

    [InverseProperty("MatchedCandidate")]
    public virtual ICollection<ContractUpload> ContractUploads { get; set; } = new List<ContractUpload>();

    [ForeignKey("CurrentPackageStageId")]
    [InverseProperty("Candidates")]
    public virtual PackageStage? CurrentPackageStage { get; set; }

    [InverseProperty("Candidate")]
    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();

    [InverseProperty("Candidate")]
    public virtual ICollection<FollowUpReminder> FollowUpReminders { get; set; } = new List<FollowUpReminder>();

    [ForeignKey("JobPackageId")]
    [InverseProperty("Candidates")]
    public virtual JobPackage JobPackage { get; set; } = null!;

    [InverseProperty("Candidate")]
    public virtual PassportDownloadedCandidate? PassportDownloadedCandidate { get; set; }

    [InverseProperty("Candidate")]
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    [InverseProperty("Candidate")]
    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();

    [ForeignKey("RegisteredById")]
    [InverseProperty("CandidateRegisteredBies")]
    public virtual User RegisteredBy { get; set; } = null!;

    [InverseProperty("Candidate")]
    public virtual ICollection<StageActionCompletion> StageActionCompletions { get; set; } = new List<StageActionCompletion>();

    [InverseProperty("Candidate")]
    public virtual ICollection<StageApprovalRequest> StageApprovalRequests { get; set; } = new List<StageApprovalRequest>();

    [InverseProperty("MatchedCandidate")]
    public virtual ICollection<VisaUpload> VisaUploads { get; set; } = new List<VisaUpload>();
}
