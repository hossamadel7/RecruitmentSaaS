-- Behaviour parity scenario (SQL Server side). Everything runs inside a transaction that is rolled back.
SET NOCOUNT ON;
BEGIN TRAN;
DECLARE @lead uniqueidentifier = 'F4D4870D-125A-41BF-870D-E41141B89086', @lead2 uniqueidentifier = 'F3D019AC-406E-4C7A-ACB1-32141EC54CEB',
        @pkg uniqueidentifier = '00000000-0000-0000-0000-000000000040', @admin uniqueidentifier = '00000000-0000-0000-0000-000000000030',
        @sales6 uniqueidentifier = '977D1803-80B1-4711-B09D-18205B5048E2', @sales3 uniqueidentifier = '80AD25C4-5BF2-4A71-92D9-01036D0A18D2';
DECLARE @out TABLE (n int IDENTITY, line nvarchar(max));
DECLARE @branch uniqueidentifier = (SELECT BranchId FROM demorecruitment.Leads WHERE Id=@lead);

UPDATE demorecruitment.Leads SET AssignedOfficeSalesId=@sales6 WHERE Id=@lead;
DECLARE @cand uniqueidentifier;
EXEC demorecruitment.sp_ConvertLeadToCandidate @lead, @pkg, @admin, @cand OUTPUT;
DECLARE @candText nvarchar(36) = CAST(@cand AS nvarchar(36));

DECLARE @i int = 0, @ok bit, @msg nvarchar(500), @stage nvarchar(200), @ov bit;
WHILE @i < 9
BEGIN
    SET @ov = CASE WHEN @i < 2 THEN 0 ELSE 1 END;
    SELECT @ok = NULL, @msg = NULL, @stage = NULL;   -- fresh output parameters per call, as the app does
    EXEC demorecruitment.sp_MoveToNextStage @cand, @admin, N'note', @ov, NULL, @ok OUTPUT, @msg OUTPUT, @stage OUTPUT;
    INSERT @out(line) VALUES (N'move|' + CAST(@ov AS nvarchar) + N'|' + CAST(@ok AS nvarchar) + N'|' + ISNULL(REPLACE(@msg, @candText, N'CAND'), N'<null>') + N'|' + ISNULL(@stage, N'<null>'));
    SET @i += 1;
END

INSERT @out(line) SELECT N'cand|' + ps.StageName + N'|' + CAST(c.Status AS nvarchar) + N'|' + CAST(c.TotalPaidEGP AS nvarchar) + N'|' + CAST(c.IsProfileComplete AS nvarchar) + N'|' + CAST(c.AssignedSalesId AS nvarchar(36))
    FROM demorecruitment.Candidates c JOIN demorecruitment.PackageStages ps ON ps.Id=c.CurrentPackageStageId WHERE c.Id=@cand;
INSERT @out(line) SELECT N'hist|' + ISNULL(CAST(FromStage AS nvarchar),N'<null>') + N'>' + CAST(ToStage AS nvarchar) + N'|' + CAST(IsOverride AS nvarchar) + N'|' + ISNULL(Notes,N'<null>')
    FROM demorecruitment.CandidateStageHistory WHERE CandidateId=@cand ORDER BY ToStage;
INSERT @out(line) SELECT N'act|' + CAST(ActivityType AS nvarchar) + N'|' + Description + N'|' + ISNULL(Details,N'<null>')
    FROM demorecruitment.CandidateActivities WHERE CandidateId=@cand ORDER BY Details;  -- same-second rows have no defined order in either system
INSERT @out(line) SELECT N'sac|' + CAST(CompletionType AS nvarchar) + N'|' + ISNULL(Notes,N'<null>') FROM demorecruitment.StageActionCompletions WHERE CandidateId=@cand ORDER BY CompletedAt;
INSERT @out(line) SELECT N'comm|' + CAST(AmountEGP AS nvarchar) + N'|' + CAST(DealsThisMonth AS nvarchar) + N'|' + CAST(Status AS nvarchar) + N'|' + CONVERT(nvarchar(10), CommissionMonth, 120) + N'|' + CAST(SalesUserId AS nvarchar(36))
    FROM demorecruitment.Commissions WHERE CandidateId=@cand;
INSERT @out(line) SELECT N'lead|' + CAST(Status AS nvarchar) + N'|' + CAST(IsConverted AS nvarchar) + N'|' + CASE WHEN ConvertedCandidateId=@cand THEN N'linked' ELSE N'NOT LINKED' END FROM demorecruitment.Leads WHERE Id=@lead;
INSERT @out(line) SELECT N'funnel|' + ISNULL(CAST(FromStatus AS nvarchar),N'<null>') + N'>' + CAST(ToStatus AS nvarchar) + N'|' + REPLACE(Note, @candText, N'CAND') FROM demorecruitment.LeadFunnelHistory WHERE LeadId=@lead ORDER BY CreatedAt;

-- triggers
INSERT demorecruitment.LeadCallLog (LeadId, CalledById, Channel, Outcome, Note) VALUES (@lead2, @admin, 2, 5, N'ملاحظة'), (@lead2, @admin, 1, 1, N'');
INSERT demorecruitment.LeadVisits (LeadId, BranchId, ReceptionUserId, AssignedSalesUserId, MeetingOutcome) VALUES (@lead2, @branch, @admin, @sales3, 3);
INSERT @out(line) SELECT N'trg|' + CAST(ActivityType AS nvarchar) + N'|' + Description + N'|' + ISNULL(Details,N'<null>') + N'|' + EntityType
    FROM demorecruitment.LeadActivities WHERE LeadId=@lead2 ORDER BY ActivityType, Description;

-- upsert
DECLARE @lid uniqueidentifier, @dup bit;
DECLARE @phone nvarchar(30) = (SELECT Phone FROM demorecruitment.Leads WHERE Id='A338DB26-CCB7-4CA4-9A95-4A69F3E39D50');
EXEC demorecruitment.sp_UpsertLead @branch, @admin, N'Robert Again', @phone, 2, NULL, NULL, N'again', @LeadId=@lid OUTPUT, @WasDuplicate=@dup OUTPUT;
INSERT @out(line) SELECT N'upsert-dup|' + CAST(@dup AS nvarchar) + N'|' + CASE WHEN @lid='A338DB26-CCB7-4CA4-9A95-4A69F3E39D50' THEN N'same' ELSE N'other' END + N'|' + FullName + N'|' + CASE WHEN Notes LIKE N'%[[]Re-entry 20__-__-__ __:__:__]: again' THEN N'notes-ok' ELSE N'notes:' + ISNULL(Notes,N'<null>') END FROM demorecruitment.Leads WHERE Id=@lid;
EXEC demorecruitment.sp_UpsertLead @branch, @admin, N'New Person', N'0109999TEST', 6, NULL, NULL, NULL, NULL, NULL, N'Referrer', @LeadId=@lid OUTPUT, @WasDuplicate=@dup OUTPUT;
INSERT @out(line) SELECT N'upsert-new|' + CAST(@dup AS nvarchar) + N'|' + CASE WHEN LeadCode = N'LD-' + RIGHT(N'00000' + CAST(LeadSequence AS nvarchar(10)), 5) AND LeadSequence > 5 THEN N'code-ok' ELSE N'code-BAD:' + LeadCode END + N'|' + CAST(Status AS nvarchar) FROM demorecruitment.Leads WHERE Id=@lid;
INSERT @out(line) SELECT N'upsert-funnel|' + Note FROM demorecruitment.LeadFunnelHistory WHERE LeadId=@lid;
-- case-insensitive match, as the SQL Server collation does
EXEC demorecruitment.sp_UpsertLead @branch, @admin, NULL, N'0109999test', 1, @LeadId=@lid OUTPUT, @WasDuplicate=@dup OUTPUT;
INSERT @out(line) VALUES (N'upsert-ci|' + CAST(@dup AS nvarchar));

-- reminders
INSERT demorecruitment.FollowUpReminders (LeadId, AssignedToId, CreatedById, ReminderDate, Status) VALUES (@lead2, @sales3, @admin, DATEADD(day,-1,CAST(SYSUTCDATETIME() AS date)), 1);
EXEC demorecruitment.sp_ProcessDueReminders;
INSERT @out(line) SELECT N'notif|' + CAST(Type AS nvarchar) + N'|' + Title + N'|' + Body FROM demorecruitment.Notifications WHERE UserId=@sales3 AND EntityId=@lead2;
INSERT @out(line) SELECT N'reminder|' + CAST(Status AS nvarchar) FROM demorecruitment.FollowUpReminders WHERE LeadId=@lead2;

-- walk-in
DECLARE @v uniqueidentifier, @wl uniqueidentifier, @wc uniqueidentifier, @we bit;
EXEC demorecruitment.sp_WalkInCheckin N'0111222333', N'Walk In', 1, NULL, NULL, @branch, @admin, @sales3, @pkg, N'visit', @v OUTPUT, @wl OUTPUT, @wc OUTPUT, @we OUTPUT;
INSERT @out(line) SELECT N'walkin|' + CAST(@we AS nvarchar) + N'|' + CAST(l.Status AS nvarchar) + N'|' + CAST(l.IsConverted AS nvarchar) + N'|' + CAST(c.IsProfileComplete AS nvarchar) + N'|' + CASE WHEN v.ConvertedCandidateId=@wc THEN N'visit-linked' ELSE N'visit-NOT-linked' END
    FROM demorecruitment.Leads l JOIN demorecruitment.Candidates c ON c.Id=@wc JOIN demorecruitment.LeadVisits v ON v.Id=@v WHERE l.Id=@wl;
INSERT @out(line) SELECT N'walkin-act|' + CAST(ActivityType AS nvarchar) + N'|' + Description + N'|' + REPLACE(REPLACE(ISNULL(Details,N''), CAST(@wc AS nvarchar(36)), N'CAND'), CAST(@v AS nvarchar(36)), N'VISIT')
    FROM demorecruitment.LeadActivities WHERE LeadId=@wl ORDER BY ActivityType;
INSERT @out(line) SELECT N'walkin-funnel|' + ISNULL(CAST(FromStatus AS nvarchar),N'<null>') + N'>' + CAST(ToStatus AS nvarchar) + N'|' + REPLACE(Note, CAST(@wc AS nvarchar(36)), N'CAND') FROM demorecruitment.LeadFunnelHistory WHERE LeadId=@wl ORDER BY ToStatus;

-- views
INSERT @out(line) SELECT N'vw_funnel|' + CAST(LeadSource AS nvarchar) + N'|' + CAST(Status AS nvarchar) + N'|' + CAST(IsConverted AS nvarchar) + N'|' + CAST(LeadCount AS nvarchar) FROM demorecruitment.vw_LeadFunnelSummary ORDER BY LeadSource, Status, IsConverted;

SELECT line FROM @out ORDER BY n;
-- Expected-error cases last: with XACT_ABORT ON inside the procedures, a caught error dooms this test transaction.
BEGIN TRY
    EXEC demorecruitment.sp_ConvertLeadToCandidate @lead, @pkg, @admin, @cand OUTPUT;
END TRY BEGIN CATCH SELECT N'convert-again-err|' + ERROR_MESSAGE(); END CATCH
BEGIN TRY
    EXEC demorecruitment.sp_WalkInCheckin N'0111222333', N'Walk In', 6, NULL, NULL, @branch, @admin, @sales3, @pkg, NULL, @v OUTPUT, @wl OUTPUT, @wc OUTPUT, @we OUTPUT;
END TRY BEGIN CATCH SELECT N'walkin-err|' + ERROR_MESSAGE(); END CATCH
ROLLBACK;
