using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Keyless]
public partial class VwDailyPayment
{
    public DateOnly? PayDate { get; set; }

    public long? PaymentCount { get; set; }

    [Column("TotalEGP", TypeName = "decimal(38, 2)")]
    public decimal? TotalEgp { get; set; }
}
