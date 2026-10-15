# Isolated Linux staging validation — 14 October 2026

The current self-contained Linux x64 build ran successfully in a private staging directory on the OVH server. This was an isolated smoke test, not a live CRM deployment or a qualified AI pilot.

## Isolation

The application and a separate restored PostgreSQL instance ran together inside a new Linux network namespace. Inspection showed only the loopback interface and no external route. PostgreSQL listened on `127.0.0.1:55441`, database `validation_smoke`; the application listened on `127.0.0.1:5191` inside that namespace. No nginx/public route, systemd service or production environment file was changed.

An opt-in `StagingValidation:Enabled` application mode now rejects use outside the Staging environment, requires that dedicated loopback port/database-name prefix, and requires all AI/external-processing gates disabled. It skips the import, rotation, reminder, durable-work, intake-timeout, voice and follow-up workers. Factory-created outbound HTTP is also blocked. Network isolation is the additional boundary for code that might create a client outside that factory. Normal production defaults and worker registrations are unchanged when this mode is off.

The staging package used minimal configuration and separate data-protection keys, not production credentials/configuration. Uploaded customer documents were omitted from the transfer; document/media previews therefore were not validated. The private full database backup stayed on the owner's server and was never sent to a model.

## Results

- **160 regression tests passed**, including 12 new validation-mode checks.
- The rebuilt application started and returned a successful health response on Linux. The packaged prompt is `egyptian-sales-draft-v7-reviewed-routing`.
- The captured legacy database restored and all seven AI migrations applied on **PostgreSQL 16.15**, ICU locale `und`, collation version `153.121`, matching the inspected source environment. The restored history reached `20261013114423_AiStructuredCustomerMemory`.
- **24 HTTP smoke checks passed**: anonymous admin redirect, login CSRF enforcement, actual login for four synthetic roles, admin/inbox/reservations/telesales/team-manager/reception page responses, both AI admin page responses, denial of AI admin access to non-admin roles, and the operations endpoint reporting no pending migrations and no live-pilot/automatic-dispatch qualification.
- The initial smoke run used an incorrect `/AiChatLearningAdmin` test URL; the actual registered route is `/AiChatLearning`. The corrected harness passed; no application-route change was needed.
- Three fixture runs created 12 synthetic users and three synthetic branches only in the copy. No real user's credentials or permissions were changed. Original row counts and original-column digests still matched the captured backup across all **83 original data tables**, excluding those fixtures and intentional new migration-history rows.
- The isolated application and PostgreSQL instance stopped cleanly after validation. The live CRM and PostgreSQL services remained active when checked; production still had the original pre-AI migration baseline and zero new AI tables.

These are HTTP and database checks, not browser-layout validation, load qualification or complete booking/intake/reception write-workflow acceptance. Disabled workers were deliberately not exercised on copied customer records. Real-model semantic results remain those in the [reviewed routing report](../validation/turkey-reviewed-routing.md); this staging test made no model calls or customer sends.

## Artifacts and next step

Private local artifacts: `%LOCALAPPDATA%\RecruitmentSaaS\ReleaseCandidates\2026-10-14-isolated-staging`, including `staging-manifest.json`, `smoke-results.json`, `data-preservation-result.json` and the remote staging path pointer. The transfer checksum matched: `c756fb01135431d11873cd26e4b163b53dc1aef44f9aa7d5915c17c2607d502e`. Private server artifacts remain under `.local/share/recruitmentsaas-staging`; no staging process remains active.

This package's configuration intentionally enables validation isolation and must not be copied as production configuration. Production role/ACL behavior, service-environment recovery, real tenant/account/branch/reviewer bindings, current branch schedules, privacy/reply approval, final human workflow acceptance and a supervised shadow-pilot plan remain required. Next prepare those concrete pilot settings and review criteria; do not turn on autonomous sending or present this smoke test as final AI release approval.
