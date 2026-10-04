using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AddChatPendingTeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PendingTeamManagerId",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_demorecruitment_WA_Cnv_PendTeam",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                column: "PendingTeamManagerId",
                filter: "(\"PendingTeamManagerId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_demorecruitment_WA_Cnv_PendTeam",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                column: "PendingTeamManagerId",
                principalSchema: "demorecruitment",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_demorecruitment_WA_Cnv_PendTeam",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropIndex(
                name: "IX_demorecruitment_WA_Cnv_PendTeam",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropColumn(
                name: "PendingTeamManagerId",
                schema: "demorecruitment",
                table: "WhatsAppConversations");
        }
    }
}
