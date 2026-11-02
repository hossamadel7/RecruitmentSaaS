using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AddCountryAdRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdCountry",
                schema: "demorecruitment",
                table: "Leads",
                type: "citext",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdText",
                schema: "demorecruitment",
                table: "Leads",
                type: "citext",
                maxLength: 3000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoutedCountry",
                schema: "demorecruitment",
                table: "Leads",
                type: "citext",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CountryRoutingRules",
                schema: "demorecruitment",
                columns: table => new
                {
                    Country = table.Column<string>(type: "citext", maxLength: 20, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    MinAge = table.Column<int>(type: "integer", nullable: false),
                    MaxAge = table.Column<int>(type: "integer", nullable: false),
                    FallbackCountry = table.Column<string>(type: "citext", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountryRoutingRules", x => x.Country);
                });

            migrationBuilder.InsertData(
                schema: "demorecruitment",
                table: "CountryRoutingRules",
                columns: new[] { "Country", "Enabled", "FallbackCountry", "MaxAge", "MinAge" },
                values: new object[,]
                {
                    { "تركيا", false, null, 80, 18 },
                    { "سعودية", false, null, 80, 18 },
                    { "عمان", false, null, 80, 18 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CountryRoutingRules",
                schema: "demorecruitment");

            migrationBuilder.DropColumn(
                name: "AdCountry",
                schema: "demorecruitment",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "AdText",
                schema: "demorecruitment",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "RoutedCountry",
                schema: "demorecruitment",
                table: "Leads");
        }
    }
}
