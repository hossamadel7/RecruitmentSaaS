using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Keyless]
public partial class VwSalesPerformance
{
    public Guid AssignedSalesId { get; set; }

    public DateOnly? MonthStart { get; set; }

    public long? CompletedDeals { get; set; }

    [Column("RevenueEGP", TypeName = "decimal(38, 2)")]
    public decimal? RevenueEgp { get; set; }
}
