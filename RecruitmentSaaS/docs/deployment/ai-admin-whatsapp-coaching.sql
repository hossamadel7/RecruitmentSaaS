START TRANSACTION;

ALTER TABLE demorecruitment."AiLeadAssignments" ALTER COLUMN "LeadId" DROP NOT NULL;

CREATE TABLE demorecruitment."AiAdminChannels" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "WhatsAppAccountId" uuid NOT NULL,
    "AdminUserId" uuid NOT NULL,
    "Phone" citext NOT NULL,
    "VerificationHash" citext NOT NULL,
    "VerificationExpiresAt" timestamp without time zone NOT NULL,
    "VerificationAttempts" integer NOT NULL,
    "VerifiedAt" timestamp without time zone,
    "Paused" boolean NOT NULL,
    "LastInboundAt" timestamp without time zone,
    "LastNotificationAt" timestamp without time zone,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_AiAdminChannels" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AiAdminChannels_Users_AdminUserId" FOREIGN KEY ("AdminUserId") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AiAdminChannels_WhatsAppAccounts_WhatsAppAccountId" FOREIGN KEY ("WhatsAppAccountId") REFERENCES demorecruitment."WhatsAppAccounts" ("Id") ON DELETE RESTRICT
);

CREATE TABLE demorecruitment."AiAdminClarifications" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "ChannelId" uuid NOT NULL,
    "AgentId" uuid NOT NULL,
    "ConversationId" uuid NOT NULL,
    "CustomerMessageId" uuid NOT NULL,
    "OwnershipVersion" bigint NOT NULL,
    "Code" citext NOT NULL,
    "Question" citext NOT NULL,
    "Answer" citext NOT NULL,
    "AnswerScope" smallint NOT NULL,
    "State" smallint NOT NULL,
    "AnswerMessageId" uuid,
    "ValidatedById" uuid,
    "KnowledgeId" uuid,
    "ReplyDraftId" uuid,
    "CreatedAt" timestamp without time zone NOT NULL,
    "Deadline" timestamp without time zone NOT NULL,
    "AnswerValidUntil" timestamp without time zone NOT NULL,
    "Version" bigint NOT NULL,
    "ErrorCode" citext,
    CONSTRAINT "PK_AiAdminClarifications" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AiAdminClarifications_AiAdminChannels_ChannelId" FOREIGN KEY ("ChannelId") REFERENCES demorecruitment."AiAdminChannels" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AiAdminClarifications_AiSalesAgents_AgentId" FOREIGN KEY ("AgentId") REFERENCES demorecruitment."AiSalesAgents" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AiAdminClarifications_AiSalesDrafts_ReplyDraftId" FOREIGN KEY ("ReplyDraftId") REFERENCES demorecruitment."AiSalesDrafts" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_AiAdminClarifications_AiSalesKnowledge_KnowledgeId" FOREIGN KEY ("KnowledgeId") REFERENCES demorecruitment."AiSalesKnowledge" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_AiAdminClarifications_WhatsAppConversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES demorecruitment."WhatsAppConversations" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AiAdminClarifications_WhatsAppMessages_CustomerMessageId" FOREIGN KEY ("CustomerMessageId") REFERENCES demorecruitment."WhatsAppMessages" ("Id") ON DELETE RESTRICT
);

CREATE TABLE demorecruitment."AiAdminMessages" (
    "Id" uuid NOT NULL,
    "ChannelId" uuid NOT NULL,
    "RequestId" uuid,
    "WhatsAppMessageId" citext,
    "OperationKey" citext NOT NULL,
    "Incoming" boolean NOT NULL,
    "IsTemplate" boolean NOT NULL,
    "Text" citext NOT NULL,
    "State" citext NOT NULL,
    "ErrorCode" citext,
    "CreatedAt" timestamp without time zone NOT NULL,
    "DeliveredAt" timestamp without time zone,
    CONSTRAINT "PK_AiAdminMessages" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AiAdminMessages_AiAdminChannels_ChannelId" FOREIGN KEY ("ChannelId") REFERENCES demorecruitment."AiAdminChannels" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AiAdminMessages_AiAdminClarifications_RequestId" FOREIGN KEY ("RequestId") REFERENCES demorecruitment."AiAdminClarifications" ("Id") ON DELETE SET NULL
);

CREATE INDEX "IX_AiAdminChannels_AdminUserId" ON demorecruitment."AiAdminChannels" ("AdminUserId");

CREATE UNIQUE INDEX "IX_AiAdminChannels_TenantId_WhatsAppAccountId" ON demorecruitment."AiAdminChannels" ("TenantId", "WhatsAppAccountId");

CREATE UNIQUE INDEX "IX_AiAdminChannels_WhatsAppAccountId_Phone" ON demorecruitment."AiAdminChannels" ("WhatsAppAccountId", "Phone");

CREATE INDEX "IX_AiAdminClarifications_AgentId" ON demorecruitment."AiAdminClarifications" ("AgentId");

CREATE INDEX "IX_AiAdminClarifications_ChannelId" ON demorecruitment."AiAdminClarifications" ("ChannelId");

CREATE UNIQUE INDEX "IX_AiAdminClarifications_Code" ON demorecruitment."AiAdminClarifications" ("Code");

CREATE INDEX "IX_AiAdminClarifications_ConversationId" ON demorecruitment."AiAdminClarifications" ("ConversationId");

CREATE INDEX "IX_AiAdminClarifications_CustomerMessageId" ON demorecruitment."AiAdminClarifications" ("CustomerMessageId");

CREATE INDEX "IX_AiAdminClarifications_KnowledgeId" ON demorecruitment."AiAdminClarifications" ("KnowledgeId");

CREATE INDEX "IX_AiAdminClarifications_ReplyDraftId" ON demorecruitment."AiAdminClarifications" ("ReplyDraftId");

CREATE UNIQUE INDEX "IX_AiAdminClarifications_TenantId_ConversationId_CustomerMessa~" ON demorecruitment."AiAdminClarifications" ("TenantId", "ConversationId", "CustomerMessageId");

CREATE INDEX "IX_AiAdminClarifications_TenantId_State_Deadline" ON demorecruitment."AiAdminClarifications" ("TenantId", "State", "Deadline");

CREATE UNIQUE INDEX "IX_AiAdminMessages_ChannelId_OperationKey" ON demorecruitment."AiAdminMessages" ("ChannelId", "OperationKey");

CREATE INDEX "IX_AiAdminMessages_RequestId" ON demorecruitment."AiAdminMessages" ("RequestId");

CREATE UNIQUE INDEX "IX_AiAdminMessages_WhatsAppMessageId" ON demorecruitment."AiAdminMessages" ("WhatsAppMessageId") WHERE "WhatsAppMessageId" IS NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261104145700_AiAdminWhatsAppCoaching', '8.0.11');

COMMIT;

