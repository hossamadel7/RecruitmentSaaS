using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AddFormVisitEngagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxScrollPercent",
                schema: "demorecruitment",
                table: "FormVisits",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SecondsOnPage",
                schema: "demorecruitment",
                table: "FormVisits",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "WhatsAppClicked",
                schema: "demorecruitment",
                table: "FormVisits",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxScrollPercent",
                schema: "demorecruitment",
                table: "FormVisits");

            migrationBuilder.DropColumn(
                name: "SecondsOnPage",
                schema: "demorecruitment",
                table: "FormVisits");

            migrationBuilder.DropColumn(
                name: "WhatsAppClicked",
                schema: "demorecruitment",
                table: "FormVisits");
        }
    }
}
