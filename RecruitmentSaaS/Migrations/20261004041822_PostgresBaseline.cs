using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <summary>
    /// Baseline for the move from SQL Server to PostgreSQL. The schema, data, functions, triggers and views were
    /// created by migration/migrate.sh (see migration/pg), which also records this migration as applied, so it
    /// intentionally does nothing. Its model snapshot is what later migrations are diffed against.
    /// </summary>
    public partial class PostgresBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
