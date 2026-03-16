using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class Payment
{
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public Guid RecordedById { get; set; }

    public decimal AmountEgp { get; set; }

    public DateOnly PaymentDate { get; set; }

    public byte PaymentMethod { get; set; }

    public byte TransactionType { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public byte Status { get; set; }

    public Guid? ApprovedById { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public string? RejectionReason { get; set; }

    public virtual User? ApprovedBy { get; set; }

    public virtual Candidate Candidate { get; set; } = null!;

    public virtual User RecordedBy { get; set; } = null!;

    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}
