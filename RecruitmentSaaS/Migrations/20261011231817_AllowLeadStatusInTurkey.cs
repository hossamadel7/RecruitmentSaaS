using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AllowLeadStatusInTurkey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Lead status 9 = متواجد في تركيا
            migrationBuilder.Sql("""
                ALTER TABLE demorecruitment."Leads" DROP CONSTRAINT IF EXISTS "CK_demorecruitment_Ld_St";
                ALTER TABLE demorecruitment."Leads" ADD CONSTRAINT "CK_demorecruitment_Ld_St" CHECK ("Status" BETWEEN 1 AND 9);
                ALTER TABLE demorecruitment."LeadFunnelHistory" DROP CONSTRAINT IF EXISTS "CK_demorecruitment_LFH_To";
                ALTER TABLE demorecruitment."LeadFunnelHistory" ADD CONSTRAINT "CK_demorecruitment_LFH_To" CHECK ("ToStatus" BETWEEN 1 AND 9);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE demorecruitment."Leads" SET "Status" = 8 WHERE "Status" = 9;
                DELETE FROM demorecruitment."LeadFunnelHistory" WHERE "ToStatus" = 9;
                ALTER TABLE demorecruitment."Leads" DROP CONSTRAINT IF EXISTS "CK_demorecruitment_Ld_St";
                ALTER TABLE demorecruitment."Leads" ADD CONSTRAINT "CK_demorecruitment_Ld_St" CHECK ("Status" BETWEEN 1 AND 8);
                ALTER TABLE demorecruitment."LeadFunnelHistory" DROP CONSTRAINT IF EXISTS "CK_demorecruitment_LFH_To";
                ALTER TABLE demorecruitment."LeadFunnelHistory" ADD CONSTRAINT "CK_demorecruitment_LFH_To" CHECK ("ToStatus" BETWEEN 1 AND 8);
                """);
        }
    }
}
