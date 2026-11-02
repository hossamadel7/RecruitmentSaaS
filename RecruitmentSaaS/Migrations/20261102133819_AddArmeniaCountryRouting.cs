using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AddArmeniaCountryRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "demorecruitment",
                table: "CountryRoutingRules",
                columns: new[] { "Country", "Enabled", "FallbackCountry", "MaxAge", "MinAge" },
                values: new object[] { "ارمينيا", false, null, 80, 18 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "demorecruitment",
                table: "CountryRoutingRules",
                keyColumn: "Country",
                keyValue: "ارمينيا");
        }
    }
}
