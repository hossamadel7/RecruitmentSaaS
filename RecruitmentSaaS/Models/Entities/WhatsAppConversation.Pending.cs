using System;

namespace RecruitmentSaaS.Models.Entities;

public partial class WhatsAppConversation
{
    /// <summary>
    /// Set when the chat's salesperson was deactivated: the chat is unassigned and waits with this
    /// team (its team leader's id) until the team leader / TeleSales manager picks someone. Cleared on assignment.
    /// </summary>
    public Guid? PendingTeamManagerId { get; set; }
}
