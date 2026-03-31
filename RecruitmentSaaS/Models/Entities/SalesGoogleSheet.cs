using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class SalesGoogleSheet
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string SpreadsheetId { get; set; } = null!;

    public string SheetName { get; set; } = null!;

    public Guid? CampaignId { get; set; }

    public bool IsActive { get; set; }

    public DateTime? LastImportedAt { get; set; }

    public int LastImportedRow { get; set; }

    public int TotalImported { get; set; }

    public Guid? CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Campaign? Campaign { get; set; }

    public virtual User? CreatedBy { get; set; }

    public virtual ICollection<Lead> Leads { get; set; } = new List<Lead>();

    public virtual ICollection<SalesGoogleSheetUser> SalesGoogleSheetUsers { get; set; } = new List<SalesGoogleSheetUser>();
}
