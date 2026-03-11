using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("Refunds", Schema = "demorecruitment")]
public partial class Refund
{
    [Key]
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    [Column("AmountEGP", TypeName = "decimal(18, 2)")]
    public decimal AmountEgp { get; set; }

    [StringLength(500)]
    public string Reason { get; set; } = null!;

    public byte Status { get; set; }

    public Guid RequestedById { get; set; }

    [Precision(0)]
    public DateTime RequestedAt { get; set; }

    public Guid? ReviewedById { get; set; }

    [Precision(0)]
    public DateTime? ReviewedAt { get; set; }

    [StringLength(500)]
    public string? RejectReason { get; set; }

    public Guid? ExecutedById { get; set; }

    [Precision(0)]
    public DateTime? ExecutedAt { get; set; }

    public Guid? RefundPaymentId { get; set; }

    [ForeignKey("CandidateId")]
    [InverseProperty("Refunds")]
    public virtual Candidate Candidate { get; set; } = null!;

    [ForeignKey("ExecutedById")]
    [InverseProperty("RefundExecutedBies")]
    public virtual User? ExecutedBy { get; set; }

    [ForeignKey("RefundPaymentId")]
    [InverseProperty("Refunds")]
    public virtual Payment? RefundPayment { get; set; }

    [ForeignKey("RequestedById")]
    [InverseProperty("RefundRequestedBies")]
    public virtual User RequestedBy { get; set; } = null!;

    [ForeignKey("ReviewedById")]
    [InverseProperty("RefundReviewedBies")]
    public virtual User? ReviewedBy { get; set; }
}
