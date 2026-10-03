using System;

namespace RecruitmentSaaS.Models.Entities;

// Kept out of the scaffolded Lead.cs so a re-scaffold doesn't drop it.
public partial class Lead
{
    public byte? Age { get; set; }

    /// <summary>
    /// Set when the lead came from a team's registration form. While unassigned, such a
    /// lead is visible only to that TeleSales Manager — never in the shared TeleSales Pool.
    /// </summary>
    public Guid? TeamManagerId { get; set; }

    public virtual User? TeamManager { get; set; }
}
