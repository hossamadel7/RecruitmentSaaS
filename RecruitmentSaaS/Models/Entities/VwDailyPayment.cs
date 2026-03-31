using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class VwDailyPayment
{
    public DateOnly? PayDate { get; set; }

    public long? PaymentCount { get; set; }

    public decimal? TotalEgp { get; set; }
}
