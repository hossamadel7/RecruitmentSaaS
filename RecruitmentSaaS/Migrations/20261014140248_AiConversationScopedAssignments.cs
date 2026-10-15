using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AiConversationScopedAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiLeadAssignments_LeadId",
                schema: "demorecruitment",
                table: "AiLeadAssignments");

            migrationBuilder.AlterColumn<Guid>(
                name: "AssignedById",
                schema: "demorecruitment",
                table: "AiLeadAssignments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "ConversationOnly",
                schema: "demorecruitment",
                table: "AiLeadAssignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                schema: "demorecruitment",
                table: "AiLeadAssignments",
                type: "citext",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiLeadAssignments_LeadId",
                schema: "demorecruitment",
                table: "AiLeadAssignments",
                column: "LeadId",
                unique: true,
                filter: "\"EndedAt\" IS NULL AND NOT \"ConversationOnly\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiAssignment_Actor",
                schema: "demorecruitment",
                table: "AiLeadAssignments",
                sql: "\"AssignedById\" IS NOT NULL OR coalesce(length(trim(\"SourceReference\"::text)) > 0, false)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM demorecruitment.\"AiLeadAssignments\" WHERE \"AssignedById\" IS NULL OR \"ConversationOnly\") THEN RAISE EXCEPTION 'Scoped/system assignments require explicit reconciliation before rollback'; END IF; END $$;");
            migrationBuilder.DropIndex(
                name: "IX_AiLeadAssignments_LeadId",
                schema: "demorecruitment",
                table: "AiLeadAssignments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiAssignment_Actor",
                schema: "demorecruitment",
                table: "AiLeadAssignments");

            migrationBuilder.DropColumn(
                name: "ConversationOnly",
                schema: "demorecruitment",
                table: "AiLeadAssignments");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                schema: "demorecruitment",
                table: "AiLeadAssignments");

            migrationBuilder.AlterColumn<Guid>(
                name: "AssignedById",
                schema: "demorecruitment",
                table: "AiLeadAssignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiLeadAssignments_LeadId",
                schema: "demorecruitment",
                table: "AiLeadAssignments",
                column: "LeadId",
                unique: true,
                filter: "\"EndedAt\" IS NULL");
        }
    }
}
