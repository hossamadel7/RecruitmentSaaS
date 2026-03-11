using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("Payments", Schema = "demorecruitment")]
[Index("CandidateId", Name = "IX_demorecruitment_Pay_Cnd")]
[Index("PaymentDate", Name = "IX_demorecruitment_Pay_Dt")]
public partial class Payment
{
    [Key]
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public Guid RecordedById { get; set; }

    [Column("AmountEGP", TypeName = "decimal(18, 2)")]
    public decimal AmountEgp { get; set; }

    public DateOnly PaymentDate { get; set; }

    public byte PaymentMethod { get; set; }

    public byte TransactionType { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public byte Status { get; set; }

    public Guid? ApprovedById { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? ApprovedAt { get; set; }

    [StringLength(500)]
    public string? RejectionReason { get; set; }

    [ForeignKey("ApprovedById")]
    [InverseProperty("PaymentApprovedBies")]
    public virtual User? ApprovedBy { get; set; }

    [ForeignKey("CandidateId")]
    [InverseProperty("Payments")]
    public virtual Candidate Candidate { get; set; } = null!;

    [ForeignKey("RecordedById")]
    [InverseProperty("PaymentRecordedBies")]
    public virtual User RecordedBy { get; set; } = null!;

    [InverseProperty("RefundPayment")]
    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}
