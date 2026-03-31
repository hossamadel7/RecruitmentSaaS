using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class Campaign
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public byte Source { get; set; }

    public byte Status { get; set; }

    public decimal? BudgetEgp { get; set; }

    public decimal? SpendEgp { get; set; }

    public string? FacebookCampaignId { get; set; }

    public string? FacebookAdSetId { get; set; }

    public string? FacebookAdId { get; set; }

    public string? UtmSource { get; set; }

    public string? UtmMedium { get; set; }

    public string? UtmCampaign { get; set; }

    public string? UtmContent { get; set; }

    public string? UtmTerm { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public Guid CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User CreatedBy { get; set; } = null!;

    public virtual ICollection<Lead> Leads { get; set; } = new List<Lead>();

    public virtual ICollection<SalesGoogleSheet> SalesGoogleSheets { get; set; } = new List<SalesGoogleSheet>();
}
