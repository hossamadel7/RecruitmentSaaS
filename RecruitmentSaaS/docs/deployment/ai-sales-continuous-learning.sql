START TRANSACTION;

CREATE TABLE demorecruitment."AiConversationAnalyses" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "WhatsAppAccountId" uuid NOT NULL,
    "ConversationId" uuid NOT NULL,
    "Fingerprint" citext NOT NULL,
    "State" smallint NOT NULL,
    "Attempts" integer NOT NULL,
    "AvailableAt" timestamp without time zone NOT NULL,
    "CreatedAt" timestamp without time zone NOT NULL,
    "CompletedAt" timestamp without time zone,
    "ErrorCode" citext NOT NULL,
    "InferredStage" citext NOT NULL,
    "Language" citext NOT NULL,
    "VerifiedOutcome" citext NOT NULL,
    "OutcomeEvidence" citext NOT NULL,
    "Cohort" citext NOT NULL,
    "SalespersonId" uuid,
    "IsEvaluationHoldout" boolean NOT NULL,
    "InsightsJson" jsonb NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_AiConversationAnalyses" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AiConversationAnalyses_Users_SalespersonId" FOREIGN KEY ("SalespersonId") REFERENCES demorecruitment."Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_AiConversationAnalyses_WhatsAppAccounts_WhatsAppAccountId" FOREIGN KEY ("WhatsAppAccountId") REFERENCES demorecruitment."WhatsAppAccounts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AiConversationAnalyses_WhatsAppConversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES demorecruitment."WhatsAppConversations" ("Id") ON DELETE CASCADE
);

CREATE TABLE demorecruitment."AiLearningAccounts" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "WhatsAppAccountId" uuid NOT NULL,
    "Paused" boolean NOT NULL,
    "IncludeOpenHistory" boolean NOT NULL,
    "Cursor" uuid NOT NULL,
    "ExcludedConversationIds" uuid[] NOT NULL,
    "LastScanAt" timestamp without time zone,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_AiLearningAccounts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AiLearningAccounts_WhatsAppAccounts_WhatsAppAccountId" FOREIGN KEY ("WhatsAppAccountId") REFERENCES demorecruitment."WhatsAppAccounts" ("Id") ON DELETE CASCADE
);

CREATE TABLE demorecruitment."AiLearningUsage" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Kind" citext NOT NULL,
    "Model" citext NOT NULL,
    "Status" citext NOT NULL,
    "InputTokens" integer NOT NULL,
    "OutputTokens" integer NOT NULL,
    "CreatedAt" timestamp without time zone NOT NULL,
    CONSTRAINT "PK_AiLearningUsage" PRIMARY KEY ("Id")
);

CREATE TABLE demorecruitment."AiSalesFeedback" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "DraftId" uuid NOT NULL,
    "ActorId" uuid NOT NULL,
    "Kind" smallint NOT NULL,
    "Note" citext NOT NULL,
    "EditedReply" citext NOT NULL,
    "CreatedAt" timestamp without time zone NOT NULL,
    CONSTRAINT "PK_AiSalesFeedback" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AiSalesFeedback_AiSalesDrafts_DraftId" FOREIGN KEY ("DraftId") REFERENCES demorecruitment."AiSalesDrafts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AiSalesFeedback_Users_ActorId" FOREIGN KEY ("ActorId") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE demorecruitment."AiEvaluationCases" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "WhatsAppAccountId" uuid NOT NULL,
    "AnalysisId" uuid,
    "Dataset" citext NOT NULL,
    "Scenario" citext NOT NULL,
    "Expected" citext NOT NULL,
    "ContextJson" jsonb NOT NULL,
    "PrivacyApproved" boolean NOT NULL,
    "CreatedAt" timestamp without time zone NOT NULL,
    CONSTRAINT "PK_AiEvaluationCases" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AiEvaluationCases_AiConversationAnalyses_AnalysisId" FOREIGN KEY ("AnalysisId") REFERENCES demorecruitment."AiConversationAnalyses" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AiEvaluationCases_WhatsAppAccounts_WhatsAppAccountId" FOREIGN KEY ("WhatsAppAccountId") REFERENCES demorecruitment."WhatsAppAccounts" ("Id") ON DELETE CASCADE
);

CREATE TABLE demorecruitment."AiKnowledgeIndexes" (
    "KnowledgeId" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "WhatsAppAccountId" uuid NOT NULL,
    "AnalysisId" uuid,
    "Category" smallint NOT NULL,
    "Confidence" double precision NOT NULL,
    "ContentHash" citext NOT NULL,
    "SourceFingerprint" citext NOT NULL,
    "KnowledgeVersion" bigint NOT NULL,
    "EmbeddingModel" citext NOT NULL,
    "Embedding" real[] NOT NULL,
    "UpdatedAt" timestamp without time zone NOT NULL,
    CONSTRAINT "PK_AiKnowledgeIndexes" PRIMARY KEY ("KnowledgeId"),
    CONSTRAINT "FK_AiKnowledgeIndexes_AiConversationAnalyses_AnalysisId" FOREIGN KEY ("AnalysisId") REFERENCES demorecruitment."AiConversationAnalyses" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_AiKnowledgeIndexes_AiSalesKnowledge_KnowledgeId" FOREIGN KEY ("KnowledgeId") REFERENCES demorecruitment."AiSalesKnowledge" ("Id") ON DELETE CASCADE
);

CREATE TABLE demorecruitment."AiEvaluationResults" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "CaseId" uuid NOT NULL,
    "RunId" uuid NOT NULL,
    "ActorId" uuid NOT NULL,
    "Configuration" citext NOT NULL,
    "Baseline" citext NOT NULL,
    "Reply" citext NOT NULL,
    "ReviewJson" jsonb NOT NULL,
    "Passed" boolean,
    "CreatedAt" timestamp without time zone NOT NULL,
    CONSTRAINT "PK_AiEvaluationResults" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AiEvaluationResults_AiEvaluationCases_CaseId" FOREIGN KEY ("CaseId") REFERENCES demorecruitment."AiEvaluationCases" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AiEvaluationResults_Users_ActorId" FOREIGN KEY ("ActorId") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_AiConversationAnalyses_ConversationId" ON demorecruitment."AiConversationAnalyses" ("ConversationId");

CREATE INDEX "IX_AiConversationAnalyses_SalespersonId" ON demorecruitment."AiConversationAnalyses" ("SalespersonId");

CREATE INDEX "IX_AiConversationAnalyses_TenantId_State_AvailableAt" ON demorecruitment."AiConversationAnalyses" ("TenantId", "State", "AvailableAt");

CREATE UNIQUE INDEX "IX_AiConversationAnalyses_TenantId_WhatsAppAccountId_Conversat~" ON demorecruitment."AiConversationAnalyses" ("TenantId", "WhatsAppAccountId", "ConversationId", "Fingerprint");

CREATE INDEX "IX_AiConversationAnalyses_WhatsAppAccountId" ON demorecruitment."AiConversationAnalyses" ("WhatsAppAccountId");

CREATE INDEX "IX_AiEvaluationCases_AnalysisId" ON demorecruitment."AiEvaluationCases" ("AnalysisId");

CREATE INDEX "IX_AiEvaluationCases_TenantId_WhatsAppAccountId_Dataset" ON demorecruitment."AiEvaluationCases" ("TenantId", "WhatsAppAccountId", "Dataset");

CREATE INDEX "IX_AiEvaluationCases_WhatsAppAccountId" ON demorecruitment."AiEvaluationCases" ("WhatsAppAccountId");

CREATE INDEX "IX_AiEvaluationResults_ActorId" ON demorecruitment."AiEvaluationResults" ("ActorId");

CREATE INDEX "IX_AiEvaluationResults_CaseId" ON demorecruitment."AiEvaluationResults" ("CaseId");

CREATE INDEX "IX_AiEvaluationResults_TenantId_RunId" ON demorecruitment."AiEvaluationResults" ("TenantId", "RunId");

CREATE INDEX "IX_AiKnowledgeIndexes_AnalysisId" ON demorecruitment."AiKnowledgeIndexes" ("AnalysisId");

CREATE INDEX "IX_AiKnowledgeIndexes_TenantId_WhatsAppAccountId_ContentHash" ON demorecruitment."AiKnowledgeIndexes" ("TenantId", "WhatsAppAccountId", "ContentHash");

CREATE UNIQUE INDEX "IX_AiLearningAccounts_TenantId_WhatsAppAccountId" ON demorecruitment."AiLearningAccounts" ("TenantId", "WhatsAppAccountId");

CREATE INDEX "IX_AiLearningAccounts_WhatsAppAccountId" ON demorecruitment."AiLearningAccounts" ("WhatsAppAccountId");

CREATE INDEX "IX_AiLearningUsage_TenantId_CreatedAt" ON demorecruitment."AiLearningUsage" ("TenantId", "CreatedAt");

CREATE INDEX "IX_AiSalesFeedback_ActorId" ON demorecruitment."AiSalesFeedback" ("ActorId");

CREATE INDEX "IX_AiSalesFeedback_DraftId" ON demorecruitment."AiSalesFeedback" ("DraftId");

CREATE INDEX "IX_AiSalesFeedback_TenantId_DraftId_CreatedAt" ON demorecruitment."AiSalesFeedback" ("TenantId", "DraftId", "CreatedAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261104133753_AiSalesContinuousLearning', '8.0.11');

COMMIT;

