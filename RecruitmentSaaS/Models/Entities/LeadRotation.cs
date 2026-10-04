using System;

namespace RecruitmentSaaS.Models.Entities;

/// <summary>
/// One person's place in a lead rotation: their turn comes at <see cref="Position"/> and lasts
/// <see cref="Share"/> leads (0 = paused). Scope "all" is the general rotation (website form,
/// Facebook, Google Sheets); "team:{managerId}" is that manager's team-form rotation.
/// TeleSales with no row here take 1 lead per turn, after everyone who has one.
/// </summary>
public partial class LeadRotationMember
{
    public string ScopeKey { get; set; } = null!;

    public Guid UserId { get; set; }

    public int Position { get; set; }

    public short Share { get; set; }

    public virtual User User { get; set; } = null!;
}

/// <summary>Whose turn it is in a rotation, and how many leads they've had in this turn.</summary>
public partial class LeadRotationState
{
    public string ScopeKey { get; set; } = null!;

    public Guid? CurrentUserId { get; set; }

    public int GivenInTurn { get; set; }

    public DateTime UpdatedAt { get; set; }
}
