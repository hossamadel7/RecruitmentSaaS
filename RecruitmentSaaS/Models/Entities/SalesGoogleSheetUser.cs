using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class SalesGoogleSheetUser
{
    public Guid Id { get; set; }

    public Guid SheetId { get; set; }

    public Guid SalesUserId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User SalesUser { get; set; } = null!;

    public virtual SalesGoogleSheet Sheet { get; set; } = null!;
}
