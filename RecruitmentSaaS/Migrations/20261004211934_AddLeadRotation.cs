using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeadRotationMembers",
                schema: "demorecruitment",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "citext", maxLength: 60, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Share = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demorecruitment_LRM", x => new { x.ScopeKey, x.UserId });
                    table.ForeignKey(
                        name: "FK_demorecruitment_LRM_User",
                        column: x => x.UserId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeadRotationStates",
                schema: "demorecruitment",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "citext", maxLength: 60, nullable: false),
                    CurrentUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GivenInTurn = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(0) without time zone", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demorecruitment_LRS", x => x.ScopeKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeadRotationMembers_UserId",
                schema: "demorecruitment",
                table: "LeadRotationMembers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadRotationMembers",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "LeadRotationStates",
                schema: "demorecruitment");
        }
    }
}
