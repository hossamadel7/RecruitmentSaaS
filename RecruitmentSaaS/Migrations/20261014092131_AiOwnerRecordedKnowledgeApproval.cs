using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AiOwnerRecordedKnowledgeApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AiKnowledge_Approval",
                schema: "demorecruitment",
                table: "AiSalesKnowledge");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedById",
                schema: "demorecruitment",
                table: "AiSalesKnowledge",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "OwnerApprovalReference",
                schema: "demorecruitment",
                table: "AiSalesKnowledge",
                type: "citext",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiKnowledge_Approval",
                schema: "demorecruitment",
                table: "AiSalesKnowledge",
                sql: "\"Status\" <> 1 OR (\"ApprovedAt\" IS NOT NULL AND (\"ApprovedById\" IS NOT NULL OR coalesce(length(trim(\"OwnerApprovalReference\"::text)) > 0, false)))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM demorecruitment.\"AiSalesKnowledge\" WHERE \"CreatedById\" IS NULL OR (\"Status\" = 1 AND \"ApprovedById\" IS NULL)) THEN RAISE EXCEPTION 'Owner-imported knowledge must be preserved; rollback requires explicit reconciliation'; END IF; END $$;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_AiKnowledge_Approval",
                schema: "demorecruitment",
                table: "AiSalesKnowledge");

            migrationBuilder.DropColumn(
                name: "OwnerApprovalReference",
                schema: "demorecruitment",
                table: "AiSalesKnowledge");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedById",
                schema: "demorecruitment",
                table: "AiSalesKnowledge",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiKnowledge_Approval",
                schema: "demorecruitment",
                table: "AiSalesKnowledge",
                sql: "\"Status\" <> 1 OR (\"ApprovedById\" IS NOT NULL AND \"ApprovedAt\" IS NOT NULL)");
        }
    }
}

