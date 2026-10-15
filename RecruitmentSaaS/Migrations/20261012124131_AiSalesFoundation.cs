using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AiSalesFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedAiSalesAgentId",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedAiSalesAgentId",
                schema: "demorecruitment",
                table: "Leads",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiControlAudits",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Event = table.Column<string>(type: "citext", maxLength: 60, nullable: false),
                    Reason = table.Column<string>(type: "citext", maxLength: 500, nullable: false),
                    OwnershipVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiControlAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiControlAudits_WhatsAppConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiControlRequests",
                schema: "demorecruitment",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "text", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiControlRequests", x => new { x.TenantId, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "AiOutboundIntents",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnershipVersion = table.Column<long>(type: "bigint", nullable: false),
                    OperationKey = table.Column<string>(type: "text", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiOutboundIntents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiOutboundIntents_WhatsAppConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiSalesAgents",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamManagerId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    Mode = table.Column<byte>(type: "smallint", nullable: false),
                    Paused = table.Column<bool>(type: "boolean", nullable: false),
                    MaxActiveConversations = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSalesAgents", x => x.Id);
                    table.CheckConstraint("CK_AiAgent_Limit", "\"MaxActiveConversations\" BETWEEN 1 AND 1000");
                    table.CheckConstraint("CK_AiAgent_Mode", "\"Mode\" IN (0,1)");
                    table.ForeignKey(
                        name: "FK_AiSalesAgents_Users_TeamManagerId",
                        column: x => x.TeamManagerId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiSalesAgents_WhatsAppAccounts_WhatsAppAccountId",
                        column: x => x.WhatsAppAccountId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConversationControls",
                schema: "demorecruitment",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Controller = table.Column<byte>(type: "smallint", nullable: false),
                    OwnershipVersion = table.Column<long>(type: "bigint", nullable: false),
                    LastActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "citext", maxLength: 500, nullable: false),
                    HandoffSummary = table.Column<string>(type: "citext", maxLength: 2000, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationControls", x => x.ConversationId);
                    table.ForeignKey(
                        name: "FK_ConversationControls_WhatsAppConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DurableWorkItems",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "text", maxLength: 160, nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "citext", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DurableWorkItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiLeadAssignments",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedById = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousSalesId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousConversationSalesId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "citext", maxLength: 500, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiLeadAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiLeadAssignments_AiSalesAgents_AgentId",
                        column: x => x.AgentId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiSalesAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiLeadAssignments_Leads_LeadId",
                        column: x => x.LeadId,
                        principalSchema: "demorecruitment",
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiLeadAssignments_Users_AssignedById",
                        column: x => x.AssignedById,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiLeadAssignments_WhatsAppConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversations_AssignedAiSalesAgentId",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                column: "AssignedAiSalesAgentId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Conversation_SalesOwner",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                sql: "\"AssignedSalesAgentId\" IS NULL OR \"AssignedAiSalesAgentId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_AssignedAiSalesAgentId",
                schema: "demorecruitment",
                table: "Leads",
                column: "AssignedAiSalesAgentId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Lead_SalesOwner",
                schema: "demorecruitment",
                table: "Leads",
                sql: "\"AssignedSalesId\" IS NULL OR \"AssignedAiSalesAgentId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiControlAudits_ConversationId",
                schema: "demorecruitment",
                table: "AiControlAudits",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_AiControlAudits_TenantId_ConversationId_CreatedAt",
                schema: "demorecruitment",
                table: "AiControlAudits",
                columns: new[] { "TenantId", "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiLeadAssignments_AgentId",
                schema: "demorecruitment",
                table: "AiLeadAssignments",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AiLeadAssignments_AssignedById",
                schema: "demorecruitment",
                table: "AiLeadAssignments",
                column: "AssignedById");

            migrationBuilder.CreateIndex(
                name: "IX_AiLeadAssignments_ConversationId",
                schema: "demorecruitment",
                table: "AiLeadAssignments",
                column: "ConversationId",
                unique: true,
                filter: "\"EndedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiLeadAssignments_LeadId",
                schema: "demorecruitment",
                table: "AiLeadAssignments",
                column: "LeadId",
                unique: true,
                filter: "\"EndedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiOutboundIntents_ConversationId",
                schema: "demorecruitment",
                table: "AiOutboundIntents",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOutboundIntents_TenantId_OperationKey",
                schema: "demorecruitment",
                table: "AiOutboundIntents",
                columns: new[] { "TenantId", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesAgents_TeamManagerId",
                schema: "demorecruitment",
                table: "AiSalesAgents",
                column: "TeamManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesAgents_TenantId_WhatsAppAccountId",
                schema: "demorecruitment",
                table: "AiSalesAgents",
                columns: new[] { "TenantId", "WhatsAppAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesAgents_WhatsAppAccountId",
                schema: "demorecruitment",
                table: "AiSalesAgents",
                column: "WhatsAppAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DurableWorkItems_AvailableAt",
                schema: "demorecruitment",
                table: "DurableWorkItems",
                column: "AvailableAt",
                filter: "\"CompletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DurableWorkItems_DeduplicationKey",
                schema: "demorecruitment",
                table: "DurableWorkItems",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Leads_AiSalesAgents_AssignedAiSalesAgentId",
                schema: "demorecruitment",
                table: "Leads",
                column: "AssignedAiSalesAgentId",
                principalSchema: "demorecruitment",
                principalTable: "AiSalesAgents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppConversations_AiSalesAgents_AssignedAiSalesAgentId",
                schema: "demorecruitment",
                table: "WhatsAppConversations",
                column: "AssignedAiSalesAgentId",
                principalSchema: "demorecruitment",
                principalTable: "AiSalesAgents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leads_AiSalesAgents_AssignedAiSalesAgentId",
                schema: "demorecruitment",
                table: "Leads");

            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppConversations_AiSalesAgents_AssignedAiSalesAgentId",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropTable(
                name: "AiControlAudits",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiControlRequests",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiLeadAssignments",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiOutboundIntents",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "ConversationControls",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "DurableWorkItems",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiSalesAgents",
                schema: "demorecruitment");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppConversations_AssignedAiSalesAgentId",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Conversation_SalesOwner",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropIndex(
                name: "IX_Leads_AssignedAiSalesAgentId",
                schema: "demorecruitment",
                table: "Leads");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Lead_SalesOwner",
                schema: "demorecruitment",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "AssignedAiSalesAgentId",
                schema: "demorecruitment",
                table: "WhatsAppConversations");

            migrationBuilder.DropColumn(
                name: "AssignedAiSalesAgentId",
                schema: "demorecruitment",
                table: "Leads");
        }
    }
}
