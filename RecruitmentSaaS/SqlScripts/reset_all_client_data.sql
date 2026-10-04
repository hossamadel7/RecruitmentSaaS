/* ============================================================================
   FULL RESET — delete ALL client data and start from scratch.

   DELETES
     - Leads and everything about them (activities, calls, visits, funnel history,
       follow-up reminders)
     - Candidates and everything about them (payments, refunds, commissions,
       documents, contracts, visas, stage history/approvals/actions, passports logs)
     - Notifications
     - WhatsApp chats: conversations, messages, notes, follow-ups, handoffs, contacts
     - Staff salary history (SalaryPayments)
     - Audit log
     - Lead codes restart at LD-00001

   KEEPS
     Users, branches, packages & stages, stage types, companies & jobs, campaigns,
     commission tiers & settings, Google Sheets setup, WhatsApp numbers & Meta token,
     registration-form settings and team forms.

   NOT TOUCHED
     - Uploaded files on disk (documents/contracts/visas) — only database rows go.
     - Google Sheets keep their "last imported row", so old sheet rows are NOT
       imported again.

   HOW TO RUN
   1. TAKE A FULL DATABASE BACKUP FIRST.
   2. Run as-is: @Commit = 0 shows the counts and anything suspicious, then rolls
      everything back — nothing is deleted.
   3. If it looks right, set @Commit = 1 and run again. This is PERMANENT.
   ============================================================================ */

SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @Commit bit = 0;   -- 0 = preview only, 1 = delete for real

/* ── 1. What will be deleted ─────────────────────────────────────────────── */
SELECT 'Leads' AS [Table], COUNT(*) AS [Rows] FROM [demorecruitment].[Leads]
UNION ALL SELECT 'LeadActivities',              COUNT(*) FROM [demorecruitment].[LeadActivities]
UNION ALL SELECT 'LeadCallLog',                 COUNT(*) FROM [demorecruitment].[LeadCallLog]
UNION ALL SELECT 'LeadVisits',                  COUNT(*) FROM [demorecruitment].[LeadVisits]
UNION ALL SELECT 'LeadFunnelHistory',           COUNT(*) FROM [demorecruitment].[LeadFunnelHistory]
UNION ALL SELECT 'FollowUpReminders',           COUNT(*) FROM [demorecruitment].[FollowUpReminders]
UNION ALL SELECT 'Candidates',                  COUNT(*) FROM [demorecruitment].[Candidates]
UNION ALL SELECT 'CandidateActivities',         COUNT(*) FROM [demorecruitment].[CandidateActivities]
UNION ALL SELECT 'CandidateStageHistory',       COUNT(*) FROM [demorecruitment].[CandidateStageHistory]
UNION ALL SELECT 'StageActionCompletions',      COUNT(*) FROM [demorecruitment].[StageActionCompletions]
UNION ALL SELECT 'StageApprovalRequests',       COUNT(*) FROM [demorecruitment].[StageApprovalRequests]
UNION ALL SELECT 'Payments',                    COUNT(*) FROM [demorecruitment].[Payments]
UNION ALL SELECT 'Refunds',                     COUNT(*) FROM [demorecruitment].[Refunds]
UNION ALL SELECT 'Commissions',                 COUNT(*) FROM [demorecruitment].[Commissions]
UNION ALL SELECT 'Documents',                   COUNT(*) FROM [demorecruitment].[Documents]
UNION ALL SELECT 'ContractUploads',             COUNT(*) FROM [demorecruitment].[ContractUploads]
UNION ALL SELECT 'VisaUploads',                 COUNT(*) FROM [demorecruitment].[VisaUploads]
UNION ALL SELECT 'PassportDownloadLogs',        COUNT(*) FROM [demorecruitment].[PassportDownloadLogs]
UNION ALL SELECT 'PassportDownloadedCandidates',COUNT(*) FROM [demorecruitment].[PassportDownloadedCandidates]
UNION ALL SELECT 'Notifications',               COUNT(*) FROM [demorecruitment].[Notifications]
UNION ALL SELECT 'WhatsAppConversations',       COUNT(*) FROM [demorecruitment].[WhatsAppConversations]
UNION ALL SELECT 'WhatsAppMessages',            COUNT(*) FROM [demorecruitment].[WhatsAppMessages]
UNION ALL SELECT 'WhatsAppContacts',            COUNT(*) FROM [demorecruitment].[WhatsAppContacts]
UNION ALL SELECT 'WhatsAppHandoffs',            COUNT(*) FROM [demorecruitment].[WhatsAppHandoffs]
UNION ALL SELECT 'ConversationNotes',           COUNT(*) FROM [demorecruitment].[ConversationNotes]
UNION ALL SELECT 'ConversationFollowUps',       COUNT(*) FROM [demorecruitment].[ConversationFollowUps]
UNION ALL SELECT 'SalaryPayments',              COUNT(*) FROM [demorecruitment].[SalaryPayments]
UNION ALL SELECT 'AuditLogs',                   COUNT(*) FROM [demorecruitment].[AuditLogs];

/* ── 2. Database code that writes commissions (should be empty) ──────────────
   The app now creates commissions ONLY when a package is fully paid. If any
   procedure/trigger below inserts into Commissions, tell your developer.      */
SELECT OBJECT_SCHEMA_NAME(m.object_id) AS [Schema], OBJECT_NAME(m.object_id) AS [Object], o.type_desc
FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id
WHERE m.definition LIKE '%Commissions%';

BEGIN TRANSACTION;

    /* WhatsApp chats */
    UPDATE [demorecruitment].[WhatsAppMessages] SET ReplyToMessageId = NULL WHERE ReplyToMessageId IS NOT NULL;
    DELETE FROM [demorecruitment].[ConversationNotes];
    DELETE FROM [demorecruitment].[ConversationFollowUps];
    DELETE FROM [demorecruitment].[WhatsAppHandoffs];
    DELETE FROM [demorecruitment].[WhatsAppMessages];
    DELETE FROM [demorecruitment].[WhatsAppConversations];
    DELETE FROM [demorecruitment].[WhatsAppContacts];

    /* Reminders <-> activities <-> notifications */
    UPDATE [demorecruitment].[LeadActivities] SET ReminderId = NULL WHERE ReminderId IS NOT NULL;
    DELETE FROM [demorecruitment].[FollowUpReminders];
    DELETE FROM [demorecruitment].[Notifications];

    /* Candidates' children */
    DELETE FROM [demorecruitment].[PassportDownloadedCandidates];
    DELETE FROM [demorecruitment].[PassportDownloadLogs];
    DELETE FROM [demorecruitment].[Refunds];
    DELETE FROM [demorecruitment].[Commissions];
    DELETE FROM [demorecruitment].[Payments];
    DELETE FROM [demorecruitment].[Documents];
    DELETE FROM [demorecruitment].[CandidateActivities];
    DELETE FROM [demorecruitment].[CandidateStageHistory];
    DELETE FROM [demorecruitment].[StageActionCompletions];
    DELETE FROM [demorecruitment].[StageApprovalRequests];
    DELETE FROM [demorecruitment].[ContractUploads];
    DELETE FROM [demorecruitment].[VisaUploads];

    /* Leads' children (call logs / visits first: their triggers write activities) */
    DELETE FROM [demorecruitment].[LeadCallLog];
    DELETE FROM [demorecruitment].[LeadVisits];
    DELETE FROM [demorecruitment].[LeadFunnelHistory];
    DELETE FROM [demorecruitment].[LeadActivities];

    /* Leads and candidates reference each other */
    UPDATE [demorecruitment].[Leads] SET ConvertedCandidateId = NULL, DuplicateOfLeadId = NULL
    WHERE ConvertedCandidateId IS NOT NULL OR DuplicateOfLeadId IS NOT NULL;
    DELETE FROM [demorecruitment].[Leads];
    DELETE FROM [demorecruitment].[Candidates];

    /* Staff salary history + audit log */
    DELETE FROM [demorecruitment].[SalaryPayments];
    DELETE FROM [demorecruitment].[AuditLogs];

IF @Commit = 1
BEGIN
    COMMIT TRANSACTION;

    /* Restart lead codes at LD-00001 */
    IF OBJECTPROPERTY(OBJECT_ID('demorecruitment.Leads'), 'TableHasIdentity') = 1
        DBCC CHECKIDENT ('demorecruitment.Leads', RESEED, 0);

    PRINT 'DONE — all client data was deleted. Lead codes restart at LD-00001.';
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT 'PREVIEW ONLY — nothing was deleted. Set @Commit = 1 to delete for real.';
END;
