using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RecruitmentSaaS.Data;

namespace RecruitmentSaaS.Migrations;

[DbContext(typeof(RecruitmentCrmContext))]
[Migration("20261022030000_AllowTeamMonitoringRole")]
public sealed class AllowTeamMonitoringRole : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE demorecruitment."Users" DROP CONSTRAINT IF EXISTS "CK_platform_Us_Role";
        ALTER TABLE demorecruitment."Users" ADD CONSTRAINT "CK_platform_Us_Role" CHECK ("Role" BETWEEN 1 AND 9);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM demorecruitment."Users" WHERE "Role" = 9) THEN
                RAISE EXCEPTION 'Reassign monitoring users before removing the monitoring role';
            END IF;
        END $$;
        ALTER TABLE demorecruitment."Users" DROP CONSTRAINT IF EXISTS "CK_platform_Us_Role";
        ALTER TABLE demorecruitment."Users" ADD CONSTRAINT "CK_platform_Us_Role" CHECK ("Role" BETWEEN 1 AND 8);
        """);
}
