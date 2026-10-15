using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RecruitmentSaaS.Data;

/// <summary>Migration generation never loads deployment secrets or starts hosted workers.</summary>
public sealed class RecruitmentCrmDesignTimeFactory : IDesignTimeDbContextFactory<RecruitmentCrmContext>
{
    public RecruitmentCrmContext CreateDbContext(string[] args)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        return new(new DbContextOptionsBuilder<RecruitmentCrmContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=design_only;Username=design_only;Timeout=1").Options);
    }
}
