using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class CommissionSetting
{
    public Guid Id { get; set; }

    public byte ResetDayOfMonth { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedById { get; set; }
}
