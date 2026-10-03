using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadAgeAndLeadFormSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Age",
                schema: "demorecruitment",
                table: "Leads",
                type: "tinyint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LeadFormSettings",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newsequentialid())"),
                    SeniorAgeThreshold = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)45),
                    SalesWhatsAppNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SalesWhatsAppMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demorecruitment_LFS", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadFormSettings",
                schema: "demorecruitment");

            migrationBuilder.DropColumn(
                name: "Age",
                schema: "demorecruitment",
                table: "Leads");
        }
    }
}
