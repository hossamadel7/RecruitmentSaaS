using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AiReviewedDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SenderAiSalesAgentId",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttemptedAt",
                schema: "demorecruitment",
                table: "AiOutboundIntents",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                schema: "demorecruitment",
                table: "AiOutboundIntents",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DraftId",
                schema: "demorecruitment",
                table: "AiOutboundIntents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                schema: "demorecruitment",
                table: "AiOutboundIntents",
                type: "citext",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MessageId",
                schema: "demorecruitment",
                table: "AiOutboundIntents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedById",
                schema: "demorecruitment",
                table: "AiOutboundIntents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_SenderAiSalesAgentId",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                column: "SenderAiSalesAgentId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Message_SalesAuthor",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                sql: "\"SenderUserId\" IS NULL OR \"SenderAiSalesAgentId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiOutboundIntents_DraftId",
                schema: "demorecruitment",
                table: "AiOutboundIntents",
                column: "DraftId",
                unique: true,
                filter: "\"DraftId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiOutboundIntents_MessageId",
                schema: "demorecruitment",
                table: "AiOutboundIntents",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOutboundIntents_RequestedById",
                schema: "demorecruitment",
                table: "AiOutboundIntents",
                column: "RequestedById");

            migrationBuilder.AddForeignKey(
                name: "FK_AiOutboundIntents_AiSalesDrafts_DraftId",
                schema: "demorecruitment",
                table: "AiOutboundIntents",
                column: "DraftId",
                principalSchema: "demorecruitment",
                principalTable: "AiSalesDrafts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AiOutboundIntents_Users_RequestedById",
                schema: "demorecruitment",
                table: "AiOutboundIntents",
                column: "RequestedById",
                principalSchema: "demorecruitment",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AiOutboundIntents_WhatsAppMessages_MessageId",
                schema: "demorecruitment",
                table: "AiOutboundIntents",
                column: "MessageId",
                principalSchema: "demorecruitment",
                principalTable: "WhatsAppMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppMessages_AiSalesAgents_SenderAiSalesAgentId",
                schema: "demorecruitment",
                table: "WhatsAppMessages",
                column: "SenderAiSalesAgentId",
                principalSchema: "demorecruitment",
                principalTable: "AiSalesAgents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiOutboundIntents_AiSalesDrafts_DraftId",
                schema: "demorecruitment",
                table: "AiOutboundIntents");

            migrationBuilder.DropForeignKey(
                name: "FK_AiOutboundIntents_Users_RequestedById",
                schema: "demorecruitment",
                table: "AiOutboundIntents");

            migrationBuilder.DropForeignKey(
                name: "FK_AiOutboundIntents_WhatsAppMessages_MessageId",
                schema: "demorecruitment",
                table: "AiOutboundIntents");

            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppMessages_AiSalesAgents_SenderAiSalesAgentId",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_SenderAiSalesAgentId",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Message_SalesAuthor",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropIndex(
                name: "IX_AiOutboundIntents_DraftId",
                schema: "demorecruitment",
                table: "AiOutboundIntents");

            migrationBuilder.DropIndex(
                name: "IX_AiOutboundIntents_MessageId",
                schema: "demorecruitment",
                table: "AiOutboundIntents");

            migrationBuilder.DropIndex(
                name: "IX_AiOutboundIntents_RequestedById",
                schema: "demorecruitment",
                table: "AiOutboundIntents");

            migrationBuilder.DropColumn(
                name: "SenderAiSalesAgentId",
                schema: "demorecruitment",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "AttemptedAt",
                schema: "demorecruitment",
                table: "AiOutboundIntents");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                schema: "demorecruitment",
                table: "AiOutboundIntents");

            migrationBuilder.DropColumn(
                name: "DraftId",
                schema: "demorecruitment",
                table: "AiOutboundIntents");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                schema: "demorecruitment",
                table: "AiOutboundIntents");

            migrationBuilder.DropColumn(
                name: "MessageId",
                schema: "demorecruitment",
                table: "AiOutboundIntents");

            migrationBuilder.DropColumn(
                name: "RequestedById",
                schema: "demorecruitment",
                table: "AiOutboundIntents");
        }
    }
}
