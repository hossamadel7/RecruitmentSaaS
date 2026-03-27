using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("SalesGoogleSheetUsers", Schema = "demorecruitment")]
[Index("SheetId", "SalesUserId", Name = "UQ_SheetUsers_SheetUser", IsUnique = true)]
public partial class SalesGoogleSheetUser
{
    [Key]
    public Guid Id { get; set; }

    public Guid SheetId { get; set; }

    public Guid SalesUserId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    [ForeignKey("SalesUserId")]
    [InverseProperty("SalesGoogleSheetUsers")]
    public virtual User SalesUser { get; set; } = null!;

    [ForeignKey("SheetId")]
    [InverseProperty("SalesGoogleSheetUsers")]
    public virtual SalesGoogleSheet Sheet { get; set; } = null!;
}
