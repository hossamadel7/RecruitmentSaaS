using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Keyless]
public partial class VwCampaignPerformance
{
    public Guid? CampaignId { get; set; }

    public long? TotalLeads { get; set; }

    public int? ConvertedLeads { get; set; }

    public long? LostLeads { get; set; }
}
