using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AddFormVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormVisits",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(0) without time zone", precision: 0, nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp(0) without time zone", precision: 0, nullable: false),
                    Page = table.Column<string>(type: "citext", maxLength: 20, nullable: false),
                    TeamSlug = table.Column<string>(type: "citext", maxLength: 50, nullable: true),
                    Source = table.Column<string>(type: "citext", maxLength: 20, nullable: false),
                    UtmCampaign = table.Column<string>(type: "citext", maxLength: 100, nullable: true),
                    Device = table.Column<string>(type: "citext", maxLength: 20, nullable: false),
                    FieldsTouched = table.Column<string>(type: "citext", maxLength: 100, nullable: true),
                    LastField = table.Column<string>(type: "citext", maxLength: 20, nullable: true),
                    ErrorCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "citext", maxLength: 200, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp(0) without time zone", precision: 0, nullable: true),
                    Outcome = table.Column<byte>(type: "smallint", nullable: true),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: true),
                    WhatsAppRedirect = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demorecruitment_FormVisits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_FormVisits_Created",
                schema: "demorecruitment",
                table: "FormVisits",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormVisits",
                schema: "demorecruitment");
        }
    }
}
