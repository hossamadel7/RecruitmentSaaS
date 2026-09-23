using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AddMetaEmbeddedSignup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "MessageSource",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<byte>(
                name: "ConnectionMode",
                schema: "demorecruitment",
                table: "WhatsAppAccounts",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedName",
                schema: "demorecruitment",
                table: "WhatsAppAccounts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "WebhookSubscriptionStatus",
                schema: "demorecruitment",
                table: "WhatsAppAccounts",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.CreateTable(
                name: "MetaSystemCredentials",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newsequentialid())"),
                    AccessToken = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ObtainedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demorecruitment_Meta_Cred", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetaSystemCredentials",
                schema: "demorecruitment");

            migrationBuilder.DropColumn(
                name: "MessageSource",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "ConnectionMode",
                schema: "demorecruitment",
                table: "WhatsAppAccounts");

            migrationBuilder.DropColumn(
                name: "VerifiedName",
                schema: "demorecruitment",
                table: "WhatsAppAccounts");

            migrationBuilder.DropColumn(
                name: "WebhookSubscriptionStatus",
                schema: "demorecruitment",
                table: "WhatsAppAccounts");
        }
    }
}
