using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AiAdminWhatsAppCoaching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "LeadId",
                schema: "demorecruitment",
                table: "AiLeadAssignments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "AiAdminChannels",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "citext", maxLength: 16, nullable: false),
                    VerificationHash = table.Column<string>(type: "citext", maxLength: 64, nullable: false),
                    VerificationExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    VerificationAttempts = table.Column<int>(type: "integer", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Paused = table.Column<bool>(type: "boolean", nullable: false),
                    LastInboundAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastNotificationAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAdminChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiAdminChannels_Users_AdminUserId",
                        column: x => x.AdminUserId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiAdminChannels_WhatsAppAccounts_WhatsAppAccountId",
                        column: x => x.WhatsAppAccountId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiAdminClarifications",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnershipVersion = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "citext", maxLength: 20, nullable: false),
                    Question = table.Column<string>(type: "citext", maxLength: 2000, nullable: false),
                    Answer = table.Column<string>(type: "citext", maxLength: 3000, nullable: false),
                    AnswerScope = table.Column<byte>(type: "smallint", nullable: false),
                    State = table.Column<byte>(type: "smallint", nullable: false),
                    AnswerMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    KnowledgeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReplyDraftId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Deadline = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    AnswerValidUntil = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ErrorCode = table.Column<string>(type: "citext", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAdminClarifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiAdminClarifications_AiAdminChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiAdminChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiAdminClarifications_AiSalesAgents_AgentId",
                        column: x => x.AgentId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiSalesAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiAdminClarifications_AiSalesDrafts_ReplyDraftId",
                        column: x => x.ReplyDraftId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiSalesDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AiAdminClarifications_AiSalesKnowledge_KnowledgeId",
                        column: x => x.KnowledgeId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiSalesKnowledge",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AiAdminClarifications_WhatsAppConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiAdminClarifications_WhatsAppMessages_CustomerMessageId",
                        column: x => x.CustomerMessageId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiAdminMessages",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    WhatsAppMessageId = table.Column<string>(type: "citext", maxLength: 100, nullable: true),
                    OperationKey = table.Column<string>(type: "citext", maxLength: 160, nullable: false),
                    Incoming = table.Column<bool>(type: "boolean", nullable: false),
                    IsTemplate = table.Column<bool>(type: "boolean", nullable: false),
                    Text = table.Column<string>(type: "citext", maxLength: 4000, nullable: false),
                    State = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    ErrorCode = table.Column<string>(type: "citext", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAdminMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiAdminMessages_AiAdminChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiAdminChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiAdminMessages_AiAdminClarifications_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiAdminClarifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminChannels_AdminUserId",
                schema: "demorecruitment",
                table: "AiAdminChannels",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminChannels_TenantId_WhatsAppAccountId",
                schema: "demorecruitment",
                table: "AiAdminChannels",
                columns: new[] { "TenantId", "WhatsAppAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminChannels_WhatsAppAccountId_Phone",
                schema: "demorecruitment",
                table: "AiAdminChannels",
                columns: new[] { "WhatsAppAccountId", "Phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminClarifications_AgentId",
                schema: "demorecruitment",
                table: "AiAdminClarifications",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminClarifications_ChannelId",
                schema: "demorecruitment",
                table: "AiAdminClarifications",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminClarifications_Code",
                schema: "demorecruitment",
                table: "AiAdminClarifications",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminClarifications_ConversationId",
                schema: "demorecruitment",
                table: "AiAdminClarifications",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminClarifications_CustomerMessageId",
                schema: "demorecruitment",
                table: "AiAdminClarifications",
                column: "CustomerMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminClarifications_KnowledgeId",
                schema: "demorecruitment",
                table: "AiAdminClarifications",
                column: "KnowledgeId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminClarifications_ReplyDraftId",
                schema: "demorecruitment",
                table: "AiAdminClarifications",
                column: "ReplyDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminClarifications_TenantId_ConversationId_CustomerMessa~",
                schema: "demorecruitment",
                table: "AiAdminClarifications",
                columns: new[] { "TenantId", "ConversationId", "CustomerMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminClarifications_TenantId_State_Deadline",
                schema: "demorecruitment",
                table: "AiAdminClarifications",
                columns: new[] { "TenantId", "State", "Deadline" });

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminMessages_ChannelId_OperationKey",
                schema: "demorecruitment",
                table: "AiAdminMessages",
                columns: new[] { "ChannelId", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminMessages_RequestId",
                schema: "demorecruitment",
                table: "AiAdminMessages",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAdminMessages_WhatsAppMessageId",
                schema: "demorecruitment",
                table: "AiAdminMessages",
                column: "WhatsAppMessageId",
                unique: true,
                filter: "\"WhatsAppMessageId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM demorecruitment.\"AiLeadAssignments\" WHERE \"LeadId\" IS NULL) THEN RAISE EXCEPTION 'Preserve and remove conversation-only assignments without a lead before rollback; no synthetic lead IDs will be created.'; END IF; END $$;");
            migrationBuilder.DropTable(
                name: "AiAdminMessages",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiAdminClarifications",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiAdminChannels",
                schema: "demorecruitment");

            migrationBuilder.AlterColumn<Guid>(
                name: "LeadId",
                schema: "demorecruitment",
                table: "AiLeadAssignments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
