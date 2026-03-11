using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Keyless]
public partial class VwLeadFunnelSummary
{
    public Guid BranchId { get; set; }

    public byte LeadSource { get; set; }

    public byte Status { get; set; }

    public bool IsConverted { get; set; }

    public long? LeadCount { get; set; }
}
