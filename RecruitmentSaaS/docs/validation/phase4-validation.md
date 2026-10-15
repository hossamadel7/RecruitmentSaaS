# Phase 4 office booking validation — 2026-10-12

Implementation uses the local checkout on `codex/ai-sales-foundation`. Production configuration remains disabled; no production migration, customer message, external provider call, deployment, commit or PR was performed.

## Automated checks

The test suite extends the previous 44 cases with 13 office-booking cases. It uses the dedicated loopback PostgreSQL test cluster, unique disposable databases and fake providers; fixtures delete their databases afterward.

Final command: `dotnet test Tests/RecruitmentSaaS.Tests.csproj --no-restore` — **57 passed, 0 failed, 0 skipped**, duration 23 seconds. `dotnet ef migrations has-pending-model-changes --no-build` reported no changes since the last migration; `git diff --check` was clean.

Coverage: atomic/idempotent save and original AI ownership; legacy status/date synchronization and draft staleness; customer-confirmation/disclosure/office-hours/source-scope validation before persistence; reminder replacement on reschedule; fresh confirmation with timestamp-rounding protection; cancellation after takeover with disabled booking feature; rejection of stale control and existing legacy appointments; matching reception branch/date evidence, with absent/mismatched attendance remaining unknown; concurrent duplicate booking; protected inbox outcome shortcuts; HTTP auth/CSRF/visibility/admin policy permissions; actual reception check-in preserving the human workflow and duplicate-visit prevention; report separation of AI from the human reviewer; shared approximate arrival times for open visits; reviewed voice confirmation evidence.

The regression run found and fixed a whole-second timestamp issue: the previous confirmation could otherwise look newer after database rounding. Explicit ID rejection and a one-second freshness margin now prevent reuse. A shared-tenant fixture also needed distinct assignment request keys; production idempotency correctly rejected its reused key.

The existing empty-table migration rehearsal now removes office tables before voice/engine/foundation tables and reapplies all four additive migrations, preserving legacy human ownership and age. Migration `20261012152606_AiOfficeBookings` creates three tables and does not rewrite existing production data. Dropping populated tables is not covered by the empty rollback test.

Build has the same five pre-existing warnings in `FacebookLeadWebhookController` and `Views/Admin/FormStats.cshtml`. Model consistency and whitespace checks accompany the test run. Windows line-ending notices from Git are informational.

## Browser checks

BrowserHost ran on `http://127.0.0.1:5189` with `--office-demo`, synthetic office hours/customer evidence and a synthetic Shadow assignment. No hosted workers or outbound transport were registered.

The compiled Razor admin page rendered policy approval and consultation controls. A synthetic consultation for 2026-10-13 at 12:00 Cairo was saved after selecting the approved branch and incoming customer confirmation and checking both attestations. The page displayed the committed booking and unknown attendance. Rescheduling with the old confirmation returned a validation error. Cancellation with a reason then displayed Cancelled plus retained confirmation/cancellation history. Evidence: [phase4-booking-browser.png](phase4-booking-browser.png).

The reservations SQL projection and reception action are exercised against PostgreSQL by automated tests. The browser demonstration does not establish actual attendance or customer consent; all its records are synthetic.

## Remaining release evidence

Real office hours, branch/contact details, verified recruitment facts, privacy approvals and representative sample conversations/audio have not been supplied. Phase 5 should evaluate replay quality, supervised manual assignments, actual reminder operations, legacy administrative edit conflicts, staging migration compatibility, load/monitoring and recovery before controlled rotation or automatic customer dispatch. No sales-quality, acoustic-accuracy, attendance-rate or production-performance claim follows from these tests.
