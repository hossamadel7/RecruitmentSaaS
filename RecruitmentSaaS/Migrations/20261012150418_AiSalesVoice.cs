using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AiSalesVoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiVoiceTurns",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnershipVersion = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Transcript = table.Column<string>(type: "citext", maxLength: 4000, nullable: false),
                    ReviewedText = table.Column<string>(type: "citext", maxLength: 4000, nullable: false),
                    NeedsClarification = table.Column<bool>(type: "boolean", nullable: false),
                    LowestTokenProbability = table.Column<double>(type: "double precision", nullable: true),
                    ErrorCode = table.Column<string>(type: "citext", maxLength: 100, nullable: true),
                    RequestedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiVoiceTurns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiVoiceTurns_AiSalesAgents_AgentId",
                        column: x => x.AgentId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiSalesAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiVoiceTurns_Users_RequestedById",
                        column: x => x.RequestedById,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiVoiceTurns_Users_ReviewedById",
                        column: x => x.ReviewedById,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiVoiceTurns_WhatsAppConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiVoiceParts",
                schema: "demorecruitment",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaId = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    MediaSha256 = table.Column<string>(type: "text", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    ByteCount = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    Transcript = table.Column<string>(type: "citext", maxLength: 2000, nullable: false),
                    LowestTokenProbability = table.Column<double>(type: "double precision", nullable: true),
                    Model = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    ReportedSeconds = table.Column<double>(type: "double precision", nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AttemptsOnLastDay = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "citext", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiVoiceParts", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_AiVoiceParts_AiVoiceTurns_TurnId",
                        column: x => x.TurnId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiVoiceTurns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiVoiceParts_WhatsAppMessages_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiVoiceParts_LastAttemptAt",
                schema: "demorecruitment",
                table: "AiVoiceParts",
                column: "LastAttemptAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiVoiceParts_TurnId",
                schema: "demorecruitment",
                table: "AiVoiceParts",
                column: "TurnId");

            migrationBuilder.CreateIndex(
                name: "IX_AiVoiceTurns_AgentId",
                schema: "demorecruitment",
                table: "AiVoiceTurns",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AiVoiceTurns_ConversationId_CreatedAt",
                schema: "demorecruitment",
                table: "AiVoiceTurns",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiVoiceTurns_RequestedById",
                schema: "demorecruitment",
                table: "AiVoiceTurns",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_AiVoiceTurns_ReviewedById",
                schema: "demorecruitment",
                table: "AiVoiceTurns",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_AiVoiceTurns_TenantId_Status_AvailableAt",
                schema: "demorecruitment",
                table: "AiVoiceTurns",
                columns: new[] { "TenantId", "Status", "AvailableAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiVoiceParts",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiVoiceTurns",
                schema: "demorecruitment");
        }
    }
}
