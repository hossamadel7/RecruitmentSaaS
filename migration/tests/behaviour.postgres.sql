-- Behaviour parity scenario (PostgreSQL side) — mirrors behaviour.sqlserver.sql line for line. Rolled back at the end.
BEGIN;
CREATE TEMP TABLE out (n serial, line text) ON COMMIT DROP;

DO $$
#variable_conflict use_column
DECLARE
    v_lead   uuid := 'F4D4870D-125A-41BF-870D-E41141B89086'; v_lead2 uuid := 'F3D019AC-406E-4C7A-ACB1-32141EC54CEB';
    v_pkg    uuid := '00000000-0000-0000-0000-000000000040'; v_admin uuid := '00000000-0000-0000-0000-000000000030';
    v_sales6 uuid := '977D1803-80B1-4711-B09D-18205B5048E2'; v_sales3 uuid := '80AD25C4-5BF2-4A71-92D9-01036D0A18D2';
    v_branch uuid; v_cand uuid; v_cand_text text; r record; i int; v_ov boolean;
    v_lid uuid; v_dup boolean; v_phone citext; w record;
BEGIN
    SELECT "BranchId" INTO v_branch FROM "demorecruitment"."Leads" WHERE "Id" = v_lead;

    UPDATE "demorecruitment"."Leads" SET "AssignedOfficeSalesId" = v_sales6 WHERE "Id" = v_lead;
    SELECT "CandidateId" INTO v_cand FROM "demorecruitment"."sp_ConvertLeadToCandidate"(v_lead, v_pkg, v_admin);
    v_cand_text := upper(v_cand::text);

    FOR i IN 0..8 LOOP
        v_ov := i >= 2;
        SELECT * INTO r FROM "demorecruitment"."sp_MoveToNextStage"(v_cand, v_admin, 'note', v_ov, NULL);
        INSERT INTO out(line) VALUES ('move|' || (v_ov::int)::text || '|' || (r."Success"::int)::text || '|' || COALESCE(replace(r."Message", v_cand_text, 'CAND'), '<null>') || '|' || COALESCE(r."NewStageName", '<null>'));
    END LOOP;

    INSERT INTO out(line) SELECT 'cand|' || ps."StageName" || '|' || c."Status"::text || '|' || c."TotalPaidEGP"::text || '|' || (c."IsProfileComplete"::int)::text || '|' || upper(c."AssignedSalesId"::text)
        FROM "demorecruitment"."Candidates" c JOIN "demorecruitment"."PackageStages" ps ON ps."Id" = c."CurrentPackageStageId" WHERE c."Id" = v_cand;
    INSERT INTO out(line) SELECT 'hist|' || COALESCE("FromStage"::text, '<null>') || '>' || "ToStage"::text || '|' || ("IsOverride"::int)::text || '|' || COALESCE("Notes", '<null>')
        FROM "demorecruitment"."CandidateStageHistory" WHERE "CandidateId" = v_cand ORDER BY "ToStage";
    INSERT INTO out(line) SELECT 'act|' || "ActivityType"::text || '|' || "Description" || '|' || COALESCE("Details", '<null>')
        FROM "demorecruitment"."CandidateActivities" WHERE "CandidateId" = v_cand ORDER BY "Details";
    INSERT INTO out(line) SELECT 'sac|' || "CompletionType"::text || '|' || COALESCE("Notes", '<null>') FROM "demorecruitment"."StageActionCompletions" WHERE "CandidateId" = v_cand ORDER BY "CompletedAt";
    INSERT INTO out(line) SELECT 'comm|' || "AmountEGP"::text || '|' || "DealsThisMonth"::text || '|' || "Status"::text || '|' || to_char("CommissionMonth", 'YYYY-MM-DD') || '|' || upper("SalesUserId"::text)
        FROM "demorecruitment"."Commissions" WHERE "CandidateId" = v_cand;
    INSERT INTO out(line) SELECT 'lead|' || "Status"::text || '|' || ("IsConverted"::int)::text || '|' || CASE WHEN "ConvertedCandidateId" = v_cand THEN 'linked' ELSE 'NOT LINKED' END FROM "demorecruitment"."Leads" WHERE "Id" = v_lead;
    INSERT INTO out(line) SELECT 'funnel|' || COALESCE("FromStatus"::text, '<null>') || '>' || "ToStatus"::text || '|' || replace("Note", v_cand_text, 'CAND') FROM "demorecruitment"."LeadFunnelHistory" WHERE "LeadId" = v_lead ORDER BY "CreatedAt";

    -- triggers
    INSERT INTO "demorecruitment"."LeadCallLog" ("LeadId", "CalledById", "Channel", "Outcome", "Note") VALUES (v_lead2, v_admin, 2, 5, 'ملاحظة'), (v_lead2, v_admin, 1, 1, '');
    INSERT INTO "demorecruitment"."LeadVisits" ("LeadId", "BranchId", "ReceptionUserId", "AssignedSalesUserId", "MeetingOutcome") VALUES (v_lead2, v_branch, v_admin, v_sales3, 3);
    INSERT INTO out(line) SELECT 'trg|' || "ActivityType"::text || '|' || "Description" || '|' || COALESCE("Details", '<null>') || '|' || "EntityType"
        FROM "demorecruitment"."LeadActivities" WHERE "LeadId" = v_lead2 ORDER BY "ActivityType", "Description";

    -- upsert
    SELECT "Phone" INTO v_phone FROM "demorecruitment"."Leads" WHERE "Id" = 'A338DB26-CCB7-4CA4-9A95-4A69F3E39D50';
    SELECT "LeadId", "WasDuplicate" INTO v_lid, v_dup FROM "demorecruitment"."sp_UpsertLead"(v_branch, v_admin, 'Robert Again', v_phone, 2::smallint, NULL, NULL, 'again');
    INSERT INTO out(line) SELECT 'upsert-dup|' || (v_dup::int)::text || '|' || CASE WHEN v_lid = 'A338DB26-CCB7-4CA4-9A95-4A69F3E39D50' THEN 'same' ELSE 'other' END || '|' || "FullName" || '|' ||
        CASE WHEN "Notes"::text ~ '\[Re-entry 20\d\d-\d\d-\d\d \d\d:\d\d:\d\d\]: again$' THEN 'notes-ok' ELSE 'notes:' || COALESCE("Notes", '<null>') END FROM "demorecruitment"."Leads" WHERE "Id" = v_lid;
    SELECT "LeadId", "WasDuplicate" INTO v_lid, v_dup FROM "demorecruitment"."sp_UpsertLead"(v_branch, v_admin, 'New Person', '0109999TEST', 6::smallint, NULL, NULL, NULL, NULL, NULL, 'Referrer');
    INSERT INTO out(line) SELECT 'upsert-new|' || (v_dup::int)::text || '|' || CASE WHEN "LeadCode" = 'LD-' || right('00000' || "LeadSequence"::text, 5) AND "LeadSequence" > 5 THEN 'code-ok' ELSE 'code-BAD:' || "LeadCode" END || '|' || "Status"::text FROM "demorecruitment"."Leads" WHERE "Id" = v_lid;
    INSERT INTO out(line) SELECT 'upsert-funnel|' || "Note" FROM "demorecruitment"."LeadFunnelHistory" WHERE "LeadId" = v_lid;
    SELECT "LeadId", "WasDuplicate" INTO v_lid, v_dup FROM "demorecruitment"."sp_UpsertLead"(v_branch, v_admin, NULL, '0109999test', 1::smallint);
    INSERT INTO out(line) VALUES ('upsert-ci|' || (v_dup::int)::text);

    -- reminders
    INSERT INTO "demorecruitment"."FollowUpReminders" ("LeadId", "AssignedToId", "CreatedById", "ReminderDate", "Status")
        VALUES (v_lead2, v_sales3, v_admin, (clock_timestamp() AT TIME ZONE 'utc')::date - 1, 1);
    PERFORM "demorecruitment"."sp_ProcessDueReminders"();
    INSERT INTO out(line) SELECT 'notif|' || "Type"::text || '|' || "Title" || '|' || "Body" FROM "demorecruitment"."Notifications" WHERE "UserId" = v_sales3 AND "EntityId" = v_lead2;
    INSERT INTO out(line) SELECT 'reminder|' || "Status"::text FROM "demorecruitment"."FollowUpReminders" WHERE "LeadId" = v_lead2;

    -- walk-in
    SELECT * INTO w FROM "demorecruitment"."sp_WalkInCheckin"('0111222333', 'Walk In', 1::smallint, NULL, NULL, v_branch, v_admin, v_sales3, v_pkg, 'visit');
    INSERT INTO out(line) SELECT 'walkin|' || (w."WasExistingLead"::int)::text || '|' || l."Status"::text || '|' || (l."IsConverted"::int)::text || '|' || (c."IsProfileComplete"::int)::text || '|' || CASE WHEN v."ConvertedCandidateId" = w."CandidateId" THEN 'visit-linked' ELSE 'visit-NOT-linked' END
        FROM "demorecruitment"."Leads" l JOIN "demorecruitment"."Candidates" c ON c."Id" = w."CandidateId" JOIN "demorecruitment"."LeadVisits" v ON v."Id" = w."VisitId" WHERE l."Id" = w."LeadId";
    INSERT INTO out(line) SELECT 'walkin-act|' || "ActivityType"::text || '|' || "Description" || '|' || replace(replace(COALESCE("Details", ''), upper(w."CandidateId"::text), 'CAND'), upper(w."VisitId"::text), 'VISIT')
        FROM "demorecruitment"."LeadActivities" WHERE "LeadId" = w."LeadId" ORDER BY "ActivityType";
    INSERT INTO out(line) SELECT 'walkin-funnel|' || COALESCE("FromStatus"::text, '<null>') || '>' || "ToStatus"::text || '|' || replace("Note", upper(w."CandidateId"::text), 'CAND') FROM "demorecruitment"."LeadFunnelHistory" WHERE "LeadId" = w."LeadId" ORDER BY "ToStatus";

    -- views
    INSERT INTO out(line) SELECT 'vw_funnel|' || "LeadSource"::text || '|' || "Status"::text || '|' || ("IsConverted"::int)::text || '|' || "LeadCount"::text FROM "demorecruitment"."vw_LeadFunnelSummary" ORDER BY "LeadSource", "Status", "IsConverted";
    -- expected-error cases last (same order as the SQL Server script)
    BEGIN
        PERFORM * FROM "demorecruitment"."sp_ConvertLeadToCandidate"(v_lead, v_pkg, v_admin);
    EXCEPTION WHEN OTHERS THEN INSERT INTO out(line) VALUES ('convert-again-err|' || SQLERRM);
    END;
    BEGIN
        PERFORM * FROM "demorecruitment"."sp_WalkInCheckin"('0111222333', 'Walk In', 6::smallint, NULL, NULL, v_branch, v_admin, v_sales3, v_pkg, NULL);
    EXCEPTION WHEN OTHERS THEN INSERT INTO out(line) VALUES ('walkin-err|' || SQLERRM);
    END;
END $$;

SELECT line FROM out ORDER BY n;
ROLLBACK;
