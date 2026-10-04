/* ============================================================================
   Remove the "Ahmed Hossam" WhatsApp number (+20 10 14423015) and ALL of its
   chats from the CRM: conversations, messages, notes, follow-ups, handoffs.

   - Leads, users and WhatsApp contacts are NOT deleted (contacts are shared
     across numbers).
   - This only removes the data from the CRM. The chats stay on the phone's
     WhatsApp Business app, and Meta is not changed.

   HOW TO RUN
   1. Run as-is first: @Commit = 0 only PREVIEWS the counts and rolls back.
   2. If the counts look right, set @Commit = 1 and run again.
   ⚠ With @Commit = 1 this is permanent. Take a database backup first.
   ============================================================================ */

SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @Commit bit = 0;                               -- 0 = preview, 1 = delete for real
DECLARE @PhoneNumberId nvarchar(100) = N'1131445996727000';

DECLARE @AccountId uniqueidentifier =
    (SELECT Id FROM [demorecruitment].[WhatsAppAccounts] WHERE PhoneNumberId = @PhoneNumberId);

IF @AccountId IS NULL
BEGIN
    PRINT 'No WhatsApp account with PhoneNumberId ' + @PhoneNumberId + ' — nothing to do.';
    RETURN;
END;

SELECT Name, DisplayPhoneNumber, PhoneNumberId, WabaId
FROM [demorecruitment].[WhatsAppAccounts] WHERE Id = @AccountId;

DECLARE @Conv TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT INTO @Conv SELECT Id FROM [demorecruitment].[WhatsAppConversations] WHERE WhatsAppAccountId = @AccountId;

DECLARE @Msg TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT INTO @Msg SELECT Id FROM [demorecruitment].[WhatsAppMessages]
WHERE WhatsAppAccountId = @AccountId OR ConversationId IN (SELECT Id FROM @Conv);

SELECT
    (SELECT COUNT(*) FROM @Conv)                                                         AS Conversations,
    (SELECT COUNT(*) FROM @Msg)                                                          AS Messages,
    (SELECT COUNT(*) FROM [demorecruitment].[ConversationNotes]     WHERE ConversationId IN (SELECT Id FROM @Conv)) AS Notes,
    (SELECT COUNT(*) FROM [demorecruitment].[ConversationFollowUps] WHERE ConversationId IN (SELECT Id FROM @Conv)) AS FollowUps,
    (SELECT COUNT(*) FROM [demorecruitment].[WhatsAppHandoffs]      WHERE WhatsAppAccountId = @AccountId)          AS Handoffs;

BEGIN TRANSACTION;

    -- Detach anything outside this number that points at its rows
    UPDATE [demorecruitment].[WhatsAppMessages] SET ReplyToMessageId = NULL
    WHERE ReplyToMessageId IN (SELECT Id FROM @Msg);

    UPDATE [demorecruitment].[WhatsAppHandoffs] SET ConversationId = NULL
    WHERE ConversationId IN (SELECT Id FROM @Conv) AND WhatsAppAccountId <> @AccountId;

    -- Children first, then the chats, then the number
    DELETE FROM [demorecruitment].[ConversationNotes]     WHERE ConversationId IN (SELECT Id FROM @Conv);
    DELETE FROM [demorecruitment].[ConversationFollowUps] WHERE ConversationId IN (SELECT Id FROM @Conv);
    DELETE FROM [demorecruitment].[WhatsAppHandoffs]      WHERE WhatsAppAccountId = @AccountId;
    DELETE FROM [demorecruitment].[WhatsAppMessages]      WHERE Id IN (SELECT Id FROM @Msg);
    DELETE FROM [demorecruitment].[WhatsAppConversations] WHERE Id IN (SELECT Id FROM @Conv);
    DELETE FROM [demorecruitment].[WhatsAppAccounts]      WHERE Id = @AccountId;

IF @Commit = 1
BEGIN
    COMMIT TRANSACTION;
    PRINT 'DONE — the number and all its chats were deleted.';
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT 'PREVIEW ONLY — nothing was deleted. Set @Commit = 1 to delete for real.';
END;
