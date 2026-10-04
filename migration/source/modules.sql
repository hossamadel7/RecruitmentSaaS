-- ==== _template.sp_MoveToNextStage (SQL_STORED_PROCEDURE)

-- Mirror for _template
CREATE   PROCEDURE [_template].[sp_MoveToNextStage]
    @CandidateId    UNIQUEIDENTIFIER,
    @MovedById      UNIQUEIDENTIFIER,
    @Notes          NVARCHAR(500) = NULL,
    @IsOverride     BIT           = 0,
    @OverrideReason NVARCHAR(500) = NULL,
    @Success        BIT           = 0   OUTPUT,
    @Message        NVARCHAR(500) = ''  OUTPUT,
    @NewStageName   NVARCHAR(200) = ''  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @CurrentStageId UNIQUEIDENTIFIER, @CurrentStageName NVARCHAR(200),
            @CurrentStageOrder INT, @CurrentMinPayment DECIMAL(18,2),
            @CurrentRequiresApproval BIT, @CurrentRequiredAction TINYINT,
            @PackageId UNIQUEIDENTIFIER, @TotalPaid DECIMAL(18,2);

    SELECT @CurrentStageId=c.CurrentPackageStageId, @CurrentStageName=ps.StageName,
           @CurrentStageOrder=ps.StageOrder, @CurrentMinPayment=ps.RequiredMinPaymentEGP,
           @CurrentRequiresApproval=ps.RequiresAdminApproval,
           @CurrentRequiredAction=st.RequiredAction,
           @PackageId=c.JobPackageId, @TotalPaid=c.TotalPaidEGP
    FROM _template.Candidates c
    LEFT JOIN _template.PackageStages ps ON ps.Id=c.CurrentPackageStageId
    LEFT JOIN _template.StageTypes    st ON st.Id=ps.StageTypeId
    WHERE c.Id=@CandidateId;

    DECLARE @NextStageId UNIQUEIDENTIFIER, @NextStageName NVARCHAR(200), @NextStageOrder INT;
    SELECT TOP 1 @NextStageId=Id, @NextStageName=StageName, @NextStageOrder=StageOrder
    FROM _template.PackageStages
    WHERE PackageId=@PackageId AND StageOrder>ISNULL(@CurrentStageOrder,0) AND IsActive=1
    ORDER BY StageOrder ASC;

    IF @NextStageId IS NULL BEGIN SET @Success=0; SET @Message=N'آخر مرحلة'; RETURN; END

    IF (@CurrentRequiredAction IN (1,4)) AND @IsOverride=0
    BEGIN
        DECLARE @DC INT;
        SELECT @DC=COUNT(*) FROM _template.Documents WHERE CandidateId=@CandidateId AND DocumentType=ISNULL(@CurrentRequiredAction,DocumentType);
        IF @DC=0 BEGIN SET @Success=0; SET @Message=N'DOCUMENT_REQUIRED:'+@CurrentStageName; RETURN; END
    END

    IF @CurrentMinPayment IS NOT NULL AND @TotalPaid<@CurrentMinPayment AND @IsOverride=0
    BEGIN
        SET @Success=0;
        SET @Message=N'PAYMENT_EXCEPTION_REQUIRED:'+CAST(@CurrentStageId AS NVARCHAR(50))+N':'+CAST(@NextStageId AS NVARCHAR(50))+N':'+CAST(@CurrentMinPayment AS NVARCHAR(20))+N':'+CAST(@TotalPaid AS NVARCHAR(20));
        RETURN;
    END

    IF @CurrentRequiresApproval=1 AND @IsOverride=0
    BEGIN
        DECLARE @Approved UNIQUEIDENTIFIER;
        SELECT TOP 1 @Approved=Id FROM _template.StageApprovalRequests
        WHERE CandidateId=@CandidateId AND FromStageId=@CurrentStageId AND ToStageId=@NextStageId AND Status=2;
        IF @Approved IS NULL BEGIN SET @Success=0; SET @Message=N'REQUIRES_APPROVAL:'+CAST(@CurrentStageId AS NVARCHAR(50))+N':'+CAST(@NextStageId AS NVARCHAR(50)); RETURN; END
    END

    BEGIN TRY
        UPDATE _template.Candidates SET CurrentPackageStageId=@NextStageId, UpdatedAt=SYSUTCDATETIME() WHERE Id=@CandidateId;

        INSERT INTO _template.CandidateStageHistory(CandidateId,FromStage,ToStage,FromStageId,ToStageId,ChangedById,IsOverride,OverrideReason,Notes)
        VALUES(@CandidateId,@CurrentStageOrder,@NextStageOrder,@CurrentStageId,@NextStageId,@MovedById,@IsOverride,@OverrideReason,@Notes);

        IF NOT EXISTS (SELECT 1 FROM _template.StageActionCompletions WHERE CandidateId=@CandidateId AND PackageStageId=@CurrentStageId)
            INSERT INTO _template.StageActionCompletions(Id,CandidateId,PackageStageId,CompletedAt,CompletedById,CompletionType,Notes)
            VALUES(NEWID(),@CandidateId,@CurrentStageId,GETUTCDATE(),@MovedById,5,N'تم اجتياز: '+@CurrentStageName);

        SET @Success=1; SET @Message=N'تم الانتقال إلى "'+@NextStageName+N'"'; SET @NewStageName=@NextStageName;
    END TRY
    BEGIN CATCH SET @Success=0; SET @Message=ERROR_MESSAGE(); END CATCH
END
GO
-- ==== _template.sp_ProcessDueReminders (SQL_STORED_PROCEDURE)

CREATE   PROCEDURE [_template].[sp_ProcessDueReminders]
    @Today DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Today IS NULL
        SET @Today = CAST(SYSUTCDATETIME() AS DATE);

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO [_template].Notifications (Id, UserId, Type, Title, Body, EntityId, IsRead, CreatedAt)
        SELECT NEWID(), fur.AssignedToId, 4,
               N'Follow-up due: ' + l.FullName,
               N'Your follow-up with ' + l.FullName + N' (' + l.Phone + N') is due today.',
               fur.LeadId,
               0,
               SYSUTCDATETIME()
        FROM [_template].FollowUpReminders fur
        INNER JOIN [_template].Leads l ON l.Id = fur.LeadId
        WHERE fur.ReminderDate <= @Today
          AND fur.Status = 1
          AND (fur.SnoozedUntil IS NULL OR fur.SnoozedUntil <= @Today);

        UPDATE [_template].FollowUpReminders
        SET Status = 2,
            UpdatedAt = SYSUTCDATETIME()
        WHERE ReminderDate <= @Today
          AND Status = 1
          AND (SnoozedUntil IS NULL OR SnoozedUntil <= @Today);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
-- ==== _template.sp_UpsertLead (SQL_STORED_PROCEDURE)

CREATE   PROCEDURE [_template].[sp_UpsertLead]
    @BranchId           UNIQUEIDENTIFIER,
    @RegisteredById     UNIQUEIDENTIFIER,
    @FullName           NVARCHAR(200),
    @Phone              NVARCHAR(30),
    @LeadSource         TINYINT,
    @CampaignId         UNIQUEIDENTIFIER = NULL,
    @AssignedSalesId    UNIQUEIDENTIFIER = NULL,
    @Notes              NVARCHAR(MAX)    = NULL,
    @InterestedJobTitle NVARCHAR(200)    = NULL,
    @InterestedCountry  NVARCHAR(100)    = NULL,
    @ReferredByName     NVARCHAR(200)    = NULL,
    @ReferredByPhone    NVARCHAR(30)     = NULL,
    @FacebookLeadId     NVARCHAR(100)    = NULL,
    @FacebookFormId     NVARCHAR(100)    = NULL,
    @UtmSource          NVARCHAR(100)    = NULL,
    @UtmMedium          NVARCHAR(100)    = NULL,
    @UtmCampaign        NVARCHAR(200)    = NULL,
    @UtmContent         NVARCHAR(200)    = NULL,
    @LeadId             UNIQUEIDENTIFIER = NULL OUTPUT,
    @WasDuplicate       BIT              = 0 OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ExistingId UNIQUEIDENTIFIER;
    SELECT @ExistingId = Id FROM [_template].Leads WHERE Phone = @Phone;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @ExistingId IS NOT NULL
        BEGIN
            SET @WasDuplicate = 1;
            SET @LeadId = @ExistingId;

            UPDATE [_template].Leads
            SET FullName           = ISNULL(@FullName, FullName),
                CampaignId         = ISNULL(@CampaignId, CampaignId),
                Notes              = CASE WHEN @Notes IS NOT NULL THEN ISNULL(Notes, N'') + CHAR(10) + N'[Re-entry ' + CONVERT(NVARCHAR(19), SYSUTCDATETIME(), 120) + N']: ' + @Notes ELSE Notes END,
                InterestedJobTitle = ISNULL(@InterestedJobTitle, InterestedJobTitle),
                InterestedCountry  = ISNULL(@InterestedCountry, InterestedCountry),
                FacebookLeadId     = ISNULL(@FacebookLeadId, FacebookLeadId),
                FacebookFormId     = ISNULL(@FacebookFormId, FacebookFormId),
                UtmSource          = ISNULL(@UtmSource, UtmSource),
                UtmMedium          = ISNULL(@UtmMedium, UtmMedium),
                UtmCampaign        = ISNULL(@UtmCampaign, UtmCampaign),
                UtmContent         = ISNULL(@UtmContent, UtmContent),
                UpdatedAt          = SYSUTCDATETIME()
            WHERE Id = @ExistingId;

            INSERT INTO [_template].LeadFunnelHistory (Id, LeadId, FromStatus, ToStatus, ChangedById, Note, CreatedAt)
            SELECT NEWID(), Id, Status, Status, @RegisteredById,
                   N'Duplicate entry from source ' + CAST(@LeadSource AS NVARCHAR(10)),
                   SYSUTCDATETIME()
            FROM [_template].Leads
            WHERE Id = @ExistingId;
        END
        ELSE
        BEGIN
            SET @WasDuplicate = 0;
            SET @LeadId = NEWID();

            INSERT INTO [_template].Leads
                (Id, BranchId, AssignedSalesId, CampaignId, RegisteredById, FullName, Phone,
                 LeadSource, Status, Notes, InterestedJobTitle, InterestedCountry,
                 ReferredByName, ReferredByPhone, FacebookLeadId, FacebookFormId,
                 UtmSource, UtmMedium, UtmCampaign, UtmContent, IsConverted, IsDuplicate, CreatedAt)
            VALUES
                (@LeadId, @BranchId, @AssignedSalesId, @CampaignId, @RegisteredById, @FullName, @Phone,
                 @LeadSource, 1, @Notes, @InterestedJobTitle, @InterestedCountry,
                 @ReferredByName, @ReferredByPhone, @FacebookLeadId, @FacebookFormId,
                 @UtmSource, @UtmMedium, @UtmCampaign, @UtmContent, 0, 0, SYSUTCDATETIME());

            INSERT INTO [_template].LeadFunnelHistory (Id, LeadId, FromStatus, ToStatus, ChangedById, Note, CreatedAt)
            VALUES (NEWID(), @LeadId, NULL, 1, @RegisteredById,
                    N'Lead created — Source: ' + CAST(@LeadSource AS NVARCHAR(10)) +
                    CASE WHEN @ReferredByName IS NOT NULL THEN N' — Referred by: ' + @ReferredByName ELSE N'' END,
                    SYSUTCDATETIME());
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
-- ==== _template.sp_WalkInCheckin (SQL_STORED_PROCEDURE)

CREATE   PROCEDURE [_template].[sp_WalkInCheckin]
    @Phone               NVARCHAR(30),
    @FullName            NVARCHAR(200)    = NULL,
    @LeadSource          TINYINT          = NULL,
    @ReferredByName      NVARCHAR(200)    = NULL,
    @ReferredByPhone     NVARCHAR(30)     = NULL,
    @BranchId            UNIQUEIDENTIFIER,
    @ReceptionUserId     UNIQUEIDENTIFIER,
    @AssignedSalesUserId UNIQUEIDENTIFIER,
    @JobPackageId        UNIQUEIDENTIFIER,
    @VisitNotes          NVARCHAR(1000)   = NULL,
    @VisitId             UNIQUEIDENTIFIER = NULL OUTPUT,
    @LeadId              UNIQUEIDENTIFIER = NULL OUTPUT,
    @CandidateId         UNIQUEIDENTIFIER = NULL OUTPUT,
    @WasExistingLead     BIT              = 0 OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM [_template].JobPackages WHERE Id = @JobPackageId AND IsActive = 1)
    BEGIN
        RAISERROR('Job package not found or inactive.', 16, 1);
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM [_template].Users WHERE Id = @AssignedSalesUserId AND IsActive = 1 AND Role = 3)
    BEGIN
        RAISERROR('Assigned sales user not found or inactive.', 16, 1);
        RETURN;
    END

    DECLARE @OldLeadStatus TINYINT;
    SELECT @LeadId = Id, @OldLeadStatus = Status
    FROM [_template].Leads
    WHERE Phone = @Phone AND IsDuplicate = 0;

    IF @LeadId IS NOT NULL
    BEGIN
        SET @WasExistingLead = 1;

        IF EXISTS (SELECT 1 FROM [_template].Leads WHERE Id = @LeadId AND IsConverted = 1)
        BEGIN
            RAISERROR('Lead already converted to a candidate.', 16, 1);
            RETURN;
        END
    END
    ELSE
    BEGIN
        SET @WasExistingLead = 0;

        IF @FullName IS NULL
        BEGIN
            RAISERROR('FullName required for new lead.', 16, 1);
            RETURN;
        END

        IF @LeadSource IS NULL
        BEGIN
            RAISERROR('LeadSource required for new lead.', 16, 1);
            RETURN;
        END

        IF @LeadSource = 6 AND @ReferredByName IS NULL
        BEGIN
            RAISERROR('ReferredByName required for Referral source.', 16, 1);
            RETURN;
        END

        SET @OldLeadStatus = 1;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @WasExistingLead = 0
        BEGIN
            SET @LeadId = NEWID();

            INSERT INTO [_template].Leads
                (Id, BranchId, AssignedSalesId, RegisteredById, FullName, Phone,
                 LeadSource, Status, ReferredByName, ReferredByPhone,
                 IsConverted, IsDuplicate, CreatedAt)
            VALUES
                (@LeadId, @BranchId, @AssignedSalesUserId, @ReceptionUserId,
                 @FullName, @Phone, @LeadSource, 1, @ReferredByName, @ReferredByPhone,
                 0, 0, SYSUTCDATETIME());

            INSERT INTO [_template].LeadFunnelHistory (Id, LeadId, FromStatus, ToStatus, ChangedById, Note, CreatedAt)
            VALUES
                (NEWID(), @LeadId, NULL, 1, @ReceptionUserId,
                 N'Walk-in new lead — Source: ' + CAST(@LeadSource AS NVARCHAR(10)) +
                 CASE WHEN @ReferredByName IS NOT NULL THEN N' — Referred by: ' + @ReferredByName ELSE N'' END,
                 SYSUTCDATETIME());
        END
        ELSE
        BEGIN
            UPDATE [_template].Leads
            SET AssignedSalesId = @AssignedSalesUserId
            WHERE Id = @LeadId AND AssignedSalesId IS NULL;
        END

        SET @VisitId = NEWID();

        INSERT INTO [_template].LeadVisits
            (Id, LeadId, BranchId, ReceptionUserId, AssignedSalesUserId, VisitDateTime, MeetingOutcome, JobPackageId, Notes, CreatedAt)
        VALUES
            (@VisitId, @LeadId, @BranchId, @ReceptionUserId, @AssignedSalesUserId, SYSUTCDATETIME(), 4, @JobPackageId, @VisitNotes, SYSUTCDATETIME());

        UPDATE [_template].Leads
        SET Status = 6,
            LastContactedAt = SYSUTCDATETIME(),
            UpdatedAt = SYSUTCDATETIME()
        WHERE Id = @LeadId;

        INSERT INTO [_template].LeadFunnelHistory (Id, LeadId, FromStatus, ToStatus, ChangedById, Note, CreatedAt)
        VALUES (NEWID(), @LeadId, @OldLeadStatus, 6, @ReceptionUserId, N'Walk-in check-in at office', SYSUTCDATETIME());

        EXEC [_template].sp_ConvertLeadToCandidate
            @LeadId = @LeadId,
            @JobPackageId = @JobPackageId,
            @ConvertedById = @ReceptionUserId,
            @CandidateId = @CandidateId OUTPUT;

        UPDATE [_template].Candidates
        SET IsProfileComplete = 0
        WHERE Id = @CandidateId;

        UPDATE [_template].LeadVisits
        SET ConvertedCandidateId = @CandidateId
        WHERE Id = @VisitId;

        INSERT INTO [_template].LeadActivities
            (Id, LeadId, ActivityType, Description, Details, CreatedById, ActorType, EntityId, EntityType, CreatedAt)
        VALUES
            (NEWID(), @LeadId, 7, N'Converted to candidate at walk-in',
             N'{"candidateId":"' + CAST(@CandidateId AS NVARCHAR(36)) + N'","visitId":"' + CAST(@VisitId AS NVARCHAR(36)) + N'","isProfileComplete":false}',
             @ReceptionUserId, 1, @CandidateId, N'Candidate', SYSUTCDATETIME());

        UPDATE [_template].FollowUpReminders
        SET Status = 3,
            DismissedAt = SYSUTCDATETIME(),
            DismissedById = @ReceptionUserId,
            UpdatedAt = SYSUTCDATETIME()
        WHERE LeadId = @LeadId
          AND Status IN (1, 4);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
-- ==== demorecruitment.sp_ConvertLeadToCandidate (SQL_STORED_PROCEDURE)

CREATE   PROCEDURE [demorecruitment].[sp_ConvertLeadToCandidate]
    @LeadId          UNIQUEIDENTIFIER,
    @JobPackageId    UNIQUEIDENTIFIER,
    @ConvertedById   UNIQUEIDENTIFIER,
    @CandidateId     UNIQUEIDENTIFIER = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS
    (
        SELECT 1
        FROM [demorecruitment].[Leads]
        WHERE Id = @LeadId
          AND IsConverted = 0
          AND IsDuplicate = 0
    )
    BEGIN
        RAISERROR('Lead not found, already converted, or duplicate.', 16, 1);
        RETURN;
    END

    IF NOT EXISTS
    (
        SELECT 1
        FROM [demorecruitment].[JobPackages]
        WHERE Id = @JobPackageId
          AND IsActive = 1
    )
    BEGIN
        RAISERROR('Job package not found or inactive.', 16, 1);
        RETURN;
    END

    DECLARE @FirstStageId UNIQUEIDENTIFIER;

    SELECT TOP 1
        @FirstStageId = Id
    FROM [demorecruitment].[PackageStages]
    WHERE PackageId = @JobPackageId
      AND IsActive = 1
    ORDER BY StageOrder ASC;

    SET @CandidateId = NEWID();

    INSERT INTO [demorecruitment].[Candidates]
    (
        Id,
        BranchId,
        AssignedSalesId,
        JobPackageId,
        RegisteredById,
        FullName,
        Phone,
        CurrentPackageStageId,
        Status,
        TotalPaidEGP,
        IsCompleted,
        IsProfileComplete,
        MilitaryStatus
    )
    SELECT
        @CandidateId,
        BranchId,
        ISNULL(AssignedOfficeSalesId, @ConvertedById),
        @JobPackageId,
        @ConvertedById,
        FullName,
        Phone,
        @FirstStageId,
        1,
        0,
        0,
        0,
        N''
    FROM [demorecruitment].[Leads]
    WHERE Id = @LeadId;

    INSERT INTO [demorecruitment].[CandidateStageHistory]
    (
        CandidateId,
        FromStage,
        ToStage,
        FromStageId,
        ToStageId,
        ChangedById,
        IsOverride,
        OverrideReason
    )
    VALUES
    (
        @CandidateId,
        NULL,
        1,
        NULL,
        @FirstStageId,
        @ConvertedById,
        0,
        NULL
    );

    UPDATE [demorecruitment].[Leads]
    SET
        IsConverted = 1,
        ConvertedAt = SYSUTCDATETIME(),
        ConvertedCandidateId = @CandidateId,
        Status = 7,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @LeadId;

    INSERT INTO [demorecruitment].[LeadFunnelHistory]
    (
        LeadId,
        FromStatus,
        ToStatus,
        ChangedById,
        Note
    )
    VALUES
    (
        @LeadId,
        6,
        7,
        @ConvertedById,
        N'Converted to Candidate — ID: ' + CAST(@CandidateId AS NVARCHAR(36))
    );
END
GO
-- ==== demorecruitment.sp_MoveToNextStage (SQL_STORED_PROCEDURE)
-- ══════════════════════════════════════════════════════════════════════════════
-- sp_MoveToNextStage — Fixed Commission Logic
-- Changes:
--   1. DealsThisMonth now counts Status != 4 (all non-reversed) instead of IN (2,3)
--   2. Commission only created for Office Sales users (Role = 6)
--   3. Tier lookup uses correct DealsThisMonth count
-- Run on: MainDb → demorecruitment schema
-- ══════════════════════════════════════════════════════════════════════════════

CREATE   PROCEDURE [demorecruitment].[sp_MoveToNextStage]
    @CandidateId    UNIQUEIDENTIFIER,
    @MovedById      UNIQUEIDENTIFIER,
    @Notes          NVARCHAR(500) = NULL,
    @IsOverride     BIT           = 0,
    @OverrideReason NVARCHAR(500) = NULL,
    @Success        BIT           = 0   OUTPUT,
    @Message        NVARCHAR(500) = ''  OUTPUT,
    @NewStageName   NVARCHAR(200) = ''  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @CurrentStageId    UNIQUEIDENTIFIER;
    DECLARE @CurrentStageOrder INT;
    DECLARE @PackageId         UNIQUEIDENTIFIER;
    DECLARE @TotalPaid         DECIMAL(18,2);
    DECLARE @AssignedSalesId   UNIQUEIDENTIFIER;

    SELECT
        @CurrentStageId    = c.CurrentPackageStageId,
        @CurrentStageOrder = ps.StageOrder,
        @PackageId         = c.JobPackageId,
        @TotalPaid         = c.TotalPaidEGP,
        @AssignedSalesId   = c.AssignedSalesId
    FROM demorecruitment.Candidates c
    LEFT JOIN demorecruitment.PackageStages ps ON ps.Id = c.CurrentPackageStageId
    WHERE c.Id = @CandidateId;

    DECLARE @NextStageId     UNIQUEIDENTIFIER;
    DECLARE @NextStageName   NVARCHAR(200);
    DECLARE @NextStageOrder  INT;
    DECLARE @NextNotifySales BIT;
    DECLARE @NextNotifyAdmin BIT;
    DECLARE @NextStageCode   NVARCHAR(100);

    SELECT TOP 1
        @NextStageId     = ps.Id,
        @NextStageName   = ps.StageName,
        @NextStageOrder  = ps.StageOrder,
        @NextNotifySales = ps.NotifySalesOnEnter,
        @NextNotifyAdmin = ps.NotifyAdminOnEnter,
        @NextStageCode   = st.StageCode
    FROM demorecruitment.PackageStages ps
    LEFT JOIN demorecruitment.StageTypes st ON st.Id = ps.StageTypeId
    WHERE ps.PackageId  = @PackageId
      AND ps.StageOrder > ISNULL(@CurrentStageOrder, 0)
      AND ps.IsActive   = 1
    ORDER BY ps.StageOrder ASC;

    IF @NextStageId IS NULL
    BEGIN
        SET @Success = 0;
        SET @Message = N'المرشح في آخر مرحلة من الباقة';
        RETURN;
    END

    -- ── Check 1: min payment ──────────────────────────────────────────────────
    DECLARE @CurrentMinPay DECIMAL(18,2);
    SELECT @CurrentMinPay = RequiredMinPaymentEGP
    FROM demorecruitment.PackageStages
    WHERE Id = @CurrentStageId;

    IF @CurrentMinPay IS NOT NULL AND @TotalPaid < @CurrentMinPay AND @IsOverride = 0
    BEGIN
        SET @Success = 0;
        SET @Message = N'PAYMENT_EXCEPTION_REQUIRED:'
            + CAST(@CurrentStageId AS NVARCHAR(36)) + N':'
            + CAST(@NextStageId    AS NVARCHAR(36)) + N':'
            + CAST(@CurrentMinPay  AS NVARCHAR(20)) + N':'
            + CAST(@TotalPaid      AS NVARCHAR(20));
        RETURN;
    END

    -- ── Check 2: document required ────────────────────────────────────────────
    DECLARE @CurrentStageCode  NVARCHAR(100);
    DECLARE @CurrentActionType TINYINT;
    DECLARE @RequiredDocType   TINYINT;

    SELECT
        @CurrentStageCode  = st.StageCode,
        @CurrentActionType = st.RequiredAction,
        @RequiredDocType   = st.DocumentTypeRequired
    FROM demorecruitment.PackageStages ps
    LEFT JOIN demorecruitment.StageTypes st ON st.Id = ps.StageTypeId
    WHERE ps.Id = @CurrentStageId;

    IF (@CurrentActionType IN (1, 4)) AND @IsOverride = 0
    BEGIN
        DECLARE @DocCount INT = 0;
        SELECT @DocCount = COUNT(*)
        FROM demorecruitment.Documents
        WHERE CandidateId = @CandidateId
          AND (@RequiredDocType IS NULL OR DocumentType = @RequiredDocType);

        IF @DocCount = 0
        BEGIN
            SET @Success = 0;
            SET @Message = N'DOCUMENT_REQUIRED:' + ISNULL(@CurrentStageCode, N'UNKNOWN');
            RETURN;
        END
    END

    -- ── Check 3: admin approval required ─────────────────────────────────────
    IF EXISTS (
        SELECT 1 FROM demorecruitment.PackageStages
        WHERE Id = @CurrentStageId AND RequiresAdminApproval = 1
    ) AND @IsOverride = 0
    BEGIN
        SET @Success = 0;
        SET @Message = N'REQUIRES_APPROVAL:'
            + CAST(@CurrentStageId AS NVARCHAR(36)) + N':'
            + CAST(@NextStageId    AS NVARCHAR(36));
        RETURN;
    END

    -- ── All checks passed — move the candidate ────────────────────────────────
    BEGIN TRANSACTION;
    BEGIN TRY
        UPDATE demorecruitment.Candidates
        SET CurrentPackageStageId = @NextStageId,
            UpdatedAt             = SYSUTCDATETIME()
        WHERE Id = @CandidateId;

        INSERT INTO demorecruitment.CandidateStageHistory
            (CandidateId, FromStage, ToStage, FromStageId, ToStageId,
             ChangedById, IsOverride, OverrideReason, Notes)
        VALUES
            (@CandidateId,
             @CurrentStageOrder, @NextStageOrder,
             @CurrentStageId,    @NextStageId,
             @MovedById, @IsOverride, @OverrideReason, @Notes);

        INSERT INTO demorecruitment.CandidateActivities
            (CandidateId, ActivityType, Description, Details, CreatedById)
        VALUES
            (@CandidateId, 1,
             N'انتقل إلى مرحلة: ' + @NextStageName,
             N'{"fromStage":"' + ISNULL(CAST(@CurrentStageOrder AS NVARCHAR), '0') +
             N'","toStage":"'  + CAST(@NextStageOrder AS NVARCHAR) + N'"}',
             @MovedById);

        -- ── Only insert StageActionCompletion if candidate was on a real stage ──
        IF @CurrentStageId IS NOT NULL
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM demorecruitment.StageActionCompletions
                WHERE CandidateId = @CandidateId AND PackageStageId = @CurrentStageId
            )
            BEGIN
                INSERT INTO demorecruitment.StageActionCompletions
                    (CandidateId, PackageStageId, CompletedAt, CompletedById, CompletionType)
                VALUES
                    (@CandidateId, @CurrentStageId, SYSUTCDATETIME(), @MovedById, 5);
            END
        END

        -- ── Commission calc — ONLY for Office Sales (Role = 6), ONLY at final stage ──
        DECLARE @IsFinalStage BIT = 0;
        SELECT @IsFinalStage = CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END
        FROM demorecruitment.PackageStages
        WHERE PackageId  = @PackageId
          AND StageOrder > @NextStageOrder
          AND IsActive   = 1;

        -- Check assigned sales user is Role 6 (Office Sales)
        DECLARE @AssignedSalesRole TINYINT = NULL;
        IF @AssignedSalesId IS NOT NULL
        BEGIN
            SELECT @AssignedSalesRole = Role
            FROM demorecruitment.Users
            WHERE Id = @AssignedSalesId;
        END

        IF @IsFinalStage = 1
           AND @AssignedSalesId IS NOT NULL
           AND @AssignedSalesRole = 6   -- Office Sales only
        BEGIN
            DECLARE @Month INT = MONTH(SYSUTCDATETIME());
            DECLARE @Year  INT = YEAR(SYSUTCDATETIME());

            -- Count ALL non-reversed commissions this month (pending + approved + paid)
            DECLARE @DealsThisMonth INT;
            SELECT @DealsThisMonth = COUNT(*) + 1
            FROM demorecruitment.Commissions
            WHERE SalesUserId     = @AssignedSalesId
              AND MONTH(CreatedAt) = @Month
              AND YEAR(CreatedAt)  = @Year
              AND Status          != 4;  -- exclude reversed only

            DECLARE @TierAmount DECIMAL(18,2) = 0;
            SELECT TOP 1
                @TierAmount = AmountPerDeal
            FROM demorecruitment.CommissionTiers
            WHERE IsActive  = 1
              AND MinDeals  <= @DealsThisMonth
              AND (MaxDeals >= @DealsThisMonth OR MaxDeals IS NULL)
            ORDER BY MinDeals DESC;

            DECLARE @CommissionAmount DECIMAL(18,2) = ISNULL(@TierAmount, 0);

            IF NOT EXISTS (
                SELECT 1 FROM demorecruitment.Commissions
                WHERE CandidateId = @CandidateId
            )
            BEGIN
                INSERT INTO demorecruitment.Commissions
                    (SalesUserId, CandidateId, CommissionMonth, AmountEGP,
                     DealsThisMonth, Status, CreatedAt)
                VALUES
                    (@AssignedSalesId, @CandidateId,
                     DATEFROMPARTS(@Year, @Month, 1),
                     @CommissionAmount, @DealsThisMonth, 1, SYSUTCDATETIME());
            END
            ELSE
            BEGIN
                UPDATE demorecruitment.Commissions
                SET AmountEGP      = @CommissionAmount,
                    DealsThisMonth = @DealsThisMonth
                WHERE CandidateId = @CandidateId AND Status = 1;
            END

            -- Update other pending commissions for same sales user this month
            UPDATE demorecruitment.Commissions
            SET AmountEGP = ISNULL((
                SELECT TOP 1 AmountPerDeal
                FROM demorecruitment.CommissionTiers
                WHERE IsActive = 1
                  AND MinDeals <= @DealsThisMonth
                  AND (MaxDeals >= @DealsThisMonth OR MaxDeals IS NULL)
                ORDER BY MinDeals DESC
            ), 0)
            WHERE SalesUserId     = @AssignedSalesId
              AND MONTH(CreatedAt) = @Month
              AND YEAR(CreatedAt)  = @Year
              AND Status          = 1
              AND CandidateId    != @CandidateId;
        END

        COMMIT TRANSACTION;
        SET @Success      = 1;
        SET @Message      = N'تم الانتقال إلى مرحلة "' + @NextStageName + N'" بنجاح';
        SET @NewStageName = @NextStageName;

    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        SET @Success = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO
-- ==== demorecruitment.sp_ProcessDueReminders (SQL_STORED_PROCEDURE)

CREATE PROCEDURE [demorecruitment].[sp_ProcessDueReminders]
    @Today DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Today IS NULL
        SET @Today = CAST(SYSUTCDATETIME() AS DATE);

    BEGIN TRANSACTION;

    BEGIN TRY
        INSERT INTO [demorecruitment].[Notifications]
            (UserId, Type, Title, Body, EntityId)
        SELECT
            fur.AssignedToId,
            4,
            N'Follow-up due: ' + l.FullName,
            N'Your follow-up with ' + l.FullName + N' (' + l.Phone + N') is due today.',
            fur.LeadId
        FROM [demorecruitment].[FollowUpReminders] fur
        INNER JOIN [demorecruitment].[Leads] l
            ON l.Id = fur.LeadId
        WHERE fur.ReminderDate <= @Today
          AND fur.Status = 1
          AND (fur.SnoozedUntil IS NULL OR fur.SnoozedUntil <= @Today);

        UPDATE [demorecruitment].[FollowUpReminders]
        SET Status = 2,
            UpdatedAt = SYSUTCDATETIME()
        WHERE ReminderDate <= @Today
          AND Status = 1
          AND (SnoozedUntil IS NULL OR SnoozedUntil <= @Today);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
-- ==== demorecruitment.sp_UpsertLead (SQL_STORED_PROCEDURE)

CREATE   PROCEDURE [demorecruitment].[sp_UpsertLead]
    @BranchId UNIQUEIDENTIFIER,
    @RegisteredById UNIQUEIDENTIFIER,
    @FullName NVARCHAR(200),
    @Phone NVARCHAR(30),
    @LeadSource TINYINT,
    @CampaignId UNIQUEIDENTIFIER = NULL,
    @AssignedSalesId UNIQUEIDENTIFIER = NULL,
    @Notes NVARCHAR(MAX) = NULL,
    @InterestedJobTitle NVARCHAR(200) = NULL,
    @InterestedCountry NVARCHAR(100) = NULL,
    @ReferredByName NVARCHAR(200) = NULL,
    @ReferredByPhone NVARCHAR(30) = NULL,
    @FacebookLeadId NVARCHAR(100) = NULL,
    @FacebookFormId NVARCHAR(100) = NULL,
    @UtmSource NVARCHAR(100) = NULL,
    @UtmMedium NVARCHAR(100) = NULL,
    @UtmCampaign NVARCHAR(200) = NULL,
    @UtmContent NVARCHAR(200) = NULL,
    @LeadId UNIQUEIDENTIFIER = NULL OUTPUT,
    @WasDuplicate BIT = 0 OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ExistingId UNIQUEIDENTIFIER;
    SELECT @ExistingId = Id FROM [demorecruitment].Leads WHERE Phone = @Phone;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @ExistingId IS NOT NULL
        BEGIN
            SET @WasDuplicate = 1;
            SET @LeadId = @ExistingId;

            UPDATE [demorecruitment].Leads
            SET FullName           = ISNULL(@FullName, FullName),
                CampaignId         = ISNULL(@CampaignId, CampaignId),
                Notes              = CASE WHEN @Notes IS NOT NULL THEN ISNULL(Notes, N'') + CHAR(10) + N'[Re-entry ' + CONVERT(NVARCHAR(19), SYSUTCDATETIME(), 120) + N']: ' + @Notes ELSE Notes END,
                InterestedJobTitle = ISNULL(@InterestedJobTitle, InterestedJobTitle),
                InterestedCountry  = ISNULL(@InterestedCountry, InterestedCountry),
                FacebookLeadId     = ISNULL(@FacebookLeadId, FacebookLeadId),
                FacebookFormId     = ISNULL(@FacebookFormId, FacebookFormId),
                UtmSource          = ISNULL(@UtmSource, UtmSource),
                UtmMedium          = ISNULL(@UtmMedium, UtmMedium),
                UtmCampaign        = ISNULL(@UtmCampaign, UtmCampaign),
                UtmContent         = ISNULL(@UtmContent, UtmContent),
                UpdatedAt          = SYSUTCDATETIME()
            WHERE Id = @ExistingId;

            INSERT INTO [demorecruitment].LeadFunnelHistory (Id, LeadId, FromStatus, ToStatus, ChangedById, Note, CreatedAt)
            SELECT NEWID(), Id, Status, Status, @RegisteredById,
                   N'Duplicate entry from source ' + CAST(@LeadSource AS NVARCHAR(10)),
                   SYSUTCDATETIME()
            FROM [demorecruitment].Leads
            WHERE Id = @ExistingId;
        END
        ELSE
        BEGIN
            SET @WasDuplicate = 0;
            SET @LeadId = NEWID();

            INSERT INTO [demorecruitment].Leads
                (Id, BranchId, AssignedSalesId, CampaignId, RegisteredById, FullName, Phone,
                 LeadSource, Status, Notes, InterestedJobTitle, InterestedCountry,
                 ReferredByName, ReferredByPhone, FacebookLeadId, FacebookFormId,
                 UtmSource, UtmMedium, UtmCampaign, UtmContent, IsConverted, IsDuplicate, CreatedAt)
            VALUES
                (@LeadId, @BranchId, @AssignedSalesId, @CampaignId, @RegisteredById, @FullName, @Phone,
                 @LeadSource, 1, @Notes, @InterestedJobTitle, @InterestedCountry,
                 @ReferredByName, @ReferredByPhone, @FacebookLeadId, @FacebookFormId,
                 @UtmSource, @UtmMedium, @UtmCampaign, @UtmContent, 0, 0, SYSUTCDATETIME());

            INSERT INTO [demorecruitment].LeadFunnelHistory (Id, LeadId, FromStatus, ToStatus, ChangedById, Note, CreatedAt)
            VALUES (NEWID(), @LeadId, NULL, 1, @RegisteredById,
                    N'Lead created — Source: ' + CAST(@LeadSource AS NVARCHAR(10)) +
                    CASE WHEN @ReferredByName IS NOT NULL THEN N' — Referred by: ' + @ReferredByName ELSE N'' END,
                    SYSUTCDATETIME());
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
-- ==== demorecruitment.sp_WalkInCheckin (SQL_STORED_PROCEDURE)

CREATE   PROCEDURE [demorecruitment].[sp_WalkInCheckin]
    @Phone NVARCHAR(30),
    @FullName NVARCHAR(200) = NULL,
    @LeadSource TINYINT = NULL,
    @ReferredByName NVARCHAR(200) = NULL,
    @ReferredByPhone NVARCHAR(30) = NULL,
    @BranchId UNIQUEIDENTIFIER,
    @ReceptionUserId UNIQUEIDENTIFIER,
    @AssignedSalesUserId UNIQUEIDENTIFIER,
    @JobPackageId UNIQUEIDENTIFIER,
    @VisitNotes NVARCHAR(1000) = NULL,
    @VisitId UNIQUEIDENTIFIER = NULL OUTPUT,
    @LeadId UNIQUEIDENTIFIER = NULL OUTPUT,
    @CandidateId UNIQUEIDENTIFIER = NULL OUTPUT,
    @WasExistingLead BIT = 0 OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM [demorecruitment].JobPackages WHERE Id = @JobPackageId AND IsActive = 1)
    BEGIN
        RAISERROR('Job package not found or inactive.', 16, 1);
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM [demorecruitment].Users WHERE Id = @AssignedSalesUserId AND IsActive = 1 AND Role = 3)
    BEGIN
        RAISERROR('Assigned sales user not found or inactive.', 16, 1);
        RETURN;
    END

    DECLARE @OldLeadStatus TINYINT;
    SELECT @LeadId = Id, @OldLeadStatus = Status
    FROM [demorecruitment].Leads
    WHERE Phone = @Phone AND IsDuplicate = 0;

    IF @LeadId IS NOT NULL
    BEGIN
        SET @WasExistingLead = 1;

        IF EXISTS (SELECT 1 FROM [demorecruitment].Leads WHERE Id = @LeadId AND IsConverted = 1)
        BEGIN
            RAISERROR('Lead already converted to a candidate.', 16, 1);
            RETURN;
        END
    END
    ELSE
    BEGIN
        SET @WasExistingLead = 0;

        IF @FullName IS NULL
        BEGIN
            RAISERROR('FullName required for new lead.', 16, 1);
            RETURN;
        END

        IF @LeadSource IS NULL
        BEGIN
            RAISERROR('LeadSource required for new lead.', 16, 1);
            RETURN;
        END

        IF @LeadSource = 6 AND @ReferredByName IS NULL
        BEGIN
            RAISERROR('ReferredByName required for Referral source.', 16, 1);
            RETURN;
        END

        SET @OldLeadStatus = 1;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @WasExistingLead = 0
        BEGIN
            SET @LeadId = NEWID();

            INSERT INTO [demorecruitment].Leads
                (Id, BranchId, AssignedSalesId, RegisteredById, FullName, Phone,
                 LeadSource, Status, ReferredByName, ReferredByPhone,
                 IsConverted, IsDuplicate, CreatedAt)
            VALUES
                (@LeadId, @BranchId, @AssignedSalesUserId, @ReceptionUserId,
                 @FullName, @Phone, @LeadSource, 1, @ReferredByName, @ReferredByPhone,
                 0, 0, SYSUTCDATETIME());

            INSERT INTO [demorecruitment].LeadFunnelHistory (Id, LeadId, FromStatus, ToStatus, ChangedById, Note, CreatedAt)
            VALUES
                (NEWID(), @LeadId, NULL, 1, @ReceptionUserId,
                 N'Walk-in new lead — Source: ' + CAST(@LeadSource AS NVARCHAR(10)) +
                 CASE WHEN @ReferredByName IS NOT NULL THEN N' — Referred by: ' + @ReferredByName ELSE N'' END,
                 SYSUTCDATETIME());
        END
        ELSE
        BEGIN
            UPDATE [demorecruitment].Leads
            SET AssignedSalesId = @AssignedSalesUserId
            WHERE Id = @LeadId AND AssignedSalesId IS NULL;
        END

        SET @VisitId = NEWID();

        INSERT INTO [demorecruitment].LeadVisits
            (Id, LeadId, BranchId, ReceptionUserId, AssignedSalesUserId, VisitDateTime, MeetingOutcome, JobPackageId, Notes, CreatedAt)
        VALUES
            (@VisitId, @LeadId, @BranchId, @ReceptionUserId, @AssignedSalesUserId, SYSUTCDATETIME(), 4, @JobPackageId, @VisitNotes, SYSUTCDATETIME());

        UPDATE [demorecruitment].Leads
        SET Status = 6,
            LastContactedAt = SYSUTCDATETIME(),
            UpdatedAt = SYSUTCDATETIME()
        WHERE Id = @LeadId;

        INSERT INTO [demorecruitment].LeadFunnelHistory (Id, LeadId, FromStatus, ToStatus, ChangedById, Note, CreatedAt)
        VALUES (NEWID(), @LeadId, @OldLeadStatus, 6, @ReceptionUserId, N'Walk-in check-in at office', SYSUTCDATETIME());

        EXEC [demorecruitment].sp_ConvertLeadToCandidate
            @LeadId = @LeadId,
            @JobPackageId = @JobPackageId,
            @ConvertedById = @ReceptionUserId,
            @CandidateId = @CandidateId OUTPUT;

        UPDATE [demorecruitment].Candidates
        SET IsProfileComplete = 0
        WHERE Id = @CandidateId;

        UPDATE [demorecruitment].LeadVisits
        SET ConvertedCandidateId = @CandidateId
        WHERE Id = @VisitId;

        INSERT INTO [demorecruitment].LeadActivities
            (Id, LeadId, ActivityType, Description, Details, CreatedById, ActorType, EntityId, EntityType, CreatedAt)
        VALUES
            (NEWID(), @LeadId, 7, N'Converted to candidate at walk-in',
             N'{"candidateId":"' + CAST(@CandidateId AS NVARCHAR(36)) + N'","visitId":"' + CAST(@VisitId AS NVARCHAR(36)) + N'","isProfileComplete":false}',
             @ReceptionUserId, 1, @CandidateId, N'Candidate', SYSUTCDATETIME());

        UPDATE [demorecruitment].FollowUpReminders
        SET Status = 3,
            DismissedAt = SYSUTCDATETIME(),
            DismissedById = @ReceptionUserId,
            UpdatedAt = SYSUTCDATETIME()
        WHERE LeadId = @LeadId
          AND Status IN (1, 4);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
-- ==== platform.sp_ProvisionTenant (SQL_STORED_PROCEDURE)

-- ============================================================
-- PART 4: Update sp_ProvisionTenant (FIXED column names)
-- ============================================================
CREATE   PROCEDURE [platform].[sp_ProvisionTenant]
    @TenantId           UNIQUEIDENTIFIER,
    @CompanyName        NVARCHAR(200),
    @SchemaName         NVARCHAR(100),
    @Subdomain          NVARCHAR(100),
    @SuperAdminId       UNIQUEIDENTIFIER,
    @SubscriptionEnd    DATE,
    @SubscriptionStatus TINYINT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @SchemaName NOT LIKE '[a-z0-9_]%' OR @SchemaName LIKE '%[^a-z0-9_]%'
    BEGIN
        RAISERROR(N'Schema name invalid.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = @SchemaName)
    BEGIN
        RAISERROR(N'Schema already exists.', 16, 1);
        RETURN;
    END

    -- ✅ FIXED: correct column names for platform.Tenants
    INSERT INTO platform.Tenants
        (Id, CompanyName, SchemaName, Subdomain, CreatedBySuperAdminId,
         SubscriptionEndDate, SubscriptionStatus, StorageUsedBytes)
    VALUES
        (@TenantId, @CompanyName, @SchemaName, @Subdomain, @SuperAdminId,
         @SubscriptionEnd, @SubscriptionStatus, 0);

    DECLARE @sql NVARCHAR(MAX);

    SET @sql = N'CREATE SCHEMA [' + @SchemaName + N']';
    EXEC sp_executesql @sql;

    SET @sql = N'
    SELECT TOP 0 * INTO [' + @SchemaName + N'].[Users]             FROM [_template].[Users];
    SELECT TOP 0 * INTO [' + @SchemaName + N'].[Branches]          FROM [_template].[Branches];
    SELECT TOP 0 * INTO [' + @SchemaName + N'].[Candidates]        FROM [_template].[Candidates];
    SELECT TOP 0 * INTO [' + @SchemaName + N'].[JobPackages]       FROM [_template].[JobPackages];
    SELECT TOP 0 * INTO [' + @SchemaName + N'].[PackageStages]     FROM [_template].[PackageStages];
    SELECT TOP 0 * INTO [' + @SchemaName + N'].[StageTypes]        FROM [_template].[StageTypes];
    SELECT TOP 0 * INTO [' + @SchemaName + N'].[Commissions]       FROM [_template].[Commissions];
    SELECT TOP 0 * INTO [' + @SchemaName + N'].[CommissionTiers]   FROM [_template].[CommissionTiers];
    SELECT TOP 0 * INTO [' + @SchemaName + N'].[Leads]             FROM [_template].[Leads];
    SELECT TOP 0 * INTO [' + @SchemaName + N'].[Payments]          FROM [_template].[Payments];
    SELECT TOP 0 * INTO [' + @SchemaName + N'].[Documents]         FROM [_template].[Documents];
    SELECT TOP 0 * INTO [' + @SchemaName + N'].[Notifications]     FROM [_template].[Notifications];
    ';
    EXEC sp_executesql @sql;

    PRINT N'Tenant provisioned: [' + @SchemaName + N']';
END
GO
-- ==== _template.trg_LCL_WriteActivity (SQL_TRIGGER)

-- ── _template ────────────────────────────────────────────────────────────────
CREATE TRIGGER [_template].[trg_LCL_WriteActivity]
ON [_template].[LeadCallLog] AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [_template].LeadActivities
        (LeadId, ActivityType, Description, Details, CreatedById, ActorType, EntityId, EntityType)
    SELECT
        i.LeadId,
        6,
        N'مكالمة — القناة: ' +
            CASE i.Channel WHEN 1 THEN N'هاتف' WHEN 2 THEN N'واتساب' ELSE N'غير معروف' END +
        N' · النتيجة: ' +
            CASE i.Outcome
                WHEN 1 THEN N'لا يرد'
                WHEN 2 THEN N'أجاب'
                WHEN 3 THEN N'سيرد لاحقاً'
                WHEN 4 THEN N'غير مهتم'
                WHEN 5 THEN N'مهتم'
                ELSE N'غير معروف'
            END +
            CASE WHEN i.Note IS NOT NULL AND i.Note != '' THEN N' · ' + i.Note ELSE N'' END,
        N'{"channel":' + CAST(i.Channel AS NVARCHAR) +
        N',"outcome":' + CAST(i.Outcome AS NVARCHAR) + N'}',
        i.CalledById,
        1,
        i.Id,
        N'LeadCallLog'
    FROM inserted i;
END
GO
-- ==== _template.trg_LV_WriteActivity (SQL_TRIGGER)

CREATE TRIGGER [_template].[trg_LV_WriteActivity]
ON [_template].[LeadVisits] AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO _template.LeadActivities
        (LeadId, ActivityType, Description, Details, CreatedById, ActorType, EntityId, EntityType)
    SELECT i.LeadId, 2,
        N'Office visit — Outcome: ' + CASE i.MeetingOutcome
            WHEN 1 THEN 'Interested' WHEN 2 THEN 'Not Interested'
            WHEN 3 THEN 'Needs More Time' WHEN 4 THEN 'Converted' WHEN 5 THEN 'No Show' ELSE 'Unknown' END,
        N'{"visitDateTime":"' + CONVERT(NVARCHAR,i.VisitDateTime,120) + '"' +
        N',"meetingOutcome":' + CAST(i.MeetingOutcome AS NVARCHAR) +
        N',"branchId":"' + CAST(i.BranchId AS NVARCHAR(36)) + '"' +
        N',"salesUserId":' + ISNULL('"' + CAST(i.AssignedSalesUserId AS NVARCHAR(36)) + '"','null') + N'}',
        i.ReceptionUserId, 1, i.Id, 'LeadVisit'
    FROM inserted i;
END
GO
-- ==== demorecruitment.trg_LCL_WriteActivity (SQL_TRIGGER)
-- Fix trg_LCL_WriteActivity to use Arabic descriptions
-- Run this in SSMS for both schemas

-- ── demorecruitment ──────────────────────────────────────────────────────────
CREATE TRIGGER [demorecruitment].[trg_LCL_WriteActivity]
ON [demorecruitment].[LeadCallLog] AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [demorecruitment].LeadActivities
        (LeadId, ActivityType, Description, Details, CreatedById, ActorType, EntityId, EntityType)
    SELECT
        i.LeadId,
        6,
        N'مكالمة — القناة: ' +
            CASE i.Channel WHEN 1 THEN N'هاتف' WHEN 2 THEN N'واتساب' ELSE N'غير معروف' END +
        N' · النتيجة: ' +
            CASE i.Outcome
                WHEN 1 THEN N'لا يرد'
                WHEN 2 THEN N'أجاب'
                WHEN 3 THEN N'سيرد لاحقاً'
                WHEN 4 THEN N'غير مهتم'
                WHEN 5 THEN N'مهتم'
                ELSE N'غير معروف'
            END +
            CASE WHEN i.Note IS NOT NULL AND i.Note != '' THEN N' · ' + i.Note ELSE N'' END,
        N'{"channel":' + CAST(i.Channel AS NVARCHAR) +
        N',"outcome":' + CAST(i.Outcome AS NVARCHAR) + N'}',
        i.CalledById,
        1,
        i.Id,
        N'LeadCallLog'
    FROM inserted i;
END
GO
-- ==== demorecruitment.trg_LV_WriteActivity (SQL_TRIGGER)
CREATE TRIGGER [demorecruitment].[trg_LV_WriteActivity]
ON [demorecruitment].[LeadVisits] AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [demorecruitment].LeadActivities
        (LeadId, ActivityType, Description, Details, CreatedById, ActorType, EntityId, EntityType)
    SELECT i.LeadId, 2,
        N'Office visit — Outcome: ' + CASE i.MeetingOutcome
            WHEN 1 THEN 'Interested' WHEN 2 THEN 'Not Interested'
            WHEN 3 THEN 'Needs More Time' WHEN 4 THEN 'Converted' WHEN 5 THEN 'No Show' ELSE 'Unknown' END,
        N'{"meetingOutcome":' + CAST(i.MeetingOutcome AS NVARCHAR) + N'}',
        i.ReceptionUserId, 1, i.Id, 'LeadVisit'
    FROM inserted i;
END
GO
-- ==== _template.vw_CampaignPerformance (VIEW)

CREATE VIEW [_template].[vw_CampaignPerformance] WITH SCHEMABINDING AS
    SELECT
        CampaignId,
        COUNT_BIG(*)                               AS TotalLeads,
        SUM(CAST(IsConverted AS INT))              AS ConvertedLeads,
        COUNT_BIG(CASE WHEN Status = 8 THEN 1 END) AS LostLeads
    FROM _template.Leads
    WHERE CampaignId IS NOT NULL AND IsDuplicate = 0
    GROUP BY CampaignId;
GO
-- ==== _template.vw_DailyLeads (VIEW)

-- =============================================================================
--  PART 3 — DASHBOARD INDEXED VIEWS  (on _template)
-- =============================================================================

-- BUG FIX: Was querying _template.Candidates — corrected to _template.Leads
CREATE VIEW [_template].[vw_DailyLeads] WITH SCHEMABINDING AS
    SELECT
        BranchId,
        RegisteredById,
        CAST(CreatedAt AS DATE)  AS LeadDate,
        COUNT_BIG(*)             AS LeadCount
    FROM _template.Leads
    WHERE IsDuplicate = 0
    GROUP BY BranchId, RegisteredById, CAST(CreatedAt AS DATE);
GO
-- ==== _template.vw_DailyPayments (VIEW)

CREATE VIEW [_template].[vw_DailyPayments] WITH SCHEMABINDING AS
    SELECT
        CAST(PaymentDate AS DATE) AS PayDate,
        COUNT_BIG(*)              AS PaymentCount,
        SUM(AmountEGP)            AS TotalEGP
    FROM _template.Payments
    WHERE TransactionType = 1
    GROUP BY CAST(PaymentDate AS DATE);
GO
-- ==== _template.vw_LeadFunnelSummary (VIEW)

CREATE VIEW [_template].[vw_LeadFunnelSummary] WITH SCHEMABINDING AS
    SELECT
        BranchId, LeadSource, Status, IsConverted,
        COUNT_BIG(*) AS LeadCount
    FROM _template.Leads
    WHERE IsDuplicate = 0
    GROUP BY BranchId, LeadSource, Status, IsConverted;
GO
-- ==== _template.vw_SalesPerformance (VIEW)

CREATE VIEW [_template].[vw_SalesPerformance] WITH SCHEMABINDING AS
    SELECT
        AssignedSalesId,
        DATEFROMPARTS(YEAR(CompletedAt), MONTH(CompletedAt), 1) AS MonthStart,
        COUNT_BIG(*)   AS CompletedDeals,
        SUM(TotalPaidEGP) AS RevenueEGP
    FROM _template.Candidates
    WHERE IsCompleted = 1 AND CompletedAt IS NOT NULL
    GROUP BY AssignedSalesId, DATEFROMPARTS(YEAR(CompletedAt), MONTH(CompletedAt), 1);
GO
-- ==== demorecruitment.vw_CampaignPerformance (VIEW)
CREATE VIEW [demorecruitment].[vw_CampaignPerformance] WITH SCHEMABINDING AS
    SELECT CampaignId, COUNT_BIG(*) AS TotalLeads,
           SUM(CAST(IsConverted AS INT)) AS ConvertedLeads,
           COUNT_BIG(CASE WHEN Status=8 THEN 1 END) AS LostLeads
    FROM [demorecruitment].Leads WHERE CampaignId IS NOT NULL AND IsDuplicate=0
    GROUP BY CampaignId
GO
-- ==== demorecruitment.vw_DailyLeads (VIEW)
CREATE VIEW [demorecruitment].[vw_DailyLeads] WITH SCHEMABINDING AS
    SELECT BranchId, RegisteredById, CAST(CreatedAt AS DATE) AS LeadDate, COUNT_BIG(*) AS LeadCount
    FROM [demorecruitment].Leads WHERE IsDuplicate=0
    GROUP BY BranchId, RegisteredById, CAST(CreatedAt AS DATE)
GO
-- ==== demorecruitment.vw_DailyPayments (VIEW)
CREATE VIEW [demorecruitment].[vw_DailyPayments] WITH SCHEMABINDING AS
    SELECT CAST(PaymentDate AS DATE) AS PayDate, COUNT_BIG(*) AS PaymentCount, SUM(AmountEGP) AS TotalEGP
    FROM [demorecruitment].Payments WHERE TransactionType=1
    GROUP BY CAST(PaymentDate AS DATE)
GO
-- ==== demorecruitment.vw_LeadFunnelSummary (VIEW)
CREATE VIEW [demorecruitment].[vw_LeadFunnelSummary] WITH SCHEMABINDING AS
    SELECT BranchId, LeadSource, Status, IsConverted, COUNT_BIG(*) AS LeadCount
    FROM [demorecruitment].Leads WHERE IsDuplicate=0
    GROUP BY BranchId, LeadSource, Status, IsConverted
GO
-- ==== demorecruitment.vw_SalesPerformance (VIEW)
CREATE VIEW [demorecruitment].[vw_SalesPerformance] WITH SCHEMABINDING AS
    SELECT AssignedSalesId, DATEFROMPARTS(YEAR(CompletedAt),MONTH(CompletedAt),1) AS MonthStart,
           COUNT_BIG(*) AS CompletedDeals, SUM(TotalPaidEGP) AS RevenueEGP
    FROM [demorecruitment].Candidates WHERE IsCompleted=1 AND CompletedAt IS NOT NULL
    GROUP BY AssignedSalesId, DATEFROMPARTS(YEAR(CompletedAt),MONTH(CompletedAt),1)
GO
