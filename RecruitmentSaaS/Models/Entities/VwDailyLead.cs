using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Keyless]
public partial class VwDailyLead
{
    public Guid BranchId { get; set; }

    public Guid RegisteredById { get; set; }

    public DateOnly? LeadDate { get; set; }

    public long? LeadCount { get; set; }
}
