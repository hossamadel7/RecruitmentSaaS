# Staging release candidate

Prepared on 13 October 2026. The code is built and locally verified; it has not been installed on the OVH VPS. The reply review is still pending, and fine-tuning is optional later.

Update on 14 October: the original candidate is superseded by the current routing/isolation build. [Isolated Linux staging validation](ai-linux-staging-validation.md) passed with 160 regression tests and 24 HTTP checks on a restored PostgreSQL 16 copy. The production CRM was not upgraded. The newer staging package has validation-only configuration and is not a production configuration bundle.

The local release candidate is under `%LOCALAPPDATA%\RecruitmentSaaS\ReleaseCandidates\2026-10-13-ai-memory-improvements`. It contains the Linux x64 self-contained app and the seven-migration SQL upgrade from the confirmed pre-AI baseline. This build includes its .NET 8 runtime, matching the deployment style observed on the server. A successful publish is not a Linux runtime or staging smoke test.

## Remaining release steps

1. The [14 October logical backup/restore rehearsal](ai-backup-restore-rehearsal.md) now establishes a private snapshot, matching download checksum, successful local restoration and migration data preservation. Repeat staging qualification on the production-equivalent Linux PostgreSQL/ICU environment. Preserve `/etc/recruitmentsaas/app.env`, the active systemd service configuration and the prior application release separately; these were not part of the database-only recovery rehearsal.
2. Verify staging matches `20261011231817_AllowLeadStatusInTurkey` and the expected legacy schema. Rehearse the SQL there, inspect constraints/foreign keys and smoke-test existing human intake, rotation, inbox, reservations and reception paths. Do not use the empty historical baseline to provision a fresh legacy database or the destructive repository migration scripts.
3. Configure a stable deployment tenant, approved account scopes and dedicated pseudonym secret through the deployment's existing configuration mechanism. The selected chat corpus excludes `testttt`. Keep external processing and customer dispatch disabled until the pilot scope and privacy review are approved. The local API credential is not included in the release candidate.
4. Import only owner/CEO/sales-lead-reviewed facts and Playbook entries, bound to actual accounts, validity dates and an accountable reviewer. Keep customer memory separate. The review sheet is not an approval or import file by itself.
5. Run an isolated real-model evaluation with the final knowledge and current prompt. Earlier provider-only trials exposed semantic errors; citation validity alone does not qualify a live pilot. Test staff takeover, opt-out, unclear voice, unknown fees and booking confirmation in the full CRM path.
6. After rollout approval, start with a small manually assigned shadow pilot. Review drafts before any approved send, monitor outcomes and unknown dispatch attempts, and qualify expansion with actual verified visits and human quality review. Automatic rotation and fine-tuning remain separate later decisions.

## Deployment safeguards

Publish to a new release directory and preserve the existing effective configuration. Do not copy a development connection string or enable flags simply by copying packaged appsettings. Keep the current service environment file private. Migration history guards do not prove schema compatibility.

The customer-memory change is additive. A code rollback can retain the new column so reviewed details are not lost; dropping it is a data-destructive rollback and needs its own reviewed recovery plan. Do not run Down migrations against a populated installation as a routine rollback.

No live schema, service, credentials or account permissions were changed during preparation.
