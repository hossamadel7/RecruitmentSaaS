# Phase 5 local validation — 2026-10-12

Branch: `codex/ai-sales-foundation`. The work adds local structural replay tooling, an admin-only aggregate operations endpoint/panel and release/pilot documentation. No production data was changed, no historical samples were uploaded, no real OpenAI/Meta call or customer message was sent, and no deployment, commit or PR was performed. Actual model/audio quality and live release remain unqualified.

## Results

- `dotnet test Tests/RecruitmentSaaS.Tests.csproj --no-restore`: **66 passed, 0 failed, 0 skipped**, duration 29 seconds. Nine new tests extend the previous 57.
- ReplayRunner: **48/48** sequential structural runs passed (16 scenarios × three repetitions): 15 local-policy runs and 33 deterministic fake-provider runs. **8/8** additional concurrent independent-tenant runs passed. Zero external calls; semantic assessment `not_run`.
- Generated artifact: [phase5-synthetic-replay.json](phase5-synthetic-replay.json), including fixture hash/version, prompt reference, case checks, simulated usage and local timings. Local timings include database seeding/checks and are not production SLO evidence.
- Main application and ReplayRunner builds succeeded. The same five pre-existing main-project warnings remain in `FacebookLeadWebhookController` and `Views/Admin/FormStats.cshtml`.
- `dotnet ef migrations has-pending-model-changes --no-build`: no model changes since the last migration. No Phase 5 migration is needed.
- `git diff --check`: clean. Git's Windows line-ending notices are informational.
- MSBuild Compile/Content item inspection found no `Tests/` sources/content in the production project, including the new ReplayRunner.

All executions used unique disposable databases on the dedicated loopback PostgreSQL cluster. Fixtures removed their data afterward. No production application configuration or transport credentials were loaded.

## New coverage and corrections

Operational tests verify tenant isolation; exclusion of customer text, raw private error codes and foreign usage; unknown usage/cost; prior ambiguous voice attempts remaining unreconciled despite a later successful result; unknown attendance; old-job flags without retries/mutations; null empty latency and a maximum 1,000 completed-duration samples; admin-only HTTP authorization; and two pending fake provider calls blocking a third same-tenant generation without reservation or provider invocation.

Replay harness tests exercise unknown customer fields, retired facts, takeover/stale results and rejection of unmarked data or playbook-as-fact citation allowances. Replay execution additionally checks all 16 provided scenario structures, citation/source scope, CRM preservation, no outgoing messages and request replay. Semantic prohibitions are retained for human assessment, not reduced to substring scoring.

The first executable run exposed a harness normalization mismatch: absent professions appear as empty readable context, while the CRM field remains null. The harness now compares the normalized representation and separately confirms the original CRM field is unchanged. A subsequent run exposed a retired fixture's nearly equal validity endpoints; its historical interval is now unambiguously valid but expired/retired. The database validity constraint was preserved.

## Browser evidence

The isolated BrowserHost served compiled Razor/static assets at `http://127.0.0.1:5189` with synthetic office/voice fixtures, strict mock providers, no hosted workers and no outbound transport. The new operations panel loaded and manually refreshed successfully. It displayed one transcript requiring review, empty/unknown duration samples, unknown financial cost, and no live qualification. The `EnsureCreated` fixture has no migration history, so migration evidence correctly remained unverified even though tables existed. Quality, privacy and operational acceptance checks remained review-required, with automatic release unavailable.

Screenshots: [operations panel](phase5-operations-browser.png) and [readiness requirements](phase5-readiness-browser.png). These screenshots show synthetic fixture state, not the replay artifact's counts or production activity.

## Next evidence

The [Phase 5 runbook](../ai-sales-phase5-readiness.md) and unapproved [release evidence worksheet](../evaluation/release-readiness-template.json) define the remaining steps. Verified real company information, authorized representative conversation/audio samples, approved external processing, actual model scoring, staging schema/restore/load checks and a human-owned supervised pilot are still required. Customer dispatch and weighted AI rotation require a further reviewed implementation and explicit release approval. Passing these local checks does not establish those capabilities or approvals.
