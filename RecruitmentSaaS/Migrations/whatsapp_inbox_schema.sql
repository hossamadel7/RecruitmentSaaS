IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE TABLE [demorecruitment].[WhatsAppAccounts] (
        [Id] uniqueidentifier NOT NULL DEFAULT ((newsequentialid())),
        [Name] nvarchar(200) NOT NULL,
        [DisplayPhoneNumber] nvarchar(30) NOT NULL,
        [PhoneNumberId] nvarchar(50) NOT NULL,
        [WabaId] nvarchar(50) NOT NULL,
        [AssignedSalesAgentId] uniqueidentifier NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAt] datetime2(0) NOT NULL DEFAULT ((sysutcdatetime())),
        [UpdatedAt] datetime2(0) NULL,
        CONSTRAINT [PK_demorecruitment_WA_Acc] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_Acc_Agent] FOREIGN KEY ([AssignedSalesAgentId]) REFERENCES [demorecruitment].[Users] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE TABLE [demorecruitment].[WhatsAppContacts] (
        [Id] uniqueidentifier NOT NULL DEFAULT ((newsequentialid())),
        [Name] nvarchar(200) NULL,
        [WhatsAppPhoneNumber] nvarchar(30) NOT NULL,
        [WhatsAppUserId] nvarchar(50) NULL,
        [CreatedAt] datetime2(0) NOT NULL DEFAULT ((sysutcdatetime())),
        [UpdatedAt] datetime2(0) NULL,
        [LastSeenAt] datetime2(0) NULL,
        CONSTRAINT [PK_demorecruitment_WA_Cnt] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE TABLE [demorecruitment].[WhatsAppConversations] (
        [Id] uniqueidentifier NOT NULL DEFAULT ((newsequentialid())),
        [ContactId] uniqueidentifier NOT NULL,
        [WhatsAppAccountId] uniqueidentifier NOT NULL,
        [AssignedSalesAgentId] uniqueidentifier NULL,
        [LeadId] uniqueidentifier NULL,
        [Status] tinyint NOT NULL DEFAULT CAST(1 AS tinyint),
        [LeadStage] tinyint NOT NULL DEFAULT CAST(1 AS tinyint),
        [LostReason] nvarchar(200) NULL,
        [UnreadCount] int NOT NULL DEFAULT 0,
        [OpenedAt] datetime2(0) NOT NULL DEFAULT ((sysutcdatetime())),
        [LastMessageAt] datetime2(0) NULL,
        [ClosedAt] datetime2(0) NULL,
        [CreatedAt] datetime2(0) NOT NULL DEFAULT ((sysutcdatetime())),
        [UpdatedAt] datetime2(0) NULL,
        CONSTRAINT [PK_demorecruitment_WA_Cnv] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_Cnv_Acc] FOREIGN KEY ([WhatsAppAccountId]) REFERENCES [demorecruitment].[WhatsAppAccounts] ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_Cnv_Agent] FOREIGN KEY ([AssignedSalesAgentId]) REFERENCES [demorecruitment].[Users] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_demorecruitment_WA_Cnv_Cnt] FOREIGN KEY ([ContactId]) REFERENCES [demorecruitment].[WhatsAppContacts] ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_Cnv_Lead] FOREIGN KEY ([LeadId]) REFERENCES [demorecruitment].[Leads] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE TABLE [demorecruitment].[WhatsAppMessages] (
        [Id] uniqueidentifier NOT NULL DEFAULT ((newsequentialid())),
        [ConversationId] uniqueidentifier NOT NULL,
        [WhatsAppAccountId] uniqueidentifier NOT NULL,
        [WhatsAppMessageId] nvarchar(100) NULL,
        [Direction] tinyint NOT NULL,
        [MessageType] tinyint NOT NULL DEFAULT CAST(1 AS tinyint),
        [TextBody] nvarchar(4000) NULL,
        [MediaId] nvarchar(200) NULL,
        [MediaUrl] nvarchar(1000) NULL,
        [SenderUserId] uniqueidentifier NULL,
        [ReplyToMessageId] uniqueidentifier NULL,
        [Status] tinyint NOT NULL,
        [ErrorCode] nvarchar(50) NULL,
        [ErrorMessage] nvarchar(500) NULL,
        [WhatsAppTimestamp] datetime2(0) NOT NULL,
        [CreatedAt] datetime2(0) NOT NULL DEFAULT ((sysutcdatetime())),
        CONSTRAINT [PK_demorecruitment_WA_Msg] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_Msg_Acc] FOREIGN KEY ([WhatsAppAccountId]) REFERENCES [demorecruitment].[WhatsAppAccounts] ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_Msg_Cnv] FOREIGN KEY ([ConversationId]) REFERENCES [demorecruitment].[WhatsAppConversations] ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_Msg_Reply] FOREIGN KEY ([ReplyToMessageId]) REFERENCES [demorecruitment].[WhatsAppMessages] ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_Msg_Sender] FOREIGN KEY ([SenderUserId]) REFERENCES [demorecruitment].[Users] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE TABLE [demorecruitment].[WhatsAppHandoffs] (
        [Id] uniqueidentifier NOT NULL DEFAULT ((newsequentialid())),
        [LeadId] uniqueidentifier NOT NULL,
        [WhatsAppAccountId] uniqueidentifier NOT NULL,
        [AssignedSalesAgentId] uniqueidentifier NOT NULL,
        [ReferenceCode] nvarchar(20) NOT NULL,
        [GeneratedWhatsAppUrl] nvarchar(500) NOT NULL,
        [Status] tinyint NOT NULL DEFAULT CAST(1 AS tinyint),
        [ConversationId] uniqueidentifier NULL,
        [CreatedAt] datetime2(0) NOT NULL DEFAULT ((sysutcdatetime())),
        [SentAt] datetime2(0) NULL,
        [ConnectedAt] datetime2(0) NULL,
        [ExpiredAt] datetime2(0) NULL,
        CONSTRAINT [PK_demorecruitment_WA_Hnd] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_Hnd_Acc] FOREIGN KEY ([WhatsAppAccountId]) REFERENCES [demorecruitment].[WhatsAppAccounts] ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_Hnd_Agent] FOREIGN KEY ([AssignedSalesAgentId]) REFERENCES [demorecruitment].[Users] ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_Hnd_Cnv] FOREIGN KEY ([ConversationId]) REFERENCES [demorecruitment].[WhatsAppConversations] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_demorecruitment_WA_Hnd_Lead] FOREIGN KEY ([LeadId]) REFERENCES [demorecruitment].[Leads] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE TABLE [demorecruitment].[ConversationNotes] (
        [Id] uniqueidentifier NOT NULL DEFAULT ((newsequentialid())),
        [ConversationId] uniqueidentifier NOT NULL,
        [AuthorId] uniqueidentifier NOT NULL,
        [Body] nvarchar(1000) NOT NULL,
        [CreatedAt] datetime2(0) NOT NULL DEFAULT ((sysutcdatetime())),
        CONSTRAINT [PK_demorecruitment_WA_Note] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_Note_Author] FOREIGN KEY ([AuthorId]) REFERENCES [demorecruitment].[Users] ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_Note_Cnv] FOREIGN KEY ([ConversationId]) REFERENCES [demorecruitment].[WhatsAppConversations] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE TABLE [demorecruitment].[ConversationFollowUps] (
        [Id] uniqueidentifier NOT NULL DEFAULT ((newsequentialid())),
        [ConversationId] uniqueidentifier NOT NULL,
        [AssignedToId] uniqueidentifier NOT NULL,
        [CreatedById] uniqueidentifier NOT NULL,
        [DueAt] datetime2(0) NOT NULL,
        [Status] tinyint NOT NULL DEFAULT CAST(1 AS tinyint),
        [Notes] nvarchar(500) NULL,
        [CompletedAt] datetime2(0) NULL,
        [CompletedById] uniqueidentifier NULL,
        [CreatedAt] datetime2(0) NOT NULL DEFAULT ((sysutcdatetime())),
        CONSTRAINT [PK_demorecruitment_WA_FU] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_FU_Assignee] FOREIGN KEY ([AssignedToId]) REFERENCES [demorecruitment].[Users] ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_FU_Cnv] FOREIGN KEY ([ConversationId]) REFERENCES [demorecruitment].[WhatsAppConversations] ([Id]),
        CONSTRAINT [FK_demorecruitment_WA_FU_CompletedBy] FOREIGN KEY ([CompletedById]) REFERENCES [demorecruitment].[Users] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_demorecruitment_WA_FU_CreatedBy] FOREIGN KEY ([CreatedById]) REFERENCES [demorecruitment].[Users] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_Acc_Active] ON [demorecruitment].[WhatsAppAccounts] ([IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_WhatsAppAccounts_AssignedSalesAgentId] ON [demorecruitment].[WhatsAppAccounts] ([AssignedSalesAgentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_demorecruitment_WA_Acc_PhoneNumberId] ON [demorecruitment].[WhatsAppAccounts] ([PhoneNumberId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_demorecruitment_WA_Cnt_Phone] ON [demorecruitment].[WhatsAppContacts] ([WhatsAppPhoneNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_Cnv_Acc] ON [demorecruitment].[WhatsAppConversations] ([WhatsAppAccountId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_Cnv_Agent] ON [demorecruitment].[WhatsAppConversations] ([AssignedSalesAgentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_Cnv_LastMsg] ON [demorecruitment].[WhatsAppConversations] ([LastMessageAt] DESC);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_Cnv_Lead] ON [demorecruitment].[WhatsAppConversations] ([LeadId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_Cnv_LeadStage] ON [demorecruitment].[WhatsAppConversations] ([LeadStage]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_Cnv_Status] ON [demorecruitment].[WhatsAppConversations] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_demorecruitment_WA_Cnv_Cnt_Acc] ON [demorecruitment].[WhatsAppConversations] ([ContactId], [WhatsAppAccountId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_Msg_Acc] ON [demorecruitment].[WhatsAppMessages] ([WhatsAppAccountId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_Msg_Cnv] ON [demorecruitment].[WhatsAppMessages] ([ConversationId], [CreatedAt] DESC);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_Msg_Status] ON [demorecruitment].[WhatsAppMessages] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_WhatsAppMessages_ReplyToMessageId] ON [demorecruitment].[WhatsAppMessages] ([ReplyToMessageId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_WhatsAppMessages_SenderUserId] ON [demorecruitment].[WhatsAppMessages] ([SenderUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UQ_demorecruitment_WA_Msg_WamId] ON [demorecruitment].[WhatsAppMessages] ([WhatsAppMessageId]) WHERE ([WhatsAppMessageId] IS NOT NULL)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_Hnd_Lead] ON [demorecruitment].[WhatsAppHandoffs] ([LeadId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_Hnd_Status] ON [demorecruitment].[WhatsAppHandoffs] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_WhatsAppHandoffs_AssignedSalesAgentId] ON [demorecruitment].[WhatsAppHandoffs] ([AssignedSalesAgentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_WhatsAppHandoffs_ConversationId] ON [demorecruitment].[WhatsAppHandoffs] ([ConversationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_WhatsAppHandoffs_WhatsAppAccountId] ON [demorecruitment].[WhatsAppHandoffs] ([WhatsAppAccountId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_demorecruitment_WA_Hnd_Ref] ON [demorecruitment].[WhatsAppHandoffs] ([ReferenceCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_ConversationNotes_AuthorId] ON [demorecruitment].[ConversationNotes] ([AuthorId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_Note_Cnv] ON [demorecruitment].[ConversationNotes] ([ConversationId], [CreatedAt] DESC);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_ConversationFollowUps_CompletedById] ON [demorecruitment].[ConversationFollowUps] ([CompletedById]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_ConversationFollowUps_CreatedById] ON [demorecruitment].[ConversationFollowUps] ([CreatedById]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_FU_Assignee] ON [demorecruitment].[ConversationFollowUps] ([AssignedToId], [Status], [DueAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_FU_Cnv] ON [demorecruitment].[ConversationFollowUps] ([ConversationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    CREATE INDEX [IX_demorecruitment_WA_FU_Due] ON [demorecruitment].[ConversationFollowUps] ([DueAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923002651_InitialWhatsAppInbox'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260923002651_InitialWhatsAppInbox', N'8.0.0');
END;
GO

COMMIT;
GO

