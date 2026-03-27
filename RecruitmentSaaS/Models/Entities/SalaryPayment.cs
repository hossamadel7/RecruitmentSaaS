using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("SalaryPayments", Schema = "demorecruitment")]
[Index("SalaryMonth", Name = "IX_demorecruitment_SP_Month", AllDescending = true)]
[Index("Status", Name = "IX_demorecruitment_SP_Status")]
[Index("UserId", Name = "IX_demorecruitment_SP_UserId")]
[Index("UserId", "SalaryMonth", Name = "UQ_demorecruitment_SP_UserMonth", IsUnique = true)]
public partial class SalaryPayment
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateOnly SalaryMonth { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal BaseSalary { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal Adjustment { get; set; }

    [StringLength(500)]
    public string? AdjustmentNote { get; set; }

    [Column(TypeName = "decimal(19, 2)")]
    public decimal? TotalAmount { get; set; }

    public byte Status { get; set; }

    public Guid? ApprovedById { get; set; }

    [Precision(0)]
    public DateTime? ApprovedAt { get; set; }

    public Guid? PaidById { get; set; }

    [Precision(0)]
    public DateTime? PaidAt { get; set; }

    public Guid CreatedById { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey("ApprovedById")]
    [InverseProperty("SalaryPaymentApprovedBies")]
    public virtual User? ApprovedBy { get; set; }

    [ForeignKey("CreatedById")]
    [InverseProperty("SalaryPaymentCreatedBies")]
    public virtual User CreatedBy { get; set; } = null!;

    [ForeignKey("PaidById")]
    [InverseProperty("SalaryPaymentPaidBies")]
    public virtual User? PaidBy { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("SalaryPaymentUsers")]
    public virtual User User { get; set; } = null!;
}
