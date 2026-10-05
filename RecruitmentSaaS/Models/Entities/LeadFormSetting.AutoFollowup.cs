using System;

namespace RecruitmentSaaS.Models.Entities;

public partial class LeadFormSetting
{
    public const int DefaultAutoFollowupDelayMinutes = 2;

    /// <summary>
    /// Send the approved opening template automatically to website leads at/above the age threshold
    /// who were sent to WhatsApp but didn't start the chat within <see cref="AutoFollowupDelayMinutes"/>.
    /// </summary>
    public bool AutoFollowupEnabled { get; set; }

    public int AutoFollowupDelayMinutes { get; set; }

    /// <summary>When it was last switched on — leads from before that are never chased.</summary>
    public DateTime? AutoFollowupEnabledAt { get; set; }
}
