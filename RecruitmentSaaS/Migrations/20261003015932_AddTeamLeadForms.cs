using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLeadForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TeamManagerId",
                schema: "demorecruitment",
                table: "Leads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeamLeadForms",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newsequentialid())"),
                    ManagerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    WhatsAppNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demorecruitment_TLF", x => x.Id);
                    table.ForeignKey(
                        name: "FK_demorecruitment_TLF_Mgr",
                        column: x => x.ManagerId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_Ld_TeamMgr",
                schema: "demorecruitment",
                table: "Leads",
                column: "TeamManagerId",
                filter: "([TeamManagerId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "UQ_demorecruitment_TLF_Mgr",
                schema: "demorecruitment",
                table: "TeamLeadForms",
                column: "ManagerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_demorecruitment_TLF_Slug",
                schema: "demorecruitment",
                table: "TeamLeadForms",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_demorecruitment_Ld_TeamMgr",
                schema: "demorecruitment",
                table: "Leads",
                column: "TeamManagerId",
                principalSchema: "demorecruitment",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_demorecruitment_Ld_TeamMgr",
                schema: "demorecruitment",
                table: "Leads");

            migrationBuilder.DropTable(
                name: "TeamLeadForms",
                schema: "demorecruitment");

            migrationBuilder.DropIndex(
                name: "IX_demorecruitment_Ld_TeamMgr",
                schema: "demorecruitment",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "TeamManagerId",
                schema: "demorecruitment",
                table: "Leads");
        }
    }
}
