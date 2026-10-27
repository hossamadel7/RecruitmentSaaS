using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class CaptureWhatsAppAdReferrals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferralBody",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                type: "citext",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReferralCampaignId",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferralClickId",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                type: "text",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferralHeadline",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                type: "citext",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferralMediaType",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                type: "citext",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferralSourceId",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                type: "text",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferralSourceType",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                type: "citext",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferralSourceUrl",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                type: "text",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Msg_ReferralCampaign",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                column: "ReferralCampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Msg_ReferralSource",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                column: "ReferralSourceId");

            migrationBuilder.AddForeignKey(
                name: "FK_demorecruitment_WA_Msg_ReferralCampaign",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                column: "ReferralCampaignId",
                principalSchema: "demorecruitment",
                principalTable: "Campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_demorecruitment_WA_Msg_ReferralCampaign",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropIndex(
                name: "IX_demorecruitment_WA_Msg_ReferralCampaign",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropIndex(
                name: "IX_demorecruitment_WA_Msg_ReferralSource",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "ReferralBody",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "ReferralCampaignId",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "ReferralClickId",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "ReferralHeadline",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "ReferralMediaType",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "ReferralSourceId",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "ReferralSourceType",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "ReferralSourceUrl",
                schema: "demorecruitment",
                table: "WhatsAppMessages");
        }
    }
}
