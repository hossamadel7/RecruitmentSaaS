using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class VwLeadFunnelSummary
{
    public Guid BranchId { get; set; }

    public byte LeadSource { get; set; }

    public byte Status { get; set; }

    public bool IsConverted { get; set; }

    public long? LeadCount { get; set; }
}
