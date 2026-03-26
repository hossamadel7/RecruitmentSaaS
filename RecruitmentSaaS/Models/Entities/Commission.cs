using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class Commission
{
    public Guid Id { get; set; }

    public Guid SalesUserId { get; set; }

    public Guid CandidateId { get; set; }

    public DateOnly CommissionMonth { get; set; }

    public decimal AmountEgp { get; set; }

    public int DealsThisMonth { get; set; }

    public byte Status { get; set; }

    public Guid? ApprovedById { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public Guid? PaidById { get; set; }

    public DateTime? PaidAt { get; set; }

    public string? ReversedReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User? ApprovedBy { get; set; }

    public virtual Candidate Candidate { get; set; } = null!;

    public virtual User? PaidBy { get; set; }

    public virtual User SalesUser { get; set; } = null!;
}
