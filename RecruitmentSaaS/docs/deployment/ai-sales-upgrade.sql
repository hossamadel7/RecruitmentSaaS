START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    ALTER TABLE demorecruitment."WhatsAppConversations" ADD "AssignedAiSalesAgentId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    ALTER TABLE demorecruitment."Leads" ADD "AssignedAiSalesAgentId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE TABLE demorecruitment."AiControlAudits" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ConversationId" uuid NOT NULL,
        "ActorId" uuid,
        "Event" citext NOT NULL,
        "Reason" citext NOT NULL,
        "OwnershipVersion" bigint NOT NULL,
        "CreatedAt" timestamp without time zone NOT NULL,
        CONSTRAINT "PK_AiControlAudits" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AiControlAudits_WhatsAppConversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES demorecruitment."WhatsAppConversations" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE TABLE demorecruitment."AiControlRequests" (
        "TenantId" uuid NOT NULL,
        "Key" text NOT NULL,
        "Fingerprint" text NOT NULL,
        "ResourceId" uuid NOT NULL,
        "Version" bigint NOT NULL,
        "CreatedAt" timestamp without time zone NOT NULL,
        CONSTRAINT "PK_AiControlRequests" PRIMARY KEY ("TenantId", "Key")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE TABLE demorecruitment."AiOutboundIntents" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ConversationId" uuid NOT NULL,
        "OwnershipVersion" bigint NOT NULL,
        "OperationKey" text NOT NULL,
        "State" citext NOT NULL,
        "CreatedAt" timestamp without time zone NOT NULL,
        CONSTRAINT "PK_AiOutboundIntents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AiOutboundIntents_WhatsAppConversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES demorecruitment."WhatsAppConversations" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE TABLE demorecruitment."AiSalesAgents" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "WhatsAppAccountId" uuid NOT NULL,
        "TeamManagerId" uuid,
        "DisplayName" citext NOT NULL,
        "Mode" smallint NOT NULL,
        "Paused" boolean NOT NULL,
        "MaxActiveConversations" integer NOT NULL,
        "Version" bigint NOT NULL,
        "CreatedAt" timestamp without time zone NOT NULL,
        CONSTRAINT "PK_AiSalesAgents" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AiAgent_Limit" CHECK ("MaxActiveConversations" BETWEEN 1 AND 1000),
        CONSTRAINT "CK_AiAgent_Mode" CHECK ("Mode" IN (0,1)),
        CONSTRAINT "FK_AiSalesAgents_Users_TeamManagerId" FOREIGN KEY ("TeamManagerId") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiSalesAgents_WhatsAppAccounts_WhatsAppAccountId" FOREIGN KEY ("WhatsAppAccountId") REFERENCES demorecruitment."WhatsAppAccounts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE TABLE demorecruitment."ConversationControls" (
        "ConversationId" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Controller" smallint NOT NULL,
        "OwnershipVersion" bigint NOT NULL,
        "LastActorId" uuid,
        "Reason" citext NOT NULL,
        "HandoffSummary" citext NOT NULL,
        "UpdatedAt" timestamp without time zone NOT NULL,
        CONSTRAINT "PK_ConversationControls" PRIMARY KEY ("ConversationId"),
        CONSTRAINT "FK_ConversationControls_WhatsAppConversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES demorecruitment."WhatsAppConversations" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE TABLE demorecruitment."DurableWorkItems" (
        "Id" uuid NOT NULL,
        "Kind" citext NOT NULL,
        "DeduplicationKey" text NOT NULL,
        "ConversationId" uuid,
        "Payload" text NOT NULL,
        "Attempts" integer NOT NULL,
        "AvailableAt" timestamp without time zone NOT NULL,
        "CompletedAt" timestamp without time zone,
        "ErrorCode" citext,
        "CreatedAt" timestamp without time zone NOT NULL,
        CONSTRAINT "PK_DurableWorkItems" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE TABLE demorecruitment."AiLeadAssignments" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "LeadId" uuid NOT NULL,
        "ConversationId" uuid NOT NULL,
        "AgentId" uuid NOT NULL,
        "AssignedById" uuid NOT NULL,
        "PreviousSalesId" uuid,
        "PreviousConversationSalesId" uuid,
        "Reason" citext NOT NULL,
        "StartedAt" timestamp without time zone NOT NULL,
        "EndedAt" timestamp without time zone,
        CONSTRAINT "PK_AiLeadAssignments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AiLeadAssignments_AiSalesAgents_AgentId" FOREIGN KEY ("AgentId") REFERENCES demorecruitment."AiSalesAgents" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiLeadAssignments_Leads_LeadId" FOREIGN KEY ("LeadId") REFERENCES demorecruitment."Leads" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiLeadAssignments_Users_AssignedById" FOREIGN KEY ("AssignedById") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiLeadAssignments_WhatsAppConversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES demorecruitment."WhatsAppConversations" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE INDEX "IX_WhatsAppConversations_AssignedAiSalesAgentId" ON demorecruitment."WhatsAppConversations" ("AssignedAiSalesAgentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    ALTER TABLE demorecruitment."WhatsAppConversations" ADD CONSTRAINT "CK_Conversation_SalesOwner" CHECK ("AssignedSalesAgentId" IS NULL OR "AssignedAiSalesAgentId" IS NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE INDEX "IX_Leads_AssignedAiSalesAgentId" ON demorecruitment."Leads" ("AssignedAiSalesAgentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    ALTER TABLE demorecruitment."Leads" ADD CONSTRAINT "CK_Lead_SalesOwner" CHECK ("AssignedSalesId" IS NULL OR "AssignedAiSalesAgentId" IS NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE INDEX "IX_AiControlAudits_ConversationId" ON demorecruitment."AiControlAudits" ("ConversationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE INDEX "IX_AiControlAudits_TenantId_ConversationId_CreatedAt" ON demorecruitment."AiControlAudits" ("TenantId", "ConversationId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE INDEX "IX_AiLeadAssignments_AgentId" ON demorecruitment."AiLeadAssignments" ("AgentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE INDEX "IX_AiLeadAssignments_AssignedById" ON demorecruitment."AiLeadAssignments" ("AssignedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE UNIQUE INDEX "IX_AiLeadAssignments_ConversationId" ON demorecruitment."AiLeadAssignments" ("ConversationId") WHERE "EndedAt" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE UNIQUE INDEX "IX_AiLeadAssignments_LeadId" ON demorecruitment."AiLeadAssignments" ("LeadId") WHERE "EndedAt" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE INDEX "IX_AiOutboundIntents_ConversationId" ON demorecruitment."AiOutboundIntents" ("ConversationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE UNIQUE INDEX "IX_AiOutboundIntents_TenantId_OperationKey" ON demorecruitment."AiOutboundIntents" ("TenantId", "OperationKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE INDEX "IX_AiSalesAgents_TeamManagerId" ON demorecruitment."AiSalesAgents" ("TeamManagerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE INDEX "IX_AiSalesAgents_TenantId_WhatsAppAccountId" ON demorecruitment."AiSalesAgents" ("TenantId", "WhatsAppAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE INDEX "IX_AiSalesAgents_WhatsAppAccountId" ON demorecruitment."AiSalesAgents" ("WhatsAppAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE INDEX "IX_DurableWorkItems_AvailableAt" ON demorecruitment."DurableWorkItems" ("AvailableAt") WHERE "CompletedAt" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    CREATE UNIQUE INDEX "IX_DurableWorkItems_DeduplicationKey" ON demorecruitment."DurableWorkItems" ("DeduplicationKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    ALTER TABLE demorecruitment."Leads" ADD CONSTRAINT "FK_Leads_AiSalesAgents_AssignedAiSalesAgentId" FOREIGN KEY ("AssignedAiSalesAgentId") REFERENCES demorecruitment."AiSalesAgents" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    ALTER TABLE demorecruitment."WhatsAppConversations" ADD CONSTRAINT "FK_WhatsAppConversations_AiSalesAgents_AssignedAiSalesAgentId" FOREIGN KEY ("AssignedAiSalesAgentId") REFERENCES demorecruitment."AiSalesAgents" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012124131_AiSalesFoundation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261012124131_AiSalesFoundation', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012132047_AiSalesDraftEngine') THEN
    CREATE TABLE demorecruitment."AiCustomerMemories" (
        "ConversationId" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ReviewedSummary" citext NOT NULL,
        "ReviewedById" uuid NOT NULL,
        "Version" bigint NOT NULL,
        "UpdatedAt" timestamp without time zone NOT NULL,
        CONSTRAINT "PK_AiCustomerMemories" PRIMARY KEY ("ConversationId"),
        CONSTRAINT "FK_AiCustomerMemories_Users_ReviewedById" FOREIGN KEY ("ReviewedById") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiCustomerMemories_WhatsAppConversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES demorecruitment."WhatsAppConversations" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012132047_AiSalesDraftEngine') THEN
    CREATE TABLE demorecruitment."AiSalesDrafts" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ConversationId" uuid NOT NULL,
        "AgentId" uuid NOT NULL,
        "RequestedById" uuid NOT NULL,
        "RequestKey" text NOT NULL,
        "OwnershipVersion" bigint NOT NULL,
        "MemoryVersion" bigint NOT NULL,
        "ContextFingerprint" text NOT NULL,
        "KnowledgeSnapshotJson" text NOT NULL,
        "SourceIdsJson" text NOT NULL,
        "Status" smallint NOT NULL,
        "Reply" citext NOT NULL,
        "Intent" citext NOT NULL,
        "ReviewNote" citext NOT NULL,
        "NeedsHuman" boolean NOT NULL,
        "Model" citext NOT NULL,
        "PromptVersion" citext NOT NULL,
        "InputTokens" integer,
        "OutputTokens" integer,
        "ErrorCode" citext,
        "CreatedAt" timestamp without time zone NOT NULL,
        "CompletedAt" timestamp without time zone,
        "ReviewedById" uuid,
        "ReviewedAt" timestamp without time zone,
        CONSTRAINT "PK_AiSalesDrafts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AiSalesDrafts_AiSalesAgents_AgentId" FOREIGN KEY ("AgentId") REFERENCES demorecruitment."AiSalesAgents" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiSalesDrafts_Users_RequestedById" FOREIGN KEY ("RequestedById") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiSalesDrafts_WhatsAppConversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES demorecruitment."WhatsAppConversations" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012132047_AiSalesDraftEngine') THEN
    CREATE TABLE demorecruitment."AiSalesKnowledge" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "WhatsAppAccountId" uuid NOT NULL,
        "Kind" smallint NOT NULL,
        "Status" smallint NOT NULL,
        "Title" citext NOT NULL,
        "Content" citext NOT NULL,
        "SourceReference" citext NOT NULL,
        "ValidFrom" timestamp without time zone NOT NULL,
        "ValidUntil" timestamp without time zone NOT NULL,
        "CreatedById" uuid NOT NULL,
        "ApprovedById" uuid,
        "ApprovedAt" timestamp without time zone,
        "Version" bigint NOT NULL,
        "CreatedAt" timestamp without time zone NOT NULL,
        CONSTRAINT "PK_AiSalesKnowledge" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AiKnowledge_Approval" CHECK ("Status" <> 1 OR ("ApprovedById" IS NOT NULL AND "ApprovedAt" IS NOT NULL)),
        CONSTRAINT "CK_AiKnowledge_Validity" CHECK ("ValidUntil" > "ValidFrom"),
        CONSTRAINT "FK_AiSalesKnowledge_Users_ApprovedById" FOREIGN KEY ("ApprovedById") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiSalesKnowledge_Users_CreatedById" FOREIGN KEY ("CreatedById") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiSalesKnowledge_WhatsAppAccounts_WhatsAppAccountId" FOREIGN KEY ("WhatsAppAccountId") REFERENCES demorecruitment."WhatsAppAccounts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012132047_AiSalesDraftEngine') THEN
    CREATE INDEX "IX_AiCustomerMemories_ReviewedById" ON demorecruitment."AiCustomerMemories" ("ReviewedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012132047_AiSalesDraftEngine') THEN
    CREATE INDEX "IX_AiSalesDrafts_AgentId" ON demorecruitment."AiSalesDrafts" ("AgentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012132047_AiSalesDraftEngine') THEN
    CREATE INDEX "IX_AiSalesDrafts_ConversationId_CreatedAt" ON demorecruitment."AiSalesDrafts" ("ConversationId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012132047_AiSalesDraftEngine') THEN
    CREATE INDEX "IX_AiSalesDrafts_RequestedById" ON demorecruitment."AiSalesDrafts" ("RequestedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012132047_AiSalesDraftEngine') THEN
    CREATE INDEX "IX_AiSalesDrafts_TenantId_CreatedAt" ON demorecruitment."AiSalesDrafts" ("TenantId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012132047_AiSalesDraftEngine') THEN
    CREATE UNIQUE INDEX "IX_AiSalesDrafts_TenantId_RequestKey" ON demorecruitment."AiSalesDrafts" ("TenantId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012132047_AiSalesDraftEngine') THEN
    CREATE INDEX "IX_AiSalesKnowledge_ApprovedById" ON demorecruitment."AiSalesKnowledge" ("ApprovedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012132047_AiSalesDraftEngine') THEN
    CREATE INDEX "IX_AiSalesKnowledge_CreatedById" ON demorecruitment."AiSalesKnowledge" ("CreatedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012132047_AiSalesDraftEngine') THEN
    CREATE INDEX "IX_AiSalesKnowledge_TenantId_WhatsAppAccountId_Status_ValidUnt~" ON demorecruitment."AiSalesKnowledge" ("TenantId", "WhatsAppAccountId", "Status", "ValidUntil");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012132047_AiSalesDraftEngine') THEN
    CREATE INDEX "IX_AiSalesKnowledge_WhatsAppAccountId" ON demorecruitment."AiSalesKnowledge" ("WhatsAppAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012132047_AiSalesDraftEngine') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261012132047_AiSalesDraftEngine', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012150418_AiSalesVoice') THEN
    CREATE TABLE demorecruitment."AiVoiceTurns" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ConversationId" uuid NOT NULL,
        "AgentId" uuid NOT NULL,
        "OwnershipVersion" bigint NOT NULL,
        "Status" smallint NOT NULL,
        "Version" bigint NOT NULL,
        "CreatedAt" timestamp without time zone NOT NULL,
        "LastMessageAt" timestamp without time zone NOT NULL,
        "AvailableAt" timestamp without time zone NOT NULL,
        "StartedAt" timestamp without time zone,
        "CompletedAt" timestamp without time zone,
        "Transcript" citext NOT NULL,
        "ReviewedText" citext NOT NULL,
        "NeedsClarification" boolean NOT NULL,
        "LowestTokenProbability" double precision,
        "ErrorCode" citext,
        "RequestedById" uuid,
        "ReviewedById" uuid,
        "ReviewedAt" timestamp without time zone,
        CONSTRAINT "PK_AiVoiceTurns" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AiVoiceTurns_AiSalesAgents_AgentId" FOREIGN KEY ("AgentId") REFERENCES demorecruitment."AiSalesAgents" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiVoiceTurns_Users_RequestedById" FOREIGN KEY ("RequestedById") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiVoiceTurns_Users_ReviewedById" FOREIGN KEY ("ReviewedById") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiVoiceTurns_WhatsAppConversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES demorecruitment."WhatsAppConversations" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012150418_AiSalesVoice') THEN
    CREATE TABLE demorecruitment."AiVoiceParts" (
        "MessageId" uuid NOT NULL,
        "TurnId" uuid NOT NULL,
        "MediaId" citext NOT NULL,
        "MediaSha256" text NOT NULL,
        "ContentType" citext NOT NULL,
        "ByteCount" integer,
        "Status" smallint NOT NULL,
        "Transcript" citext NOT NULL,
        "LowestTokenProbability" double precision,
        "Model" citext NOT NULL,
        "ReportedSeconds" double precision,
        "InputTokens" integer,
        "OutputTokens" integer,
        "LastAttemptAt" timestamp without time zone,
        "AttemptsOnLastDay" integer NOT NULL,
        "ErrorCode" citext,
        CONSTRAINT "PK_AiVoiceParts" PRIMARY KEY ("MessageId"),
        CONSTRAINT "FK_AiVoiceParts_AiVoiceTurns_TurnId" FOREIGN KEY ("TurnId") REFERENCES demorecruitment."AiVoiceTurns" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiVoiceParts_WhatsAppMessages_MessageId" FOREIGN KEY ("MessageId") REFERENCES demorecruitment."WhatsAppMessages" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012150418_AiSalesVoice') THEN
    CREATE INDEX "IX_AiVoiceParts_LastAttemptAt" ON demorecruitment."AiVoiceParts" ("LastAttemptAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012150418_AiSalesVoice') THEN
    CREATE INDEX "IX_AiVoiceParts_TurnId" ON demorecruitment."AiVoiceParts" ("TurnId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012150418_AiSalesVoice') THEN
    CREATE INDEX "IX_AiVoiceTurns_AgentId" ON demorecruitment."AiVoiceTurns" ("AgentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012150418_AiSalesVoice') THEN
    CREATE INDEX "IX_AiVoiceTurns_ConversationId_CreatedAt" ON demorecruitment."AiVoiceTurns" ("ConversationId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012150418_AiSalesVoice') THEN
    CREATE INDEX "IX_AiVoiceTurns_RequestedById" ON demorecruitment."AiVoiceTurns" ("RequestedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012150418_AiSalesVoice') THEN
    CREATE INDEX "IX_AiVoiceTurns_ReviewedById" ON demorecruitment."AiVoiceTurns" ("ReviewedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012150418_AiSalesVoice') THEN
    CREATE INDEX "IX_AiVoiceTurns_TenantId_Status_AvailableAt" ON demorecruitment."AiVoiceTurns" ("TenantId", "Status", "AvailableAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012150418_AiSalesVoice') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261012150418_AiSalesVoice', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE TABLE demorecruitment."AiOfficePolicies" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "WhatsAppAccountId" uuid NOT NULL,
        "BranchId" uuid NOT NULL,
        "OpenDaysMask" integer NOT NULL,
        "OpensAtMinute" integer NOT NULL,
        "ClosesAtMinute" integer NOT NULL,
        "ValidUntil" timestamp without time zone NOT NULL,
        "Enabled" boolean NOT NULL,
        "Version" bigint NOT NULL,
        "SourceReference" citext NOT NULL,
        "ApprovedById" uuid NOT NULL,
        "ApprovedAt" timestamp without time zone NOT NULL,
        CONSTRAINT "PK_AiOfficePolicies" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AiOfficePolicies_Branches_BranchId" FOREIGN KEY ("BranchId") REFERENCES demorecruitment."Branches" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficePolicies_Users_ApprovedById" FOREIGN KEY ("ApprovedById") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficePolicies_WhatsAppAccounts_WhatsAppAccountId" FOREIGN KEY ("WhatsAppAccountId") REFERENCES demorecruitment."WhatsAppAccounts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE TABLE demorecruitment."AiOfficeBookings" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ConversationId" uuid NOT NULL,
        "LeadId" uuid NOT NULL,
        "AgentId" uuid NOT NULL,
        "TeamManagerId" uuid,
        "BranchId" uuid NOT NULL,
        "PolicyId" uuid NOT NULL,
        "PolicyVersion" bigint NOT NULL,
        "Status" smallint NOT NULL,
        "Version" bigint NOT NULL,
        "ArrivalLocal" timestamp without time zone NOT NULL,
        "Purpose" citext NOT NULL,
        "ConfirmationMessageId" uuid NOT NULL,
        "ConfirmationVoiceTurnId" uuid,
        "ConfirmationFingerprint" text NOT NULL,
        "ReminderId" uuid,
        "VerifiedVisitId" uuid,
        "ReviewedById" uuid NOT NULL,
        "CreatedAt" timestamp without time zone NOT NULL,
        "UpdatedAt" timestamp without time zone NOT NULL,
        CONSTRAINT "PK_AiOfficeBookings" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AiOfficeBookings_AiOfficePolicies_PolicyId" FOREIGN KEY ("PolicyId") REFERENCES demorecruitment."AiOfficePolicies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficeBookings_AiSalesAgents_AgentId" FOREIGN KEY ("AgentId") REFERENCES demorecruitment."AiSalesAgents" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficeBookings_AiVoiceTurns_ConfirmationVoiceTurnId" FOREIGN KEY ("ConfirmationVoiceTurnId") REFERENCES demorecruitment."AiVoiceTurns" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficeBookings_Branches_BranchId" FOREIGN KEY ("BranchId") REFERENCES demorecruitment."Branches" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficeBookings_FollowUpReminders_ReminderId" FOREIGN KEY ("ReminderId") REFERENCES demorecruitment."FollowUpReminders" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficeBookings_LeadVisits_VerifiedVisitId" FOREIGN KEY ("VerifiedVisitId") REFERENCES demorecruitment."LeadVisits" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficeBookings_Leads_LeadId" FOREIGN KEY ("LeadId") REFERENCES demorecruitment."Leads" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficeBookings_Users_ReviewedById" FOREIGN KEY ("ReviewedById") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficeBookings_Users_TeamManagerId" FOREIGN KEY ("TeamManagerId") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficeBookings_WhatsAppConversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES demorecruitment."WhatsAppConversations" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficeBookings_WhatsAppMessages_ConfirmationMessageId" FOREIGN KEY ("ConfirmationMessageId") REFERENCES demorecruitment."WhatsAppMessages" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE TABLE demorecruitment."AiOfficeBookingEvents" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "BookingId" uuid NOT NULL,
        "Version" bigint NOT NULL,
        "Action" citext NOT NULL,
        "ActorId" uuid NOT NULL,
        "At" timestamp without time zone NOT NULL,
        "ArrivalLocal" timestamp without time zone NOT NULL,
        "BranchId" uuid NOT NULL,
        "Purpose" citext NOT NULL,
        "ConfirmationMessageId" uuid,
        "ConfirmationFingerprint" text NOT NULL,
        "Reason" citext NOT NULL,
        "FunnelHistoryId" uuid,
        CONSTRAINT "PK_AiOfficeBookingEvents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AiOfficeBookingEvents_AiOfficeBookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES demorecruitment."AiOfficeBookings" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficeBookingEvents_Branches_BranchId" FOREIGN KEY ("BranchId") REFERENCES demorecruitment."Branches" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficeBookingEvents_LeadFunnelHistory_FunnelHistoryId" FOREIGN KEY ("FunnelHistoryId") REFERENCES demorecruitment."LeadFunnelHistory" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficeBookingEvents_Users_ActorId" FOREIGN KEY ("ActorId") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiOfficeBookingEvents_WhatsAppMessages_ConfirmationMessageId" FOREIGN KEY ("ConfirmationMessageId") REFERENCES demorecruitment."WhatsAppMessages" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookingEvents_ActorId" ON demorecruitment."AiOfficeBookingEvents" ("ActorId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE UNIQUE INDEX "IX_AiOfficeBookingEvents_BookingId_Version" ON demorecruitment."AiOfficeBookingEvents" ("BookingId", "Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookingEvents_BranchId" ON demorecruitment."AiOfficeBookingEvents" ("BranchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookingEvents_ConfirmationMessageId" ON demorecruitment."AiOfficeBookingEvents" ("ConfirmationMessageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookingEvents_FunnelHistoryId" ON demorecruitment."AiOfficeBookingEvents" ("FunnelHistoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookings_AgentId" ON demorecruitment."AiOfficeBookings" ("AgentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookings_BranchId" ON demorecruitment."AiOfficeBookings" ("BranchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookings_ConfirmationMessageId" ON demorecruitment."AiOfficeBookings" ("ConfirmationMessageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookings_ConfirmationVoiceTurnId" ON demorecruitment."AiOfficeBookings" ("ConfirmationVoiceTurnId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookings_ConversationId" ON demorecruitment."AiOfficeBookings" ("ConversationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE UNIQUE INDEX "IX_AiOfficeBookings_LeadId" ON demorecruitment."AiOfficeBookings" ("LeadId") WHERE "Status" = 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookings_PolicyId" ON demorecruitment."AiOfficeBookings" ("PolicyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookings_ReminderId" ON demorecruitment."AiOfficeBookings" ("ReminderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookings_ReviewedById" ON demorecruitment."AiOfficeBookings" ("ReviewedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookings_TeamManagerId" ON demorecruitment."AiOfficeBookings" ("TeamManagerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookings_TenantId_CreatedAt" ON demorecruitment."AiOfficeBookings" ("TenantId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficeBookings_VerifiedVisitId" ON demorecruitment."AiOfficeBookings" ("VerifiedVisitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficePolicies_ApprovedById" ON demorecruitment."AiOfficePolicies" ("ApprovedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficePolicies_BranchId" ON demorecruitment."AiOfficePolicies" ("BranchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE UNIQUE INDEX "IX_AiOfficePolicies_TenantId_WhatsAppAccountId_BranchId" ON demorecruitment."AiOfficePolicies" ("TenantId", "WhatsAppAccountId", "BranchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    CREATE INDEX "IX_AiOfficePolicies_WhatsAppAccountId" ON demorecruitment."AiOfficePolicies" ("WhatsAppAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012152606_AiOfficeBookings') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261012152606_AiOfficeBookings', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    ALTER TABLE demorecruitment."WhatsAppMessages" ADD "SenderAiSalesAgentId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    ALTER TABLE demorecruitment."AiOutboundIntents" ADD "AttemptedAt" timestamp without time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    ALTER TABLE demorecruitment."AiOutboundIntents" ADD "CompletedAt" timestamp without time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    ALTER TABLE demorecruitment."AiOutboundIntents" ADD "DraftId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    ALTER TABLE demorecruitment."AiOutboundIntents" ADD "ErrorCode" citext;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    ALTER TABLE demorecruitment."AiOutboundIntents" ADD "MessageId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    ALTER TABLE demorecruitment."AiOutboundIntents" ADD "RequestedById" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    CREATE INDEX "IX_WhatsAppMessages_SenderAiSalesAgentId" ON demorecruitment."WhatsAppMessages" ("SenderAiSalesAgentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    ALTER TABLE demorecruitment."WhatsAppMessages" ADD CONSTRAINT "CK_Message_SalesAuthor" CHECK ("SenderUserId" IS NULL OR "SenderAiSalesAgentId" IS NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    CREATE UNIQUE INDEX "IX_AiOutboundIntents_DraftId" ON demorecruitment."AiOutboundIntents" ("DraftId") WHERE "DraftId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    CREATE INDEX "IX_AiOutboundIntents_MessageId" ON demorecruitment."AiOutboundIntents" ("MessageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    CREATE INDEX "IX_AiOutboundIntents_RequestedById" ON demorecruitment."AiOutboundIntents" ("RequestedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    ALTER TABLE demorecruitment."AiOutboundIntents" ADD CONSTRAINT "FK_AiOutboundIntents_AiSalesDrafts_DraftId" FOREIGN KEY ("DraftId") REFERENCES demorecruitment."AiSalesDrafts" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    ALTER TABLE demorecruitment."AiOutboundIntents" ADD CONSTRAINT "FK_AiOutboundIntents_Users_RequestedById" FOREIGN KEY ("RequestedById") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    ALTER TABLE demorecruitment."AiOutboundIntents" ADD CONSTRAINT "FK_AiOutboundIntents_WhatsAppMessages_MessageId" FOREIGN KEY ("MessageId") REFERENCES demorecruitment."WhatsAppMessages" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    ALTER TABLE demorecruitment."WhatsAppMessages" ADD CONSTRAINT "FK_WhatsAppMessages_AiSalesAgents_SenderAiSalesAgentId" FOREIGN KEY ("SenderAiSalesAgentId") REFERENCES demorecruitment."AiSalesAgents" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012202732_AiReviewedDispatch') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261012202732_AiReviewedDispatch', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013003318_AiChatFactReview') THEN
    CREATE TABLE demorecruitment."AiChatFactCandidates" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "WhatsAppAccountId" uuid NOT NULL,
        "SourceMessageId" uuid,
        "SourceFingerprint" text NOT NULL,
        "Topic" citext NOT NULL,
        "SourceStatement" citext NOT NULL,
        "Speaker" citext NOT NULL,
        "State" smallint NOT NULL,
        "Version" bigint NOT NULL,
        "KnowledgeId" uuid,
        "ReviewedById" uuid,
        "CreatedAt" timestamp without time zone NOT NULL,
        "ReviewedAt" timestamp without time zone,
        CONSTRAINT "PK_AiChatFactCandidates" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_ChatFact_State" CHECK ("State" IN (0,1,2)),
        CONSTRAINT "FK_AiChatFactCandidates_AiSalesKnowledge_KnowledgeId" FOREIGN KEY ("KnowledgeId") REFERENCES demorecruitment."AiSalesKnowledge" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiChatFactCandidates_Users_ReviewedById" FOREIGN KEY ("ReviewedById") REFERENCES demorecruitment."Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiChatFactCandidates_WhatsAppAccounts_WhatsAppAccountId" FOREIGN KEY ("WhatsAppAccountId") REFERENCES demorecruitment."WhatsAppAccounts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AiChatFactCandidates_WhatsAppMessages_SourceMessageId" FOREIGN KEY ("SourceMessageId") REFERENCES demorecruitment."WhatsAppMessages" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013003318_AiChatFactReview') THEN
    CREATE INDEX "IX_AiChatFactCandidates_KnowledgeId" ON demorecruitment."AiChatFactCandidates" ("KnowledgeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013003318_AiChatFactReview') THEN
    CREATE INDEX "IX_AiChatFactCandidates_ReviewedById" ON demorecruitment."AiChatFactCandidates" ("ReviewedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013003318_AiChatFactReview') THEN
    CREATE INDEX "IX_AiChatFactCandidates_SourceMessageId" ON demorecruitment."AiChatFactCandidates" ("SourceMessageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013003318_AiChatFactReview') THEN
    CREATE UNIQUE INDEX "IX_AiChatFactCandidates_TenantId_WhatsAppAccountId_SourceMessa~" ON demorecruitment."AiChatFactCandidates" ("TenantId", "WhatsAppAccountId", "SourceMessageId", "SourceFingerprint", "Topic");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013003318_AiChatFactReview') THEN
    CREATE INDEX "IX_AiChatFactCandidates_TenantId_WhatsAppAccountId_State_Creat~" ON demorecruitment."AiChatFactCandidates" ("TenantId", "WhatsAppAccountId", "State", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013003318_AiChatFactReview') THEN
    CREATE INDEX "IX_AiChatFactCandidates_WhatsAppAccountId" ON demorecruitment."AiChatFactCandidates" ("WhatsAppAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013003318_AiChatFactReview') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261013003318_AiChatFactReview', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013114423_AiStructuredCustomerMemory') THEN
    ALTER TABLE demorecruitment."AiCustomerMemories" ADD "ReviewedDetailsJson" jsonb NOT NULL DEFAULT '{}';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013114423_AiStructuredCustomerMemory') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261013114423_AiStructuredCustomerMemory', '8.0.11');
    END IF;
END $EF$;
COMMIT;

