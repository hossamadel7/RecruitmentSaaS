-- Hand-ported from the SQL Server stored procedures, triggers and views (source: migration/source/modules.sql).
-- Behaviour notes kept identical to SQL Server:
--   * GUIDs that SQL Server turned into text (CAST(uniqueidentifier AS nvarchar)) are upper-case there, so upper() is used here.
--   * SYSUTCDATETIME()/GETUTCDATE() -> clock_timestamp() AT TIME ZONE 'utc' (statement-time UTC, not transaction start).
--   * TRY/CATCH + ROLLBACK -> BEGIN ... EXCEPTION block (rolls back the block's work, same as the ROLLBACK).
--   * RAISERROR(...,16,1); RETURN -> RAISE EXCEPTION (the caller receives an error, as it did from SQL Server).
--   * Triggers stay statement-level AFTER INSERT and read the transition table "inserted", like SQL Server's inserted table.
-- Run after the data copy so the triggers do not fire for copied rows.

-- ═════════════════════════════ demorecruitment ═════════════════════════════

CREATE OR REPLACE FUNCTION "demorecruitment"."sp_ConvertLeadToCandidate"(
    p_lead_id uuid, p_job_package_id uuid, p_converted_by_id uuid,
    OUT "CandidateId" uuid)
LANGUAGE plpgsql AS $$
#variable_conflict use_column
DECLARE
    v_first_stage_id uuid;
    v_candidate_id   uuid;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM "demorecruitment"."Leads"
                   WHERE "Id" = p_lead_id AND "IsConverted" = false AND "IsDuplicate" = false) THEN
        RAISE EXCEPTION 'Lead not found, already converted, or duplicate.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM "demorecruitment"."JobPackages" WHERE "Id" = p_job_package_id AND "IsActive" = true) THEN
        RAISE EXCEPTION 'Job package not found or inactive.';
    END IF;

    SELECT "Id" INTO v_first_stage_id
    FROM "demorecruitment"."PackageStages"
    WHERE "PackageId" = p_job_package_id AND "IsActive" = true
    ORDER BY "StageOrder" ASC
    LIMIT 1;

    v_candidate_id := gen_random_uuid();

    INSERT INTO "demorecruitment"."Candidates"
        ("Id", "BranchId", "AssignedSalesId", "JobPackageId", "RegisteredById", "FullName", "Phone",
         "CurrentPackageStageId", "Status", "TotalPaidEGP", "IsCompleted", "IsProfileComplete", "MilitaryStatus")
    SELECT v_candidate_id, "BranchId", COALESCE("AssignedOfficeSalesId", p_converted_by_id), p_job_package_id, p_converted_by_id,
           "FullName", "Phone", v_first_stage_id, 1, 0, false, false, ''
    FROM "demorecruitment"."Leads"
    WHERE "Id" = p_lead_id;

    INSERT INTO "demorecruitment"."CandidateStageHistory"
        ("CandidateId", "FromStage", "ToStage", "FromStageId", "ToStageId", "ChangedById", "IsOverride", "OverrideReason")
    VALUES (v_candidate_id, NULL, 1, NULL, v_first_stage_id, p_converted_by_id, false, NULL);

    UPDATE "demorecruitment"."Leads"
    SET "IsConverted" = true,
        "ConvertedAt" = clock_timestamp() AT TIME ZONE 'utc',
        "ConvertedCandidateId" = v_candidate_id,
        "Status" = 7,
        "UpdatedAt" = clock_timestamp() AT TIME ZONE 'utc'
    WHERE "Id" = p_lead_id;

    INSERT INTO "demorecruitment"."LeadFunnelHistory" ("LeadId", "FromStatus", "ToStatus", "ChangedById", "Note")
    VALUES (p_lead_id, 6, 7, p_converted_by_id, 'Converted to Candidate — ID: ' || upper(v_candidate_id::text));

    "CandidateId" := v_candidate_id;
END $$;


CREATE OR REPLACE FUNCTION "demorecruitment"."sp_MoveToNextStage"(
    p_candidate_id uuid, p_moved_by_id uuid,
    p_notes citext DEFAULT NULL, p_is_override boolean DEFAULT false, p_override_reason citext DEFAULT NULL,
    OUT "Success" boolean, OUT "Message" text, OUT "NewStageName" text)
LANGUAGE plpgsql AS $$
#variable_conflict use_column
DECLARE
    v_current_stage_id    uuid;
    v_current_stage_order integer;
    v_package_id          uuid;
    v_total_paid          numeric(18,2);
    v_assigned_sales_id   uuid;
    v_next_stage_id       uuid;
    v_next_stage_name     text;
    v_next_stage_order    integer;
    v_next_notify_sales   boolean;
    v_next_notify_admin   boolean;
    v_next_stage_code     text;
    v_current_min_pay     numeric(18,2);
    v_current_stage_code  text;
    v_current_action_type smallint;
    v_required_doc_type   smallint;
    v_doc_count           integer := 0;
    v_is_final_stage      boolean := false;
    v_assigned_sales_role smallint;
    v_month               integer;
    v_year                integer;
    v_deals_this_month    integer;
    v_tier_amount         numeric(18,2) := 0;
    v_commission_amount   numeric(18,2);
BEGIN
    SELECT c."CurrentPackageStageId", ps."StageOrder", c."JobPackageId", c."TotalPaidEGP", c."AssignedSalesId"
    INTO v_current_stage_id, v_current_stage_order, v_package_id, v_total_paid, v_assigned_sales_id
    FROM "demorecruitment"."Candidates" c
    LEFT JOIN "demorecruitment"."PackageStages" ps ON ps."Id" = c."CurrentPackageStageId"
    WHERE c."Id" = p_candidate_id;

    SELECT ps."Id", ps."StageName", ps."StageOrder", ps."NotifySalesOnEnter", ps."NotifyAdminOnEnter", st."StageCode"
    INTO v_next_stage_id, v_next_stage_name, v_next_stage_order, v_next_notify_sales, v_next_notify_admin, v_next_stage_code
    FROM "demorecruitment"."PackageStages" ps
    LEFT JOIN "demorecruitment"."StageTypes" st ON st."Id" = ps."StageTypeId"
    WHERE ps."PackageId" = v_package_id
      AND ps."StageOrder" > COALESCE(v_current_stage_order, 0)
      AND ps."IsActive" = true
    ORDER BY ps."StageOrder" ASC
    LIMIT 1;

    IF v_next_stage_id IS NULL THEN
        "Success" := false;
        "Message" := 'المرشح في آخر مرحلة من الباقة';
        RETURN;
    END IF;

    -- Check 1: min payment
    SELECT "RequiredMinPaymentEGP" INTO v_current_min_pay
    FROM "demorecruitment"."PackageStages" WHERE "Id" = v_current_stage_id;

    IF v_current_min_pay IS NOT NULL AND v_total_paid < v_current_min_pay AND p_is_override = false THEN
        "Success" := false;
        "Message" := 'PAYMENT_EXCEPTION_REQUIRED:'
            || upper(v_current_stage_id::text) || ':'
            || upper(v_next_stage_id::text) || ':'
            || v_current_min_pay::text || ':'
            || v_total_paid::text;
        RETURN;
    END IF;

    -- Check 2: document required
    SELECT st."StageCode", st."RequiredAction", st."DocumentTypeRequired"
    INTO v_current_stage_code, v_current_action_type, v_required_doc_type
    FROM "demorecruitment"."PackageStages" ps
    LEFT JOIN "demorecruitment"."StageTypes" st ON st."Id" = ps."StageTypeId"
    WHERE ps."Id" = v_current_stage_id;

    IF v_current_action_type IN (1, 4) AND p_is_override = false THEN
        SELECT count(*) INTO v_doc_count
        FROM "demorecruitment"."Documents"
        WHERE "CandidateId" = p_candidate_id
          AND (v_required_doc_type IS NULL OR "DocumentType" = v_required_doc_type);

        IF v_doc_count = 0 THEN
            "Success" := false;
            "Message" := 'DOCUMENT_REQUIRED:' || COALESCE(v_current_stage_code, 'UNKNOWN');
            RETURN;
        END IF;
    END IF;

    -- Check 3: admin approval required
    IF EXISTS (SELECT 1 FROM "demorecruitment"."PackageStages"
               WHERE "Id" = v_current_stage_id AND "RequiresAdminApproval" = true)
       AND p_is_override = false THEN
        "Success" := false;
        "Message" := 'REQUIRES_APPROVAL:' || upper(v_current_stage_id::text) || ':' || upper(v_next_stage_id::text);
        RETURN;
    END IF;

    -- All checks passed — move the candidate
    BEGIN
        UPDATE "demorecruitment"."Candidates"
        SET "CurrentPackageStageId" = v_next_stage_id,
            "UpdatedAt" = clock_timestamp() AT TIME ZONE 'utc'
        WHERE "Id" = p_candidate_id;

        INSERT INTO "demorecruitment"."CandidateStageHistory"
            ("CandidateId", "FromStage", "ToStage", "FromStageId", "ToStageId", "ChangedById", "IsOverride", "OverrideReason", "Notes")
        VALUES (p_candidate_id, v_current_stage_order, v_next_stage_order, v_current_stage_id, v_next_stage_id,
                p_moved_by_id, p_is_override, p_override_reason, p_notes);

        INSERT INTO "demorecruitment"."CandidateActivities" ("CandidateId", "ActivityType", "Description", "Details", "CreatedById")
        VALUES (p_candidate_id, 1,
                'انتقل إلى مرحلة: ' || v_next_stage_name,
                '{"fromStage":"' || COALESCE(v_current_stage_order::text, '0') || '","toStage":"' || v_next_stage_order::text || '"}',
                p_moved_by_id);

        IF v_current_stage_id IS NOT NULL THEN
            IF NOT EXISTS (SELECT 1 FROM "demorecruitment"."StageActionCompletions"
                           WHERE "CandidateId" = p_candidate_id AND "PackageStageId" = v_current_stage_id) THEN
                INSERT INTO "demorecruitment"."StageActionCompletions"
                    ("CandidateId", "PackageStageId", "CompletedAt", "CompletedById", "CompletionType")
                VALUES (p_candidate_id, v_current_stage_id, clock_timestamp() AT TIME ZONE 'utc', p_moved_by_id, 5);
            END IF;
        END IF;

        -- Commission calc — ONLY for Office Sales (Role = 6), ONLY at final stage
        SELECT CASE WHEN count(*) = 0 THEN true ELSE false END INTO v_is_final_stage
        FROM "demorecruitment"."PackageStages"
        WHERE "PackageId" = v_package_id AND "StageOrder" > v_next_stage_order AND "IsActive" = true;

        IF v_assigned_sales_id IS NOT NULL THEN
            SELECT "Role" INTO v_assigned_sales_role FROM "demorecruitment"."Users" WHERE "Id" = v_assigned_sales_id;
        END IF;

        IF v_is_final_stage AND v_assigned_sales_id IS NOT NULL AND v_assigned_sales_role = 6 THEN
            v_month := extract(month FROM clock_timestamp() AT TIME ZONE 'utc')::integer;
            v_year  := extract(year  FROM clock_timestamp() AT TIME ZONE 'utc')::integer;

            SELECT count(*) + 1 INTO v_deals_this_month
            FROM "demorecruitment"."Commissions"
            WHERE "SalesUserId" = v_assigned_sales_id
              AND extract(month FROM "CreatedAt") = v_month
              AND extract(year FROM "CreatedAt") = v_year
              AND "Status" <> 4;

            SELECT "AmountPerDeal" INTO v_tier_amount
            FROM "demorecruitment"."CommissionTiers"
            WHERE "IsActive" = true AND "MinDeals" <= v_deals_this_month
              AND ("MaxDeals" >= v_deals_this_month OR "MaxDeals" IS NULL)
            ORDER BY "MinDeals" DESC
            LIMIT 1;

            v_commission_amount := COALESCE(v_tier_amount, 0);

            IF NOT EXISTS (SELECT 1 FROM "demorecruitment"."Commissions" WHERE "CandidateId" = p_candidate_id) THEN
                INSERT INTO "demorecruitment"."Commissions"
                    ("SalesUserId", "CandidateId", "CommissionMonth", "AmountEGP", "DealsThisMonth", "Status", "CreatedAt")
                VALUES (v_assigned_sales_id, p_candidate_id, make_date(v_year, v_month, 1),
                        v_commission_amount, v_deals_this_month, 1, clock_timestamp() AT TIME ZONE 'utc');
            ELSE
                UPDATE "demorecruitment"."Commissions"
                SET "AmountEGP" = v_commission_amount, "DealsThisMonth" = v_deals_this_month
                WHERE "CandidateId" = p_candidate_id AND "Status" = 1;
            END IF;

            UPDATE "demorecruitment"."Commissions"
            SET "AmountEGP" = COALESCE((
                    SELECT "AmountPerDeal" FROM "demorecruitment"."CommissionTiers"
                    WHERE "IsActive" = true AND "MinDeals" <= v_deals_this_month
                      AND ("MaxDeals" >= v_deals_this_month OR "MaxDeals" IS NULL)
                    ORDER BY "MinDeals" DESC
                    LIMIT 1), 0)
            WHERE "SalesUserId" = v_assigned_sales_id
              AND extract(month FROM "CreatedAt") = v_month
              AND extract(year FROM "CreatedAt") = v_year
              AND "Status" = 1
              AND "CandidateId" <> p_candidate_id;
        END IF;

        "Success" := true;
        "Message" := 'تم الانتقال إلى مرحلة "' || v_next_stage_name || '" بنجاح';
        "NewStageName" := v_next_stage_name;
    EXCEPTION WHEN OTHERS THEN
        "Success" := false;
        "Message" := SQLERRM;
    END;
END $$;


CREATE OR REPLACE FUNCTION "demorecruitment"."sp_ProcessDueReminders"(p_today date DEFAULT NULL)
RETURNS void LANGUAGE plpgsql AS $$
#variable_conflict use_column
BEGIN
    IF p_today IS NULL THEN
        p_today := (clock_timestamp() AT TIME ZONE 'utc')::date;
    END IF;

    INSERT INTO "demorecruitment"."Notifications" ("UserId", "Type", "Title", "Body", "EntityId")
    SELECT fur."AssignedToId", 4,
           'Follow-up due: ' || l."FullName",
           'Your follow-up with ' || l."FullName" || ' (' || l."Phone" || ') is due today.',
           fur."LeadId"
    FROM "demorecruitment"."FollowUpReminders" fur
    INNER JOIN "demorecruitment"."Leads" l ON l."Id" = fur."LeadId"
    WHERE fur."ReminderDate" <= p_today
      AND fur."Status" = 1
      AND (fur."SnoozedUntil" IS NULL OR fur."SnoozedUntil" <= p_today);

    UPDATE "demorecruitment"."FollowUpReminders"
    SET "Status" = 2, "UpdatedAt" = clock_timestamp() AT TIME ZONE 'utc'
    WHERE "ReminderDate" <= p_today
      AND "Status" = 1
      AND ("SnoozedUntil" IS NULL OR "SnoozedUntil" <= p_today);
END $$;


CREATE OR REPLACE FUNCTION "demorecruitment"."sp_UpsertLead"(
    p_branch_id uuid, p_registered_by_id uuid, p_full_name citext, p_phone citext, p_lead_source smallint,
    p_campaign_id uuid DEFAULT NULL, p_assigned_sales_id uuid DEFAULT NULL, p_notes citext DEFAULT NULL,
    p_interested_job_title citext DEFAULT NULL, p_interested_country citext DEFAULT NULL,
    p_referred_by_name citext DEFAULT NULL, p_referred_by_phone citext DEFAULT NULL,
    p_facebook_lead_id citext DEFAULT NULL, p_facebook_form_id citext DEFAULT NULL,
    p_utm_source citext DEFAULT NULL, p_utm_medium citext DEFAULT NULL,
    p_utm_campaign citext DEFAULT NULL, p_utm_content citext DEFAULT NULL,
    OUT "LeadId" uuid, OUT "WasDuplicate" boolean)
LANGUAGE plpgsql AS $$
#variable_conflict use_column
DECLARE
    v_existing_id uuid;
    v_lead_id     uuid;
BEGIN
    SELECT "Id" INTO v_existing_id FROM "demorecruitment"."Leads" WHERE "Phone" = p_phone LIMIT 1;

    IF v_existing_id IS NOT NULL THEN
        "WasDuplicate" := true;
        v_lead_id := v_existing_id;

        UPDATE "demorecruitment"."Leads"
        SET "FullName"           = COALESCE(p_full_name, "FullName"),
            "CampaignId"         = COALESCE(p_campaign_id, "CampaignId"),
            "Notes"              = CASE WHEN p_notes IS NOT NULL
                                        THEN COALESCE("Notes", '') || chr(10) || '[Re-entry ' || to_char(clock_timestamp() AT TIME ZONE 'utc', 'YYYY-MM-DD HH24:MI:SS') || ']: ' || p_notes
                                        ELSE "Notes" END,
            "InterestedJobTitle" = COALESCE(p_interested_job_title, "InterestedJobTitle"),
            "InterestedCountry"  = COALESCE(p_interested_country, "InterestedCountry"),
            "FacebookLeadId"     = COALESCE(p_facebook_lead_id, "FacebookLeadId"),
            "FacebookFormId"     = COALESCE(p_facebook_form_id, "FacebookFormId"),
            "UtmSource"          = COALESCE(p_utm_source, "UtmSource"),
            "UtmMedium"          = COALESCE(p_utm_medium, "UtmMedium"),
            "UtmCampaign"        = COALESCE(p_utm_campaign, "UtmCampaign"),
            "UtmContent"         = COALESCE(p_utm_content, "UtmContent"),
            "UpdatedAt"          = clock_timestamp() AT TIME ZONE 'utc'
        WHERE "Id" = v_existing_id;

        INSERT INTO "demorecruitment"."LeadFunnelHistory" ("Id", "LeadId", "FromStatus", "ToStatus", "ChangedById", "Note", "CreatedAt")
        SELECT gen_random_uuid(), "Id", "Status", "Status", p_registered_by_id,
               'Duplicate entry from source ' || p_lead_source::text,
               clock_timestamp() AT TIME ZONE 'utc'
        FROM "demorecruitment"."Leads" WHERE "Id" = v_existing_id;
    ELSE
        "WasDuplicate" := false;
        v_lead_id := gen_random_uuid();

        INSERT INTO "demorecruitment"."Leads"
            ("Id", "BranchId", "AssignedSalesId", "CampaignId", "RegisteredById", "FullName", "Phone",
             "LeadSource", "Status", "Notes", "InterestedJobTitle", "InterestedCountry",
             "ReferredByName", "ReferredByPhone", "FacebookLeadId", "FacebookFormId",
             "UtmSource", "UtmMedium", "UtmCampaign", "UtmContent", "IsConverted", "IsDuplicate", "CreatedAt")
        VALUES
            (v_lead_id, p_branch_id, p_assigned_sales_id, p_campaign_id, p_registered_by_id, p_full_name, p_phone,
             p_lead_source, 1, p_notes, p_interested_job_title, p_interested_country,
             p_referred_by_name, p_referred_by_phone, p_facebook_lead_id, p_facebook_form_id,
             p_utm_source, p_utm_medium, p_utm_campaign, p_utm_content, false, false, clock_timestamp() AT TIME ZONE 'utc');

        INSERT INTO "demorecruitment"."LeadFunnelHistory" ("Id", "LeadId", "FromStatus", "ToStatus", "ChangedById", "Note", "CreatedAt")
        VALUES (gen_random_uuid(), v_lead_id, NULL, 1, p_registered_by_id,
                'Lead created — Source: ' || p_lead_source::text ||
                CASE WHEN p_referred_by_name IS NOT NULL THEN ' — Referred by: ' || p_referred_by_name ELSE '' END,
                clock_timestamp() AT TIME ZONE 'utc');
    END IF;

    "LeadId" := v_lead_id;
END $$;


CREATE OR REPLACE FUNCTION "demorecruitment"."sp_WalkInCheckin"(
    p_phone citext, p_full_name citext DEFAULT NULL, p_lead_source smallint DEFAULT NULL,
    p_referred_by_name citext DEFAULT NULL, p_referred_by_phone citext DEFAULT NULL,
    p_branch_id uuid DEFAULT NULL, p_reception_user_id uuid DEFAULT NULL,
    p_assigned_sales_user_id uuid DEFAULT NULL, p_job_package_id uuid DEFAULT NULL,
    p_visit_notes citext DEFAULT NULL,
    OUT "VisitId" uuid, OUT "LeadId" uuid, OUT "CandidateId" uuid, OUT "WasExistingLead" boolean)
LANGUAGE plpgsql AS $$
#variable_conflict use_column
DECLARE
    v_old_lead_status smallint;
    v_lead_id         uuid;
    v_visit_id        uuid;
    v_candidate_id    uuid;
    v_was_existing    boolean;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM "demorecruitment"."JobPackages" WHERE "Id" = p_job_package_id AND "IsActive" = true) THEN
        RAISE EXCEPTION 'Job package not found or inactive.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM "demorecruitment"."Users" WHERE "Id" = p_assigned_sales_user_id AND "IsActive" = true AND "Role" = 3) THEN
        RAISE EXCEPTION 'Assigned sales user not found or inactive.';
    END IF;

    SELECT "Id", "Status" INTO v_lead_id, v_old_lead_status
    FROM "demorecruitment"."Leads"
    WHERE "Phone" = p_phone AND "IsDuplicate" = false
    LIMIT 1;

    IF v_lead_id IS NOT NULL THEN
        v_was_existing := true;
        IF EXISTS (SELECT 1 FROM "demorecruitment"."Leads" WHERE "Id" = v_lead_id AND "IsConverted" = true) THEN
            RAISE EXCEPTION 'Lead already converted to a candidate.';
        END IF;
    ELSE
        v_was_existing := false;
        IF p_full_name IS NULL THEN RAISE EXCEPTION 'FullName required for new lead.'; END IF;
        IF p_lead_source IS NULL THEN RAISE EXCEPTION 'LeadSource required for new lead.'; END IF;
        IF p_lead_source = 6 AND p_referred_by_name IS NULL THEN RAISE EXCEPTION 'ReferredByName required for Referral source.'; END IF;
        v_old_lead_status := 1;
    END IF;

    IF v_was_existing = false THEN
        v_lead_id := gen_random_uuid();

        INSERT INTO "demorecruitment"."Leads"
            ("Id", "BranchId", "AssignedSalesId", "RegisteredById", "FullName", "Phone",
             "LeadSource", "Status", "ReferredByName", "ReferredByPhone", "IsConverted", "IsDuplicate", "CreatedAt")
        VALUES
            (v_lead_id, p_branch_id, p_assigned_sales_user_id, p_reception_user_id,
             p_full_name, p_phone, p_lead_source, 1, p_referred_by_name, p_referred_by_phone,
             false, false, clock_timestamp() AT TIME ZONE 'utc');

        INSERT INTO "demorecruitment"."LeadFunnelHistory" ("Id", "LeadId", "FromStatus", "ToStatus", "ChangedById", "Note", "CreatedAt")
        VALUES (gen_random_uuid(), v_lead_id, NULL, 1, p_reception_user_id,
                'Walk-in new lead — Source: ' || p_lead_source::text ||
                CASE WHEN p_referred_by_name IS NOT NULL THEN ' — Referred by: ' || p_referred_by_name ELSE '' END,
                clock_timestamp() AT TIME ZONE 'utc');
    ELSE
        UPDATE "demorecruitment"."Leads"
        SET "AssignedSalesId" = p_assigned_sales_user_id
        WHERE "Id" = v_lead_id AND "AssignedSalesId" IS NULL;
    END IF;

    v_visit_id := gen_random_uuid();

    INSERT INTO "demorecruitment"."LeadVisits"
        ("Id", "LeadId", "BranchId", "ReceptionUserId", "AssignedSalesUserId", "VisitDateTime", "MeetingOutcome", "JobPackageId", "Notes", "CreatedAt")
    VALUES
        (v_visit_id, v_lead_id, p_branch_id, p_reception_user_id, p_assigned_sales_user_id,
         clock_timestamp() AT TIME ZONE 'utc', 4, p_job_package_id, p_visit_notes, clock_timestamp() AT TIME ZONE 'utc');

    UPDATE "demorecruitment"."Leads"
    SET "Status" = 6,
        "LastContactedAt" = clock_timestamp() AT TIME ZONE 'utc',
        "UpdatedAt" = clock_timestamp() AT TIME ZONE 'utc'
    WHERE "Id" = v_lead_id;

    INSERT INTO "demorecruitment"."LeadFunnelHistory" ("Id", "LeadId", "FromStatus", "ToStatus", "ChangedById", "Note", "CreatedAt")
    VALUES (gen_random_uuid(), v_lead_id, v_old_lead_status, 6, p_reception_user_id, 'Walk-in check-in at office', clock_timestamp() AT TIME ZONE 'utc');

    SELECT r."CandidateId" INTO v_candidate_id
    FROM "demorecruitment"."sp_ConvertLeadToCandidate"(v_lead_id, p_job_package_id, p_reception_user_id) r;

    UPDATE "demorecruitment"."Candidates" SET "IsProfileComplete" = false WHERE "Id" = v_candidate_id;

    UPDATE "demorecruitment"."LeadVisits" SET "ConvertedCandidateId" = v_candidate_id WHERE "Id" = v_visit_id;

    INSERT INTO "demorecruitment"."LeadActivities"
        ("Id", "LeadId", "ActivityType", "Description", "Details", "CreatedById", "ActorType", "EntityId", "EntityType", "CreatedAt")
    VALUES
        (gen_random_uuid(), v_lead_id, 7, 'Converted to candidate at walk-in',
         '{"candidateId":"' || upper(v_candidate_id::text) || '","visitId":"' || upper(v_visit_id::text) || '","isProfileComplete":false}',
         p_reception_user_id, 1, v_candidate_id, 'Candidate', clock_timestamp() AT TIME ZONE 'utc');

    UPDATE "demorecruitment"."FollowUpReminders"
    SET "Status" = 3,
        "DismissedAt" = clock_timestamp() AT TIME ZONE 'utc',
        "DismissedById" = p_reception_user_id,
        "UpdatedAt" = clock_timestamp() AT TIME ZONE 'utc'
    WHERE "LeadId" = v_lead_id AND "Status" IN (1, 4);

    "VisitId" := v_visit_id; "LeadId" := v_lead_id; "CandidateId" := v_candidate_id; "WasExistingLead" := v_was_existing;
END $$;


-- Triggers -------------------------------------------------------------------

CREATE OR REPLACE FUNCTION "demorecruitment"."trg_LCL_WriteActivity"() RETURNS trigger
LANGUAGE plpgsql AS $$
#variable_conflict use_column
BEGIN
    INSERT INTO "demorecruitment"."LeadActivities"
        ("LeadId", "ActivityType", "Description", "Details", "CreatedById", "ActorType", "EntityId", "EntityType")
    SELECT
        i."LeadId",
        6,
        'مكالمة — القناة: ' ||
            CASE i."Channel" WHEN 1 THEN 'هاتف' WHEN 2 THEN 'واتساب' ELSE 'غير معروف' END ||
        ' · النتيجة: ' ||
            CASE i."Outcome"
                WHEN 1 THEN 'لا يرد'
                WHEN 2 THEN 'أجاب'
                WHEN 3 THEN 'سيرد لاحقاً'
                WHEN 4 THEN 'غير مهتم'
                WHEN 5 THEN 'مهتم'
                ELSE 'غير معروف'
            END ||
            CASE WHEN i."Note" IS NOT NULL AND i."Note" <> '' THEN ' · ' || i."Note" ELSE '' END,
        '{"channel":' || i."Channel"::text || ',"outcome":' || i."Outcome"::text || '}',
        i."CalledById",
        1,
        i."Id",
        'LeadCallLog'
    FROM inserted i;
    RETURN NULL;
END $$;

CREATE TRIGGER "trg_LCL_WriteActivity" AFTER INSERT ON "demorecruitment"."LeadCallLog"
    REFERENCING NEW TABLE AS inserted FOR EACH STATEMENT EXECUTE FUNCTION "demorecruitment"."trg_LCL_WriteActivity"();

CREATE OR REPLACE FUNCTION "demorecruitment"."trg_LV_WriteActivity"() RETURNS trigger
LANGUAGE plpgsql AS $$
#variable_conflict use_column
BEGIN
    INSERT INTO "demorecruitment"."LeadActivities"
        ("LeadId", "ActivityType", "Description", "Details", "CreatedById", "ActorType", "EntityId", "EntityType")
    SELECT i."LeadId", 2,
        'Office visit — Outcome: ' || CASE i."MeetingOutcome"
            WHEN 1 THEN 'Interested' WHEN 2 THEN 'Not Interested'
            WHEN 3 THEN 'Needs More Time' WHEN 4 THEN 'Converted' WHEN 5 THEN 'No Show' ELSE 'Unknown' END,
        '{"meetingOutcome":' || i."MeetingOutcome"::text || '}',
        i."ReceptionUserId", 1, i."Id", 'LeadVisit'
    FROM inserted i;
    RETURN NULL;
END $$;

CREATE TRIGGER "trg_LV_WriteActivity" AFTER INSERT ON "demorecruitment"."LeadVisits"
    REFERENCING NEW TABLE AS inserted FOR EACH STATEMENT EXECUTE FUNCTION "demorecruitment"."trg_LV_WriteActivity"();


-- Views (column types match SQL Server: COUNT_BIG -> bigint, SUM(int) -> int, SUM(decimal(18,2)) -> decimal(38,2)) --

CREATE VIEW "demorecruitment"."vw_CampaignPerformance" AS
    SELECT "CampaignId",
           count(*)::bigint                                AS "TotalLeads",
           sum("IsConverted"::integer)::integer            AS "ConvertedLeads",
           count(CASE WHEN "Status" = 8 THEN 1 END)::bigint AS "LostLeads"
    FROM "demorecruitment"."Leads"
    WHERE "CampaignId" IS NOT NULL AND "IsDuplicate" = false
    GROUP BY "CampaignId";

CREATE VIEW "demorecruitment"."vw_DailyLeads" AS
    SELECT "BranchId", "RegisteredById", "CreatedAt"::date AS "LeadDate", count(*)::bigint AS "LeadCount"
    FROM "demorecruitment"."Leads"
    WHERE "IsDuplicate" = false
    GROUP BY "BranchId", "RegisteredById", "CreatedAt"::date;

CREATE VIEW "demorecruitment"."vw_DailyPayments" AS
    SELECT "PaymentDate"::date AS "PayDate", count(*)::bigint AS "PaymentCount", sum("AmountEGP")::numeric(38,2) AS "TotalEGP"
    FROM "demorecruitment"."Payments"
    WHERE "TransactionType" = 1
    GROUP BY "PaymentDate"::date;

CREATE VIEW "demorecruitment"."vw_LeadFunnelSummary" AS
    SELECT "BranchId", "LeadSource", "Status", "IsConverted", count(*)::bigint AS "LeadCount"
    FROM "demorecruitment"."Leads"
    WHERE "IsDuplicate" = false
    GROUP BY "BranchId", "LeadSource", "Status", "IsConverted";

CREATE VIEW "demorecruitment"."vw_SalesPerformance" AS
    SELECT "AssignedSalesId",
           make_date(extract(year FROM "CompletedAt")::integer, extract(month FROM "CompletedAt")::integer, 1) AS "MonthStart",
           count(*)::bigint AS "CompletedDeals",
           sum("TotalPaidEGP")::numeric(38,2) AS "RevenueEGP"
    FROM "demorecruitment"."Candidates"
    WHERE "IsCompleted" = true AND "CompletedAt" IS NOT NULL
    GROUP BY "AssignedSalesId", make_date(extract(year FROM "CompletedAt")::integer, extract(month FROM "CompletedAt")::integer, 1);


-- ═════════════════════════════ _template (blank tenant template) ═════════════════════════════

CREATE OR REPLACE FUNCTION "_template"."sp_MoveToNextStage"(
    p_candidate_id uuid, p_moved_by_id uuid,
    p_notes citext DEFAULT NULL, p_is_override boolean DEFAULT false, p_override_reason citext DEFAULT NULL,
    OUT "Success" boolean, OUT "Message" text, OUT "NewStageName" text)
LANGUAGE plpgsql AS $$
#variable_conflict use_column
DECLARE
    v_current_stage_id uuid; v_current_stage_name text; v_current_stage_order integer;
    v_current_min_payment numeric(18,2); v_current_requires_approval boolean; v_current_required_action smallint;
    v_package_id uuid; v_total_paid numeric(18,2);
    v_next_stage_id uuid; v_next_stage_name text; v_next_stage_order integer;
    v_dc integer; v_approved uuid;
BEGIN
    SELECT c."CurrentPackageStageId", ps."StageName", ps."StageOrder", ps."RequiredMinPaymentEGP", ps."RequiresAdminApproval",
           st."RequiredAction", c."JobPackageId", c."TotalPaidEGP"
    INTO v_current_stage_id, v_current_stage_name, v_current_stage_order, v_current_min_payment, v_current_requires_approval,
         v_current_required_action, v_package_id, v_total_paid
    FROM "_template"."Candidates" c
    LEFT JOIN "_template"."PackageStages" ps ON ps."Id" = c."CurrentPackageStageId"
    LEFT JOIN "_template"."StageTypes" st ON st."Id" = ps."StageTypeId"
    WHERE c."Id" = p_candidate_id;

    SELECT "Id", "StageName", "StageOrder" INTO v_next_stage_id, v_next_stage_name, v_next_stage_order
    FROM "_template"."PackageStages"
    WHERE "PackageId" = v_package_id AND "StageOrder" > COALESCE(v_current_stage_order, 0) AND "IsActive" = true
    ORDER BY "StageOrder" ASC
    LIMIT 1;

    IF v_next_stage_id IS NULL THEN "Success" := false; "Message" := 'آخر مرحلة'; RETURN; END IF;

    IF v_current_required_action IN (1, 4) AND p_is_override = false THEN
        SELECT count(*) INTO v_dc FROM "_template"."Documents"
        WHERE "CandidateId" = p_candidate_id AND "DocumentType" = COALESCE(v_current_required_action, "DocumentType");
        IF v_dc = 0 THEN "Success" := false; "Message" := 'DOCUMENT_REQUIRED:' || v_current_stage_name; RETURN; END IF;
    END IF;

    IF v_current_min_payment IS NOT NULL AND v_total_paid < v_current_min_payment AND p_is_override = false THEN
        "Success" := false;
        "Message" := 'PAYMENT_EXCEPTION_REQUIRED:' || upper(v_current_stage_id::text) || ':' || upper(v_next_stage_id::text) || ':'
                     || v_current_min_payment::text || ':' || v_total_paid::text;
        RETURN;
    END IF;

    IF v_current_requires_approval = true AND p_is_override = false THEN
        SELECT "Id" INTO v_approved FROM "_template"."StageApprovalRequests"
        WHERE "CandidateId" = p_candidate_id AND "FromStageId" = v_current_stage_id AND "ToStageId" = v_next_stage_id AND "Status" = 2
        LIMIT 1;
        IF v_approved IS NULL THEN
            "Success" := false;
            "Message" := 'REQUIRES_APPROVAL:' || upper(v_current_stage_id::text) || ':' || upper(v_next_stage_id::text);
            RETURN;
        END IF;
    END IF;

    BEGIN
        UPDATE "_template"."Candidates" SET "CurrentPackageStageId" = v_next_stage_id, "UpdatedAt" = clock_timestamp() AT TIME ZONE 'utc'
        WHERE "Id" = p_candidate_id;

        INSERT INTO "_template"."CandidateStageHistory"
            ("CandidateId", "FromStage", "ToStage", "FromStageId", "ToStageId", "ChangedById", "IsOverride", "OverrideReason", "Notes")
        VALUES (p_candidate_id, v_current_stage_order, v_next_stage_order, v_current_stage_id, v_next_stage_id,
                p_moved_by_id, p_is_override, p_override_reason, p_notes);

        IF NOT EXISTS (SELECT 1 FROM "_template"."StageActionCompletions" WHERE "CandidateId" = p_candidate_id AND "PackageStageId" = v_current_stage_id) THEN
            INSERT INTO "_template"."StageActionCompletions" ("Id", "CandidateId", "PackageStageId", "CompletedAt", "CompletedById", "CompletionType", "Notes")
            VALUES (gen_random_uuid(), p_candidate_id, v_current_stage_id, clock_timestamp() AT TIME ZONE 'utc', p_moved_by_id, 5,
                    'تم اجتياز: ' || v_current_stage_name);
        END IF;

        "Success" := true;
        "Message" := 'تم الانتقال إلى "' || v_next_stage_name || '"';
        "NewStageName" := v_next_stage_name;
    EXCEPTION WHEN OTHERS THEN
        "Success" := false;
        "Message" := SQLERRM;
    END;
END $$;


CREATE OR REPLACE FUNCTION "_template"."sp_ProcessDueReminders"(p_today date DEFAULT NULL)
RETURNS void LANGUAGE plpgsql AS $$
#variable_conflict use_column
BEGIN
    IF p_today IS NULL THEN
        p_today := (clock_timestamp() AT TIME ZONE 'utc')::date;
    END IF;

    INSERT INTO "_template"."Notifications" ("Id", "UserId", "Type", "Title", "Body", "EntityId", "IsRead", "CreatedAt")
    SELECT gen_random_uuid(), fur."AssignedToId", 4,
           'Follow-up due: ' || l."FullName",
           'Your follow-up with ' || l."FullName" || ' (' || l."Phone" || ') is due today.',
           fur."LeadId", false, clock_timestamp() AT TIME ZONE 'utc'
    FROM "_template"."FollowUpReminders" fur
    INNER JOIN "_template"."Leads" l ON l."Id" = fur."LeadId"
    WHERE fur."ReminderDate" <= p_today
      AND fur."Status" = 1
      AND (fur."SnoozedUntil" IS NULL OR fur."SnoozedUntil" <= p_today);

    UPDATE "_template"."FollowUpReminders"
    SET "Status" = 2, "UpdatedAt" = clock_timestamp() AT TIME ZONE 'utc'
    WHERE "ReminderDate" <= p_today
      AND "Status" = 1
      AND ("SnoozedUntil" IS NULL OR "SnoozedUntil" <= p_today);
END $$;


CREATE OR REPLACE FUNCTION "_template"."sp_UpsertLead"(
    p_branch_id uuid, p_registered_by_id uuid, p_full_name citext, p_phone citext, p_lead_source smallint,
    p_campaign_id uuid DEFAULT NULL, p_assigned_sales_id uuid DEFAULT NULL, p_notes citext DEFAULT NULL,
    p_interested_job_title citext DEFAULT NULL, p_interested_country citext DEFAULT NULL,
    p_referred_by_name citext DEFAULT NULL, p_referred_by_phone citext DEFAULT NULL,
    p_facebook_lead_id citext DEFAULT NULL, p_facebook_form_id citext DEFAULT NULL,
    p_utm_source citext DEFAULT NULL, p_utm_medium citext DEFAULT NULL,
    p_utm_campaign citext DEFAULT NULL, p_utm_content citext DEFAULT NULL,
    OUT "LeadId" uuid, OUT "WasDuplicate" boolean)
LANGUAGE plpgsql AS $$
#variable_conflict use_column
DECLARE
    v_existing_id uuid;
    v_lead_id     uuid;
BEGIN
    SELECT "Id" INTO v_existing_id FROM "_template"."Leads" WHERE "Phone" = p_phone LIMIT 1;

    IF v_existing_id IS NOT NULL THEN
        "WasDuplicate" := true;
        v_lead_id := v_existing_id;

        UPDATE "_template"."Leads"
        SET "FullName"           = COALESCE(p_full_name, "FullName"),
            "CampaignId"         = COALESCE(p_campaign_id, "CampaignId"),
            "Notes"              = CASE WHEN p_notes IS NOT NULL
                                        THEN COALESCE("Notes", '') || chr(10) || '[Re-entry ' || to_char(clock_timestamp() AT TIME ZONE 'utc', 'YYYY-MM-DD HH24:MI:SS') || ']: ' || p_notes
                                        ELSE "Notes" END,
            "InterestedJobTitle" = COALESCE(p_interested_job_title, "InterestedJobTitle"),
            "InterestedCountry"  = COALESCE(p_interested_country, "InterestedCountry"),
            "FacebookLeadId"     = COALESCE(p_facebook_lead_id, "FacebookLeadId"),
            "FacebookFormId"     = COALESCE(p_facebook_form_id, "FacebookFormId"),
            "UtmSource"          = COALESCE(p_utm_source, "UtmSource"),
            "UtmMedium"          = COALESCE(p_utm_medium, "UtmMedium"),
            "UtmCampaign"        = COALESCE(p_utm_campaign, "UtmCampaign"),
            "UtmContent"         = COALESCE(p_utm_content, "UtmContent"),
            "UpdatedAt"          = clock_timestamp() AT TIME ZONE 'utc'
        WHERE "Id" = v_existing_id;

        INSERT INTO "_template"."LeadFunnelHistory" ("Id", "LeadId", "FromStatus", "ToStatus", "ChangedById", "Note", "CreatedAt")
        SELECT gen_random_uuid(), "Id", "Status", "Status", p_registered_by_id,
               'Duplicate entry from source ' || p_lead_source::text,
               clock_timestamp() AT TIME ZONE 'utc'
        FROM "_template"."Leads" WHERE "Id" = v_existing_id;
    ELSE
        "WasDuplicate" := false;
        v_lead_id := gen_random_uuid();

        INSERT INTO "_template"."Leads"
            ("Id", "BranchId", "AssignedSalesId", "CampaignId", "RegisteredById", "FullName", "Phone",
             "LeadSource", "Status", "Notes", "InterestedJobTitle", "InterestedCountry",
             "ReferredByName", "ReferredByPhone", "FacebookLeadId", "FacebookFormId",
             "UtmSource", "UtmMedium", "UtmCampaign", "UtmContent", "IsConverted", "IsDuplicate", "CreatedAt")
        VALUES
            (v_lead_id, p_branch_id, p_assigned_sales_id, p_campaign_id, p_registered_by_id, p_full_name, p_phone,
             p_lead_source, 1, p_notes, p_interested_job_title, p_interested_country,
             p_referred_by_name, p_referred_by_phone, p_facebook_lead_id, p_facebook_form_id,
             p_utm_source, p_utm_medium, p_utm_campaign, p_utm_content, false, false, clock_timestamp() AT TIME ZONE 'utc');

        INSERT INTO "_template"."LeadFunnelHistory" ("Id", "LeadId", "FromStatus", "ToStatus", "ChangedById", "Note", "CreatedAt")
        VALUES (gen_random_uuid(), v_lead_id, NULL, 1, p_registered_by_id,
                'Lead created — Source: ' || p_lead_source::text ||
                CASE WHEN p_referred_by_name IS NOT NULL THEN ' — Referred by: ' || p_referred_by_name ELSE '' END,
                clock_timestamp() AT TIME ZONE 'utc');
    END IF;

    "LeadId" := v_lead_id;
END $$;


-- Note: like the SQL Server original, this calls "_template"."sp_ConvertLeadToCandidate", which does not exist in
-- the source database either; the call fails at run time in both systems. Ported as-is.
CREATE OR REPLACE FUNCTION "_template"."sp_WalkInCheckin"(
    p_phone citext, p_full_name citext DEFAULT NULL, p_lead_source smallint DEFAULT NULL,
    p_referred_by_name citext DEFAULT NULL, p_referred_by_phone citext DEFAULT NULL,
    p_branch_id uuid DEFAULT NULL, p_reception_user_id uuid DEFAULT NULL,
    p_assigned_sales_user_id uuid DEFAULT NULL, p_job_package_id uuid DEFAULT NULL,
    p_visit_notes citext DEFAULT NULL,
    OUT "VisitId" uuid, OUT "LeadId" uuid, OUT "CandidateId" uuid, OUT "WasExistingLead" boolean)
LANGUAGE plpgsql AS $$
#variable_conflict use_column
DECLARE
    v_old_lead_status smallint;
    v_lead_id         uuid;
    v_visit_id        uuid;
    v_candidate_id    uuid;
    v_was_existing    boolean;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM "_template"."JobPackages" WHERE "Id" = p_job_package_id AND "IsActive" = true) THEN
        RAISE EXCEPTION 'Job package not found or inactive.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM "_template"."Users" WHERE "Id" = p_assigned_sales_user_id AND "IsActive" = true AND "Role" = 3) THEN
        RAISE EXCEPTION 'Assigned sales user not found or inactive.';
    END IF;

    SELECT "Id", "Status" INTO v_lead_id, v_old_lead_status
    FROM "_template"."Leads"
    WHERE "Phone" = p_phone AND "IsDuplicate" = false
    LIMIT 1;

    IF v_lead_id IS NOT NULL THEN
        v_was_existing := true;
        IF EXISTS (SELECT 1 FROM "_template"."Leads" WHERE "Id" = v_lead_id AND "IsConverted" = true) THEN
            RAISE EXCEPTION 'Lead already converted to a candidate.';
        END IF;
    ELSE
        v_was_existing := false;
        IF p_full_name IS NULL THEN RAISE EXCEPTION 'FullName required for new lead.'; END IF;
        IF p_lead_source IS NULL THEN RAISE EXCEPTION 'LeadSource required for new lead.'; END IF;
        IF p_lead_source = 6 AND p_referred_by_name IS NULL THEN RAISE EXCEPTION 'ReferredByName required for Referral source.'; END IF;
        v_old_lead_status := 1;
    END IF;

    IF v_was_existing = false THEN
        v_lead_id := gen_random_uuid();

        INSERT INTO "_template"."Leads"
            ("Id", "BranchId", "AssignedSalesId", "RegisteredById", "FullName", "Phone",
             "LeadSource", "Status", "ReferredByName", "ReferredByPhone", "IsConverted", "IsDuplicate", "CreatedAt")
        VALUES
            (v_lead_id, p_branch_id, p_assigned_sales_user_id, p_reception_user_id,
             p_full_name, p_phone, p_lead_source, 1, p_referred_by_name, p_referred_by_phone,
             false, false, clock_timestamp() AT TIME ZONE 'utc');

        INSERT INTO "_template"."LeadFunnelHistory" ("Id", "LeadId", "FromStatus", "ToStatus", "ChangedById", "Note", "CreatedAt")
        VALUES (gen_random_uuid(), v_lead_id, NULL, 1, p_reception_user_id,
                'Walk-in new lead — Source: ' || p_lead_source::text ||
                CASE WHEN p_referred_by_name IS NOT NULL THEN ' — Referred by: ' || p_referred_by_name ELSE '' END,
                clock_timestamp() AT TIME ZONE 'utc');
    ELSE
        UPDATE "_template"."Leads"
        SET "AssignedSalesId" = p_assigned_sales_user_id
        WHERE "Id" = v_lead_id AND "AssignedSalesId" IS NULL;
    END IF;

    v_visit_id := gen_random_uuid();

    INSERT INTO "_template"."LeadVisits"
        ("Id", "LeadId", "BranchId", "ReceptionUserId", "AssignedSalesUserId", "VisitDateTime", "MeetingOutcome", "JobPackageId", "Notes", "CreatedAt")
    VALUES
        (v_visit_id, v_lead_id, p_branch_id, p_reception_user_id, p_assigned_sales_user_id,
         clock_timestamp() AT TIME ZONE 'utc', 4, p_job_package_id, p_visit_notes, clock_timestamp() AT TIME ZONE 'utc');

    UPDATE "_template"."Leads"
    SET "Status" = 6,
        "LastContactedAt" = clock_timestamp() AT TIME ZONE 'utc',
        "UpdatedAt" = clock_timestamp() AT TIME ZONE 'utc'
    WHERE "Id" = v_lead_id;

    INSERT INTO "_template"."LeadFunnelHistory" ("Id", "LeadId", "FromStatus", "ToStatus", "ChangedById", "Note", "CreatedAt")
    VALUES (gen_random_uuid(), v_lead_id, v_old_lead_status, 6, p_reception_user_id, 'Walk-in check-in at office', clock_timestamp() AT TIME ZONE 'utc');

    EXECUTE 'SELECT "CandidateId" FROM "_template"."sp_ConvertLeadToCandidate"($1, $2, $3)'
        INTO v_candidate_id USING v_lead_id, p_job_package_id, p_reception_user_id;

    UPDATE "_template"."Candidates" SET "IsProfileComplete" = false WHERE "Id" = v_candidate_id;

    UPDATE "_template"."LeadVisits" SET "ConvertedCandidateId" = v_candidate_id WHERE "Id" = v_visit_id;

    INSERT INTO "_template"."LeadActivities"
        ("Id", "LeadId", "ActivityType", "Description", "Details", "CreatedById", "ActorType", "EntityId", "EntityType", "CreatedAt")
    VALUES
        (gen_random_uuid(), v_lead_id, 7, 'Converted to candidate at walk-in',
         '{"candidateId":"' || upper(v_candidate_id::text) || '","visitId":"' || upper(v_visit_id::text) || '","isProfileComplete":false}',
         p_reception_user_id, 1, v_candidate_id, 'Candidate', clock_timestamp() AT TIME ZONE 'utc');

    UPDATE "_template"."FollowUpReminders"
    SET "Status" = 3,
        "DismissedAt" = clock_timestamp() AT TIME ZONE 'utc',
        "DismissedById" = p_reception_user_id,
        "UpdatedAt" = clock_timestamp() AT TIME ZONE 'utc'
    WHERE "LeadId" = v_lead_id AND "Status" IN (1, 4);

    "VisitId" := v_visit_id; "LeadId" := v_lead_id; "CandidateId" := v_candidate_id; "WasExistingLead" := v_was_existing;
END $$;


CREATE OR REPLACE FUNCTION "_template"."trg_LCL_WriteActivity"() RETURNS trigger
LANGUAGE plpgsql AS $$
#variable_conflict use_column
BEGIN
    INSERT INTO "_template"."LeadActivities"
        ("LeadId", "ActivityType", "Description", "Details", "CreatedById", "ActorType", "EntityId", "EntityType")
    SELECT
        i."LeadId",
        6,
        'مكالمة — القناة: ' ||
            CASE i."Channel" WHEN 1 THEN 'هاتف' WHEN 2 THEN 'واتساب' ELSE 'غير معروف' END ||
        ' · النتيجة: ' ||
            CASE i."Outcome"
                WHEN 1 THEN 'لا يرد'
                WHEN 2 THEN 'أجاب'
                WHEN 3 THEN 'سيرد لاحقاً'
                WHEN 4 THEN 'غير مهتم'
                WHEN 5 THEN 'مهتم'
                ELSE 'غير معروف'
            END ||
            CASE WHEN i."Note" IS NOT NULL AND i."Note" <> '' THEN ' · ' || i."Note" ELSE '' END,
        '{"channel":' || i."Channel"::text || ',"outcome":' || i."Outcome"::text || '}',
        i."CalledById",
        1,
        i."Id",
        'LeadCallLog'
    FROM inserted i;
    RETURN NULL;
END $$;

CREATE TRIGGER "trg_LCL_WriteActivity" AFTER INSERT ON "_template"."LeadCallLog"
    REFERENCING NEW TABLE AS inserted FOR EACH STATEMENT EXECUTE FUNCTION "_template"."trg_LCL_WriteActivity"();

CREATE OR REPLACE FUNCTION "_template"."trg_LV_WriteActivity"() RETURNS trigger
LANGUAGE plpgsql AS $$
#variable_conflict use_column
BEGIN
    INSERT INTO "_template"."LeadActivities"
        ("LeadId", "ActivityType", "Description", "Details", "CreatedById", "ActorType", "EntityId", "EntityType")
    SELECT i."LeadId", 2,
        'Office visit — Outcome: ' || CASE i."MeetingOutcome"
            WHEN 1 THEN 'Interested' WHEN 2 THEN 'Not Interested'
            WHEN 3 THEN 'Needs More Time' WHEN 4 THEN 'Converted' WHEN 5 THEN 'No Show' ELSE 'Unknown' END,
        '{"visitDateTime":"' || to_char(i."VisitDateTime", 'YYYY-MM-DD HH24:MI:SS') || '"' ||
        ',"meetingOutcome":' || i."MeetingOutcome"::text ||
        ',"branchId":"' || upper(i."BranchId"::text) || '"' ||
        ',"salesUserId":' || COALESCE('"' || upper(i."AssignedSalesUserId"::text) || '"', 'null') || '}',
        i."ReceptionUserId", 1, i."Id", 'LeadVisit'
    FROM inserted i;
    RETURN NULL;
END $$;

CREATE TRIGGER "trg_LV_WriteActivity" AFTER INSERT ON "_template"."LeadVisits"
    REFERENCING NEW TABLE AS inserted FOR EACH STATEMENT EXECUTE FUNCTION "_template"."trg_LV_WriteActivity"();


CREATE VIEW "_template"."vw_CampaignPerformance" AS
    SELECT "CampaignId",
           count(*)::bigint                                AS "TotalLeads",
           sum("IsConverted"::integer)::integer            AS "ConvertedLeads",
           count(CASE WHEN "Status" = 8 THEN 1 END)::bigint AS "LostLeads"
    FROM "_template"."Leads"
    WHERE "CampaignId" IS NOT NULL AND "IsDuplicate" = false
    GROUP BY "CampaignId";

CREATE VIEW "_template"."vw_DailyLeads" AS
    SELECT "BranchId", "RegisteredById", "CreatedAt"::date AS "LeadDate", count(*)::bigint AS "LeadCount"
    FROM "_template"."Leads"
    WHERE "IsDuplicate" = false
    GROUP BY "BranchId", "RegisteredById", "CreatedAt"::date;

CREATE VIEW "_template"."vw_DailyPayments" AS
    SELECT "PaymentDate"::date AS "PayDate", count(*)::bigint AS "PaymentCount", sum("AmountEGP")::numeric(38,2) AS "TotalEGP"
    FROM "_template"."Payments"
    WHERE "TransactionType" = 1
    GROUP BY "PaymentDate"::date;

CREATE VIEW "_template"."vw_LeadFunnelSummary" AS
    SELECT "BranchId", "LeadSource", "Status", "IsConverted", count(*)::bigint AS "LeadCount"
    FROM "_template"."Leads"
    WHERE "IsDuplicate" = false
    GROUP BY "BranchId", "LeadSource", "Status", "IsConverted";

CREATE VIEW "_template"."vw_SalesPerformance" AS
    SELECT "AssignedSalesId",
           make_date(extract(year FROM "CompletedAt")::integer, extract(month FROM "CompletedAt")::integer, 1) AS "MonthStart",
           count(*)::bigint AS "CompletedDeals",
           sum("TotalPaidEGP")::numeric(38,2) AS "RevenueEGP"
    FROM "_template"."Candidates"
    WHERE "IsCompleted" = true AND "CompletedAt" IS NOT NULL
    GROUP BY "AssignedSalesId", make_date(extract(year FROM "CompletedAt")::integer, extract(month FROM "CompletedAt")::integer, 1);


-- ═════════════════════════════ platform ═════════════════════════════

-- Creates a tenant schema with the same columns as the template tables (SELECT TOP 0 * INTO copies columns,
-- nullability and IDENTITY, but no defaults/keys/constraints — LIKE ... INCLUDING IDENTITY does the same).
CREATE OR REPLACE FUNCTION "platform"."sp_ProvisionTenant"(
    p_tenant_id uuid, p_company_name citext, p_schema_name citext, p_subdomain citext,
    p_super_admin_id uuid, p_subscription_end date, p_subscription_status smallint DEFAULT 1)
RETURNS void LANGUAGE plpgsql AS $$
#variable_conflict use_column
DECLARE
    v_table text;
BEGIN
    -- SQL Server: NOT LIKE '[a-z0-9_]%' OR LIKE '%[^a-z0-9_]%' under a case-insensitive collation.
    IF p_schema_name IS NULL OR p_schema_name::text !~ '^[A-Za-z0-9_]+$' THEN
        RAISE EXCEPTION 'Schema name invalid.';
    END IF;

    IF EXISTS (SELECT 1 FROM pg_namespace WHERE lower(nspname) = lower(p_schema_name::text)) THEN
        RAISE EXCEPTION 'Schema already exists.';
    END IF;

    INSERT INTO "platform"."Tenants"
        ("Id", "CompanyName", "SchemaName", "Subdomain", "CreatedBySuperAdminId", "SubscriptionEndDate", "SubscriptionStatus", "StorageUsedBytes")
    VALUES (p_tenant_id, p_company_name, p_schema_name, p_subdomain, p_super_admin_id, p_subscription_end, p_subscription_status, 0);

    EXECUTE format('CREATE SCHEMA %I', p_schema_name::text);

    FOREACH v_table IN ARRAY ARRAY['Users','Branches','Candidates','JobPackages','PackageStages','StageTypes',
                                   'Commissions','CommissionTiers','Leads','Payments','Documents','Notifications'] LOOP
        EXECUTE format('CREATE TABLE %I.%I (LIKE "_template".%I INCLUDING IDENTITY)', p_schema_name::text, v_table, v_table);
    END LOOP;

    RAISE NOTICE 'Tenant provisioned: [%]', p_schema_name;
END $$;
