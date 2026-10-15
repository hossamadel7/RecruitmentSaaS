# Backup and restore rehearsal — 14 October 2026

The production CRM database was backed up using PostgreSQL 16.15 `pg_dump` custom format. The backup is a consistent logical database snapshot, created through existing SSH access. No production schema, service, account permissions or effective application configuration was changed. A private backup artifact was added on the server and downloaded to the owner's laptop; this is not a complete server or point-in-time recovery backup.

## Evidence

- Source database size reported approximately 63 MB. Compressed archive: 8,143,254 bytes with 617 table-of-contents entries.
- Server and downloaded SHA-256 matched: `1a183dcc37d05d978903e3f440e36418acde4c0f2e155df30d135763732eb805`.
- The original migration baseline was `20261011231817_AllowLeadStatusInTurkey`. CRM tables were confirmed under `demorecruitment`; migration history is under `public`.
- The archive restored successfully with `--exit-on-error --single-transaction --no-owner --no-acl` into a dedicated local PostgreSQL 17 instance, `127.0.0.1:55440`, database `recruitment_restore`, separate from application/test instances.
- Restored row counts matched the archive COPY data across all **83 data tables**. The verification tool streams the backup locally and prints aggregate results only.
- All **seven AI migrations** applied successfully to that restored legacy database. The latest migration is `20261013114423_AiStructuredCustomerMemory`, with 14 AI tables and no unvalidated PostgreSQL constraints. Reviewed memory details use JSONB with an empty-object default.
- Across all 83 original tables, original row counts and digests of original column values matched before/after the upgrade. Existing migration-history rows were compared separately from the seven intentional new history rows. Digests detect accidental data changes; they are not a cryptographic authenticity guarantee.
- Reapplying the conditional upgrade script succeeded. The production CRM/PostgreSQL services were active when checked before and after. No claim of continuous uptime measurement is made.
- The isolated restored database was stopped cleanly after verification. The source backup and restored files were retained privately for the next rehearsal.

## Private artifacts

Artifacts are outside Git under `%LOCALAPPDATA%\RecruitmentSaaS\BackupRehearsals\2026-10-14-60bbfb7de24f495d9e1aba90f46e9167`. The pointer `%LOCALAPPDATA%\RecruitmentSaaS\ChatLearningReview\latest-backup-rehearsal-path.txt` identifies this directory. It contains the archive, restore/migration logs, aggregate preservation reports and a manifest. The server copy is under the owner's private `.local/share/recruitmentsaas-backup-rehearsals` directory with restrictive creation permissions. The backup contains actual private CRM data and is not an AI training dataset; it was not uploaded to a model.

## Remaining limits

This proves logical restore and migration compatibility for the captured snapshot, not a complete deployment or disaster-recovery exercise. Database role ownership/ACLs were deliberately not reproduced, and application environment secrets, systemd settings and prior binaries remain separate recovery items. The source is Linux PostgreSQL 16 with ICU `und` version `153.121`; the rehearsal is Windows PostgreSQL 17 with ICU `und` version `153.14`. Production-equivalent Linux staging, collation-sensitive behavior and permissions still need verification.

Next: rebuild the current routing revision and validate the application on isolated staging, with outbound integrations/workers kept disconnected and original configuration preserved. Human CRM workflow checks, approved tenant/account/branch bindings, actual branch hours and final reply review are still required. The live agent remains disabled and production has not received any AI migration.
