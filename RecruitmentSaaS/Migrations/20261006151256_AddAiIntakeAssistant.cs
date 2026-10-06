using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AddAiIntakeAssistant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "IntakeAge",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IntakeAgeAt",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                type: "timestamp(0) without time zone",
                precision: 0,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntakeHandoffReason",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                type: "citext",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntakeJob",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                type: "citext",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IntakeLastCustomerAt",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                type: "timestamp(0) without time zone",
                precision: 0,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntakeName",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                type: "citext",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IntakeNoTextCount",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "IntakeStartedAt",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                type: "timestamp(0) without time zone",
                precision: 0,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "IntakeStatus",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "IntakeTurns",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AiAssistantEnabled",
                schema: "demorecruitment",
                table: "WhatsAppAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AiEnabled",
                schema: "demorecruitment",
                table: "LeadFormSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AiGreeting",
                schema: "demorecruitment",
                table: "LeadFormSettings",
                type: "citext",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiTestMode",
                schema: "demorecruitment",
                table: "LeadFormSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "AiTestNumbers",
                schema: "demorecruitment",
                table: "LeadFormSettings",
                type: "citext",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiUnderAgeToRotation",
                schema: "demorecruitment",
                table: "LeadFormSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AiWaitAfterAgeMinutes",
                schema: "demorecruitment",
                table: "LeadFormSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AiWaitNoAgeHours",
                schema: "demorecruitment",
                table: "LeadFormSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Cnv_Intake",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                column: "IntakeStatus",
                filter: "(\"IntakeStatus\" = 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_demorecruitment_WA_Cnv_Intake",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropColumn(
                name: "IntakeAge",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropColumn(
                name: "IntakeAgeAt",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropColumn(
                name: "IntakeHandoffReason",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropColumn(
                name: "IntakeJob",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropColumn(
                name: "IntakeLastCustomerAt",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropColumn(
                name: "IntakeName",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropColumn(
                name: "IntakeNoTextCount",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropColumn(
                name: "IntakeStartedAt",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropColumn(
                name: "IntakeStatus",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropColumn(
                name: "IntakeTurns",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropColumn(
                name: "AiAssistantEnabled",
                schema: "demorecruitment",
                table: "WhatsAppAccounts");

            migrationBuilder.DropColumn(
                name: "AiEnabled",
                schema: "demorecruitment",
                table: "LeadFormSettings");

            migrationBuilder.DropColumn(
                name: "AiGreeting",
                schema: "demorecruitment",
                table: "LeadFormSettings");

            migrationBuilder.DropColumn(
                name: "AiTestMode",
                schema: "demorecruitment",
                table: "LeadFormSettings");

            migrationBuilder.DropColumn(
                name: "AiTestNumbers",
                schema: "demorecruitment",
                table: "LeadFormSettings");

            migrationBuilder.DropColumn(
                name: "AiUnderAgeToRotation",
                schema: "demorecruitment",
                table: "LeadFormSettings");

            migrationBuilder.DropColumn(
                name: "AiWaitAfterAgeMinutes",
                schema: "demorecruitment",
                table: "LeadFormSettings");

            migrationBuilder.DropColumn(
                name: "AiWaitNoAgeHours",
                schema: "demorecruitment",
                table: "LeadFormSettings");
        }
    }
}
