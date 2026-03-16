using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class LeadVisit
{
    public Guid Id { get; set; }

    public Guid LeadId { get; set; }

    public Guid BranchId { get; set; }

    public Guid ReceptionUserId { get; set; }

    public Guid? AssignedSalesUserId { get; set; }

    public DateTime VisitDateTime { get; set; }

    public byte MeetingOutcome { get; set; }

    public Guid? ConvertedCandidateId { get; set; }

    public Guid? JobPackageId { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User? AssignedSalesUser { get; set; }

    public virtual Branch Branch { get; set; } = null!;

    public virtual JobPackage? JobPackage { get; set; }

    public virtual Lead Lead { get; set; } = null!;

    public virtual User ReceptionUser { get; set; } = null!;
}
