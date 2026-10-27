namespace RecruitmentSaaS.Models.Entities;

// Additional teams; User.ManagerId remains the primary team for legacy workflows.
public class TeleSalesTeamMembership
{
    public Guid UserId { get; set; }
    public Guid ManagerId { get; set; }
    public User User { get; set; } = null!;
    public User Manager { get; set; } = null!;
}
