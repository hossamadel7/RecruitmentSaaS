using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class VwSalesPerformance
{
    public Guid AssignedSalesId { get; set; }

    public DateOnly? MonthStart { get; set; }

    public long? CompletedDeals { get; set; }

    public decimal? RevenueEgp { get; set; }
}
