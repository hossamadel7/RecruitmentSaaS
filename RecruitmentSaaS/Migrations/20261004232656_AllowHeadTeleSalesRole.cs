using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <summary>Role 8 = head TeleSales manager (own leads + every team). The users table only allowed 1–7.</summary>
    public partial class AllowHeadTeleSalesRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE demorecruitment."Users" DROP CONSTRAINT IF EXISTS "CK_platform_Us_Role";
                ALTER TABLE demorecruitment."Users" ADD CONSTRAINT "CK_platform_Us_Role" CHECK ("Role" BETWEEN 1 AND 8);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE demorecruitment."Users" DROP CONSTRAINT IF EXISTS "CK_platform_Us_Role";
                ALTER TABLE demorecruitment."Users" ADD CONSTRAINT "CK_platform_Us_Role" CHECK ("Role" BETWEEN 1 AND 7);
                """);
        }
    }
}
