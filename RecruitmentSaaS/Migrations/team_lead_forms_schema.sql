BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003015932_AddTeamLeadForms'
)
BEGIN
    ALTER TABLE [demorecruitment].[Leads] ADD [TeamManagerId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003015932_AddTeamLeadForms'
)
BEGIN
    CREATE TABLE [demorecruitment].[TeamLeadForms] (
        [Id] uniqueidentifier NOT NULL DEFAULT ((newsequentialid())),
        [ManagerId] uniqueidentifier NOT NULL,
        [Slug] nvarchar(50) NOT NULL,
        [WhatsAppNumber] nvarchar(30) NULL,
        [CreatedAt] datetime2(0) NOT NULL DEFAULT ((sysutcdatetime())),
        [UpdatedAt] datetime2(0) NULL,
        CONSTRAINT [PK_demorecruitment_TLF] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_demorecruitment_TLF_Mgr] FOREIGN KEY ([ManagerId]) REFERENCES [demorecruitment].[Users] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003015932_AddTeamLeadForms'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_demorecruitment_Ld_TeamMgr] ON [demorecruitment].[Leads] ([TeamManagerId]) WHERE ([TeamManagerId] IS NOT NULL)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003015932_AddTeamLeadForms'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_demorecruitment_TLF_Mgr] ON [demorecruitment].[TeamLeadForms] ([ManagerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003015932_AddTeamLeadForms'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_demorecruitment_TLF_Slug] ON [demorecruitment].[TeamLeadForms] ([Slug]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003015932_AddTeamLeadForms'
)
BEGIN
    ALTER TABLE [demorecruitment].[Leads] ADD CONSTRAINT [FK_demorecruitment_Ld_TeamMgr] FOREIGN KEY ([TeamManagerId]) REFERENCES [demorecruitment].[Users] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003015932_AddTeamLeadForms'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261003015932_AddTeamLeadForms', N'8.0.0');
END;
GO

COMMIT;
GO

