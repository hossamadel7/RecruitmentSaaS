BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002114101_AddLeadAgeAndLeadFormSettings'
)
BEGIN
    ALTER TABLE [demorecruitment].[Leads] ADD [Age] tinyint NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002114101_AddLeadAgeAndLeadFormSettings'
)
BEGIN
    CREATE TABLE [demorecruitment].[LeadFormSettings] (
        [Id] uniqueidentifier NOT NULL DEFAULT ((newsequentialid())),
        [SeniorAgeThreshold] tinyint NOT NULL DEFAULT CAST(45 AS tinyint),
        [SalesWhatsAppNumber] nvarchar(30) NULL,
        [SalesWhatsAppMessage] nvarchar(1000) NULL,
        [UpdatedAt] datetime2(0) NULL,
        [UpdatedById] uniqueidentifier NULL,
        CONSTRAINT [PK_demorecruitment_LFS] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002114101_AddLeadAgeAndLeadFormSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002114101_AddLeadAgeAndLeadFormSettings', N'8.0.0');
END;
GO

COMMIT;
GO

