using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AddArabicNameAndWelcomeMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FullNameAr",
                schema: "demorecruitment",
                table: "Users",
                type: "citext",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WelcomeMessage",
                schema: "demorecruitment",
                table: "LeadFormSettings",
                type: "citext",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WelcomeMessageEnabled",
                schema: "demorecruitment",
                table: "LeadFormSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FullNameAr",
                schema: "demorecruitment",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WelcomeMessage",
                schema: "demorecruitment",
                table: "LeadFormSettings");

            migrationBuilder.DropColumn(
                name: "WelcomeMessageEnabled",
                schema: "demorecruitment",
                table: "LeadFormSettings");
        }
    }
}
