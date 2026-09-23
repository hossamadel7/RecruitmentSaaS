BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923115918_AddMetaEmbeddedSignup'
)
BEGIN
    ALTER TABLE [demorecruitment].[WhatsAppMessages] ADD [MessageSource] tinyint NOT NULL DEFAULT CAST(1 AS tinyint);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923115918_AddMetaEmbeddedSignup'
)
BEGIN
    ALTER TABLE [demorecruitment].[WhatsAppAccounts] ADD [ConnectionMode] tinyint NOT NULL DEFAULT CAST(1 AS tinyint);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923115918_AddMetaEmbeddedSignup'
)
BEGIN
    ALTER TABLE [demorecruitment].[WhatsAppAccounts] ADD [VerifiedName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923115918_AddMetaEmbeddedSignup'
)
BEGIN
    ALTER TABLE [demorecruitment].[WhatsAppAccounts] ADD [WebhookSubscriptionStatus] tinyint NOT NULL DEFAULT CAST(1 AS tinyint);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923115918_AddMetaEmbeddedSignup'
)
BEGIN
    CREATE TABLE [demorecruitment].[MetaSystemCredentials] (
        [Id] uniqueidentifier NOT NULL DEFAULT ((newsequentialid())),
        [AccessToken] nvarchar(2000) NOT NULL,
        [ObtainedAt] datetime2(0) NOT NULL,
        [CreatedAt] datetime2(0) NOT NULL DEFAULT ((sysutcdatetime())),
        [UpdatedAt] datetime2(0) NULL,
        CONSTRAINT [PK_demorecruitment_Meta_Cred] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923115918_AddMetaEmbeddedSignup'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260923115918_AddMetaEmbeddedSignup', N'8.0.0');
END;
GO

COMMIT;
GO

