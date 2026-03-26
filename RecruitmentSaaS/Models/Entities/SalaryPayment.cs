using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class SalaryPayment
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateOnly SalaryMonth { get; set; }

    public decimal BaseSalary { get; set; }

    public decimal Adjustment { get; set; }

    public string? AdjustmentNote { get; set; }

    public decimal? TotalAmount { get; set; }

    public byte Status { get; set; }

    public Guid? ApprovedById { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public Guid? PaidById { get; set; }

    public DateTime? PaidAt { get; set; }

    public Guid CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User? ApprovedBy { get; set; }

    public virtual User CreatedBy { get; set; } = null!;

    public virtual User? PaidBy { get; set; }

    public virtual User User { get; set; } = null!;
}
