using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("Commissions", Schema = "demorecruitment")]
[Index("SalesUserId", "CommissionMonth", Name = "IX_demorecruitment_Com_Mo")]
[Index("Status", Name = "IX_demorecruitment_Com_St")]
[Index("CandidateId", Name = "UQ_demorecruitment_Com_Cnd", IsUnique = true)]
public partial class Commission
{
    [Key]
    public Guid Id { get; set; }

    public Guid SalesUserId { get; set; }

    public Guid CandidateId { get; set; }

    public DateOnly CommissionMonth { get; set; }

    [Column("AmountEGP", TypeName = "decimal(18, 2)")]
    public decimal AmountEgp { get; set; }

    public int DealsThisMonth { get; set; }

    public byte Status { get; set; }

    public Guid? ApprovedById { get; set; }

    [Precision(0)]
    public DateTime? ApprovedAt { get; set; }

    public Guid? PaidById { get; set; }

    [Precision(0)]
    public DateTime? PaidAt { get; set; }

    [StringLength(500)]
    public string? ReversedReason { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("ApprovedById")]
    [InverseProperty("CommissionApprovedBies")]
    public virtual User? ApprovedBy { get; set; }

    [ForeignKey("CandidateId")]
    [InverseProperty("Commission")]
    public virtual Candidate Candidate { get; set; } = null!;

    [ForeignKey("PaidById")]
    [InverseProperty("CommissionPaidBies")]
    public virtual User? PaidBy { get; set; }

    [ForeignKey("SalesUserId")]
    [InverseProperty("CommissionSalesUsers")]
    public virtual User SalesUser { get; set; } = null!;
}
