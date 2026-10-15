using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AiChatFactReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiChatFactCandidates",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceFingerprint = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                    Topic = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    SourceStatement = table.Column<string>(type: "citext", maxLength: 4000, nullable: false),
                    Speaker = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    State = table.Column<byte>(type: "smallint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    KnowledgeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiChatFactCandidates", x => x.Id);
                    table.CheckConstraint("CK_ChatFact_State", "\"State\" IN (0,1,2)");
                    table.ForeignKey(
                        name: "FK_AiChatFactCandidates_AiSalesKnowledge_KnowledgeId",
                        column: x => x.KnowledgeId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiSalesKnowledge",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiChatFactCandidates_Users_ReviewedById",
                        column: x => x.ReviewedById,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiChatFactCandidates_WhatsAppAccounts_WhatsAppAccountId",
                        column: x => x.WhatsAppAccountId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiChatFactCandidates_WhatsAppMessages_SourceMessageId",
                        column: x => x.SourceMessageId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiChatFactCandidates_KnowledgeId",
                schema: "demorecruitment",
                table: "AiChatFactCandidates",
                column: "KnowledgeId");

            migrationBuilder.CreateIndex(
                name: "IX_AiChatFactCandidates_ReviewedById",
                schema: "demorecruitment",
                table: "AiChatFactCandidates",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_AiChatFactCandidates_SourceMessageId",
                schema: "demorecruitment",
                table: "AiChatFactCandidates",
                column: "SourceMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_AiChatFactCandidates_TenantId_WhatsAppAccountId_SourceMessa~",
                schema: "demorecruitment",
                table: "AiChatFactCandidates",
                columns: new[] { "TenantId", "WhatsAppAccountId", "SourceMessageId", "SourceFingerprint", "Topic" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiChatFactCandidates_TenantId_WhatsAppAccountId_State_Creat~",
                schema: "demorecruitment",
                table: "AiChatFactCandidates",
                columns: new[] { "TenantId", "WhatsAppAccountId", "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiChatFactCandidates_WhatsAppAccountId",
                schema: "demorecruitment",
                table: "AiChatFactCandidates",
                column: "WhatsAppAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiChatFactCandidates",
                schema: "demorecruitment");
        }
    }
}
