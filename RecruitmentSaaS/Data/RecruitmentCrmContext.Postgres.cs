using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Data;

// PostgreSQL-specific model conventions, kept apart from the scaffolded model.
public partial class RecruitmentCrmContext
{
    private static void ApplyPostgresConventions(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");

        // The database was SQL Server with a case-insensitive collation (SQL_Latin1_General_CP1_CI_AS):
        // logins, searches, duplicate-phone checks and unique indexes all ignored letter case.
        // Every text column is citext in PostgreSQL to keep that behaviour; declaring it here makes
        // EF send string parameters as citext too, so comparisons stay case-insensitive.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                if (property.ClrType == typeof(string))
                    property.SetColumnType("citext");
    }
}
