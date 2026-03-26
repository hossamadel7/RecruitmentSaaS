using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class VwCampaignPerformance
{
    public Guid? CampaignId { get; set; }

    public long? TotalLeads { get; set; }

    public int? ConvertedLeads { get; set; }

    public long? LostLeads { get; set; }
}
