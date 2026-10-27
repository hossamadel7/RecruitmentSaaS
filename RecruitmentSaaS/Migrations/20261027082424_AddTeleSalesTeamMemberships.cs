using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AddTeleSalesTeamMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeleSalesTeamMemberships",
                schema: "demorecruitment",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeleSalesTeamMemberships", x => new { x.UserId, x.ManagerId });
                    table.ForeignKey(
                        name: "FK_TeleSalesTeamMemberships_Users_ManagerId",
                        column: x => x.ManagerId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeleSalesTeamMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeleSalesTeamMemberships_ManagerId",
                schema: "demorecruitment",
                table: "TeleSalesTeamMemberships",
                column: "ManagerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeleSalesTeamMemberships",
                schema: "demorecruitment");
        }
    }
}
