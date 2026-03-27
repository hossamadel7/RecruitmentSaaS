using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("SalesGoogleSheets", Schema = "demorecruitment")]
public partial class SalesGoogleSheet
{
    [Key]
    public Guid Id { get; set; }

    [StringLength(200)]
    public string Name { get; set; } = null!;

    [StringLength(200)]
    public string SpreadsheetId { get; set; } = null!;

    [StringLength(200)]
    public string SheetName { get; set; } = null!;

    public Guid? CampaignId { get; set; }

    public bool IsActive { get; set; }

    public DateTime? LastImportedAt { get; set; }

    public int LastImportedRow { get; set; }

    public int TotalImported { get; set; }

    public Guid? CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }

    [ForeignKey("CampaignId")]
    [InverseProperty("SalesGoogleSheets")]
    public virtual Campaign? Campaign { get; set; }

    [ForeignKey("CreatedById")]
    [InverseProperty("SalesGoogleSheets")]
    public virtual User? CreatedBy { get; set; }

    [InverseProperty("GoogleSheet")]
    public virtual ICollection<Lead> Leads { get; set; } = new List<Lead>();

    [InverseProperty("Sheet")]
    public virtual ICollection<SalesGoogleSheetUser> SalesGoogleSheetUsers { get; set; } = new List<SalesGoogleSheetUser>();
}
