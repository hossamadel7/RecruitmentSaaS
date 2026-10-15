using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AiSalesDraftEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiCustomerMemories",
                schema: "demorecruitment",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedSummary = table.Column<string>(type: "citext", maxLength: 2000, nullable: false),
                    ReviewedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCustomerMemories", x => x.ConversationId);
                    table.ForeignKey(
                        name: "FK_AiCustomerMemories_Users_ReviewedById",
                        column: x => x.ReviewedById,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiCustomerMemories_WhatsAppConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiSalesDrafts",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedById = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "text", maxLength: 128, nullable: false),
                    OwnershipVersion = table.Column<long>(type: "bigint", nullable: false),
                    MemoryVersion = table.Column<long>(type: "bigint", nullable: false),
                    ContextFingerprint = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                    KnowledgeSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    SourceIdsJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    Reply = table.Column<string>(type: "citext", maxLength: 3000, nullable: false),
                    Intent = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    ReviewNote = table.Column<string>(type: "citext", maxLength: 500, nullable: false),
                    NeedsHuman = table.Column<bool>(type: "boolean", nullable: false),
                    Model = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    ErrorCode = table.Column<string>(type: "citext", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReviewedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSalesDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiSalesDrafts_AiSalesAgents_AgentId",
                        column: x => x.AgentId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiSalesAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiSalesDrafts_Users_RequestedById",
                        column: x => x.RequestedById,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiSalesDrafts_WhatsAppConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiSalesKnowledge",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    Title = table.Column<string>(type: "citext", maxLength: 150, nullable: false),
                    Content = table.Column<string>(type: "citext", maxLength: 4000, nullable: false),
                    SourceReference = table.Column<string>(type: "citext", maxLength: 500, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSalesKnowledge", x => x.Id);
                    table.CheckConstraint("CK_AiKnowledge_Approval", "\"Status\" <> 1 OR (\"ApprovedById\" IS NOT NULL AND \"ApprovedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_AiKnowledge_Validity", "\"ValidUntil\" > \"ValidFrom\"");
                    table.ForeignKey(
                        name: "FK_AiSalesKnowledge_Users_ApprovedById",
                        column: x => x.ApprovedById,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiSalesKnowledge_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiSalesKnowledge_WhatsAppAccounts_WhatsAppAccountId",
                        column: x => x.WhatsAppAccountId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiCustomerMemories_ReviewedById",
                schema: "demorecruitment",
                table: "AiCustomerMemories",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesDrafts_AgentId",
                schema: "demorecruitment",
                table: "AiSalesDrafts",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesDrafts_ConversationId_CreatedAt",
                schema: "demorecruitment",
                table: "AiSalesDrafts",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesDrafts_RequestedById",
                schema: "demorecruitment",
                table: "AiSalesDrafts",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesDrafts_TenantId_CreatedAt",
                schema: "demorecruitment",
                table: "AiSalesDrafts",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesDrafts_TenantId_RequestKey",
                schema: "demorecruitment",
                table: "AiSalesDrafts",
                columns: new[] { "TenantId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesKnowledge_ApprovedById",
                schema: "demorecruitment",
                table: "AiSalesKnowledge",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesKnowledge_CreatedById",
                schema: "demorecruitment",
                table: "AiSalesKnowledge",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesKnowledge_TenantId_WhatsAppAccountId_Status_ValidUnt~",
                schema: "demorecruitment",
                table: "AiSalesKnowledge",
                columns: new[] { "TenantId", "WhatsAppAccountId", "Status", "ValidUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesKnowledge_WhatsAppAccountId",
                schema: "demorecruitment",
                table: "AiSalesKnowledge",
                column: "WhatsAppAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiCustomerMemories",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiSalesDrafts",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiSalesKnowledge",
                schema: "demorecruitment");
        }
    }
}
