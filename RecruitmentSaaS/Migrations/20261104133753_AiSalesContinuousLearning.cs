using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecruitmentSaaS.Migrations
{
    /// <inheritdoc />
    public partial class AiSalesContinuousLearning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiConversationAnalyses",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "citext", maxLength: 64, nullable: false),
                    State = table.Column<byte>(type: "smallint", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    InferredStage = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    Language = table.Column<string>(type: "citext", maxLength: 20, nullable: false),
                    VerifiedOutcome = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    OutcomeEvidence = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    Cohort = table.Column<string>(type: "citext", maxLength: 300, nullable: false),
                    SalespersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsEvaluationHoldout = table.Column<bool>(type: "boolean", nullable: false),
                    InsightsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiConversationAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiConversationAnalyses_Users_SalespersonId",
                        column: x => x.SalespersonId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AiConversationAnalyses_WhatsAppAccounts_WhatsAppAccountId",
                        column: x => x.WhatsAppAccountId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiConversationAnalyses_WhatsAppConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiLearningAccounts",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Paused = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeOpenHistory = table.Column<bool>(type: "boolean", nullable: false),
                    Cursor = table.Column<Guid>(type: "uuid", nullable: false),
                    ExcludedConversationIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    LastScanAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiLearningAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiLearningAccounts_WhatsAppAccounts_WhatsAppAccountId",
                        column: x => x.WhatsAppAccountId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiLearningUsage",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    Model = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiLearningUsage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiSalesFeedback",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    Note = table.Column<string>(type: "citext", maxLength: 1000, nullable: false),
                    EditedReply = table.Column<string>(type: "citext", maxLength: 3000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSalesFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiSalesFeedback_AiSalesDrafts_DraftId",
                        column: x => x.DraftId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiSalesDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiSalesFeedback_Users_ActorId",
                        column: x => x.ActorId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiEvaluationCases",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnalysisId = table.Column<Guid>(type: "uuid", nullable: true),
                    Dataset = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    Scenario = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    Expected = table.Column<string>(type: "citext", maxLength: 2000, nullable: false),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: false),
                    PrivacyApproved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiEvaluationCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiEvaluationCases_AiConversationAnalyses_AnalysisId",
                        column: x => x.AnalysisId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiConversationAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiEvaluationCases_WhatsAppAccounts_WhatsAppAccountId",
                        column: x => x.WhatsAppAccountId,
                        principalSchema: "demorecruitment",
                        principalTable: "WhatsAppAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiKnowledgeIndexes",
                schema: "demorecruitment",
                columns: table => new
                {
                    KnowledgeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnalysisId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<byte>(type: "smallint", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    ContentHash = table.Column<string>(type: "citext", maxLength: 64, nullable: false),
                    SourceFingerprint = table.Column<string>(type: "citext", maxLength: 64, nullable: false),
                    KnowledgeVersion = table.Column<long>(type: "bigint", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    Embedding = table.Column<float[]>(type: "real[]", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiKnowledgeIndexes", x => x.KnowledgeId);
                    table.ForeignKey(
                        name: "FK_AiKnowledgeIndexes_AiConversationAnalyses_AnalysisId",
                        column: x => x.AnalysisId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiConversationAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AiKnowledgeIndexes_AiSalesKnowledge_KnowledgeId",
                        column: x => x.KnowledgeId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiSalesKnowledge",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiEvaluationResults",
                schema: "demorecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Configuration = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    Baseline = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    Reply = table.Column<string>(type: "citext", maxLength: 3000, nullable: false),
                    ReviewJson = table.Column<string>(type: "jsonb", nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiEvaluationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiEvaluationResults_AiEvaluationCases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "demorecruitment",
                        principalTable: "AiEvaluationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiEvaluationResults_Users_ActorId",
                        column: x => x.ActorId,
                        principalSchema: "demorecruitment",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiConversationAnalyses_ConversationId",
                schema: "demorecruitment",
                table: "AiConversationAnalyses",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_AiConversationAnalyses_SalespersonId",
                schema: "demorecruitment",
                table: "AiConversationAnalyses",
                column: "SalespersonId");

            migrationBuilder.CreateIndex(
                name: "IX_AiConversationAnalyses_TenantId_State_AvailableAt",
                schema: "demorecruitment",
                table: "AiConversationAnalyses",
                columns: new[] { "TenantId", "State", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiConversationAnalyses_TenantId_WhatsAppAccountId_Conversat~",
                schema: "demorecruitment",
                table: "AiConversationAnalyses",
                columns: new[] { "TenantId", "WhatsAppAccountId", "ConversationId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiConversationAnalyses_WhatsAppAccountId",
                schema: "demorecruitment",
                table: "AiConversationAnalyses",
                column: "WhatsAppAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AiEvaluationCases_AnalysisId",
                schema: "demorecruitment",
                table: "AiEvaluationCases",
                column: "AnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_AiEvaluationCases_TenantId_WhatsAppAccountId_Dataset",
                schema: "demorecruitment",
                table: "AiEvaluationCases",
                columns: new[] { "TenantId", "WhatsAppAccountId", "Dataset" });

            migrationBuilder.CreateIndex(
                name: "IX_AiEvaluationCases_WhatsAppAccountId",
                schema: "demorecruitment",
                table: "AiEvaluationCases",
                column: "WhatsAppAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AiEvaluationResults_ActorId",
                schema: "demorecruitment",
                table: "AiEvaluationResults",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AiEvaluationResults_CaseId",
                schema: "demorecruitment",
                table: "AiEvaluationResults",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_AiEvaluationResults_TenantId_RunId",
                schema: "demorecruitment",
                table: "AiEvaluationResults",
                columns: new[] { "TenantId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiKnowledgeIndexes_AnalysisId",
                schema: "demorecruitment",
                table: "AiKnowledgeIndexes",
                column: "AnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_AiKnowledgeIndexes_TenantId_WhatsAppAccountId_ContentHash",
                schema: "demorecruitment",
                table: "AiKnowledgeIndexes",
                columns: new[] { "TenantId", "WhatsAppAccountId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_AiLearningAccounts_TenantId_WhatsAppAccountId",
                schema: "demorecruitment",
                table: "AiLearningAccounts",
                columns: new[] { "TenantId", "WhatsAppAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiLearningAccounts_WhatsAppAccountId",
                schema: "demorecruitment",
                table: "AiLearningAccounts",
                column: "WhatsAppAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AiLearningUsage_TenantId_CreatedAt",
                schema: "demorecruitment",
                table: "AiLearningUsage",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesFeedback_ActorId",
                schema: "demorecruitment",
                table: "AiSalesFeedback",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesFeedback_DraftId",
                schema: "demorecruitment",
                table: "AiSalesFeedback",
                column: "DraftId");

            migrationBuilder.CreateIndex(
                name: "IX_AiSalesFeedback_TenantId_DraftId_CreatedAt",
                schema: "demorecruitment",
                table: "AiSalesFeedback",
                columns: new[] { "TenantId", "DraftId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiEvaluationResults",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiKnowledgeIndexes",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiLearningAccounts",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiLearningUsage",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiSalesFeedback",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiEvaluationCases",
                schema: "demorecruitment");

            migrationBuilder.DropTable(
                name: "AiConversationAnalyses",
                schema: "demorecruitment");
        }
    }
}
