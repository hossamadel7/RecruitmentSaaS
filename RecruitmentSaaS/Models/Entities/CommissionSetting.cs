using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("CommissionSettings", Schema = "demorecruitment")]
public partial class CommissionSetting
{
    [Key]
    public Guid Id { get; set; }

    public byte ResetDayOfMonth { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedById { get; set; }
}
