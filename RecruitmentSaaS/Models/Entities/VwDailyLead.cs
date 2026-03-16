using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class VwDailyLead
{
    public Guid BranchId { get; set; }

    public Guid RegisteredById { get; set; }

    public DateOnly? LeadDate { get; set; }

    public long? LeadCount { get; set; }
}
