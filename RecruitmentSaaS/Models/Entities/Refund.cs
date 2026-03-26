using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class Refund
{
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public decimal AmountEgp { get; set; }

    public string Reason { get; set; } = null!;

    public byte Status { get; set; }

    public Guid RequestedById { get; set; }

    public DateTime RequestedAt { get; set; }

    public Guid? ReviewedById { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? RejectReason { get; set; }

    public Guid? ExecutedById { get; set; }

    public DateTime? ExecutedAt { get; set; }

    public Guid? RefundPaymentId { get; set; }

    public virtual Candidate Candidate { get; set; } = null!;

    public virtual User? ExecutedBy { get; set; }

    public virtual Payment? RefundPayment { get; set; }

    public virtual User RequestedBy { get; set; } = null!;

    public virtual User? ReviewedBy { get; set; }
}
