# AI Sales — Phase 1 foundation

Implemented locally on `codex/ai-sales-foundation`. This phase provides manually assigned service identities, ownership controls, an admin screen, durable inbound processing, and human-workflow protection. It does not implement an LLM, knowledge retrieval, transcription, autonomous sales messages, appointment booking, or AI participation in rotation.

## Behavior

- Open `/AiSalesAdmin` as an administrator (role 1). Create a Disabled or Shadow agent bound to one WhatsApp account and its existing team. Account/team bindings cannot be changed afterward.
- Enable Shadow and unpause an agent before manually assigning an eligible linked lead/chat. The server checks account, team, active-assignment cap, current ownership version, and existing assignments. Leads with multiple conversations require human review and are excluded from this pilot.
- AI identity is separate from `Users`; it has no login, password, salary, commission, or employee metrics. Database checks prohibit simultaneous human/AI ownership on a lead or chat. Human distribution queries exclude AI-owned leads.
- Assignment preserves existing name, age, profession, intake state and prior owners. Already-running scripted intake can continue while enabled. On completion it keeps AI ownership and moves to Assisted (shadow). Assisted is a control state; this phase generates no sales replies or recommendations.
- Staff inbox sends, staff mobile-app echoes, and recognized customer requests for a person/declining assistance stop automation. Takeover increments the ownership version, invalidates blocked future intents, and stores a deterministic summary. It retains AI assignment for admin review. Release explicitly returns the lead/chat to eligible active former owners in the same team, or to the existing unassigned workflow.
- Pausing/disabling an agent pauses its non-human conversations atomically. Unpausing does not resume conversations; each requires an explicit resume action. Resume selects Assisted and does not restart data collection.
- Admins can assign, resume and release. Roles 7/8 may inspect and take over only conversations visible in their existing team scope. Existing inbox staff sends retain their normal visibility checks. Sensitive realtime message events resolve current recipients rather than trusting stale group membership.

## Configuration and database scope

Defaults in `appsettings.json`:

```json
"AiSales": {
  "Enabled": false,
  "TenantId": "00000000-0000-0000-0000-000000000000"
}
```

For an approved staging pilot, configure `AiSales__Enabled=true` and `AiSales__TenantId` with a stable nonempty deployment tenant GUID through deployment configuration. Never change that GUID to switch tenants on an existing database. Tenant IDs on new records are assigned by the server, never accepted from clients.

The existing CRM has one operational `demorecruitment` schema and no runtime tenant resolver. This implementation treats that database/deployment as one tenant boundary. It is **not** shared-database tenant isolation for the legacy CRM. Agent account/team checks apply inside that deployment.

Feature disabling blocks new AI assignments, resumes, and AI-owned scripted replies. It does not turn off existing human CRM workflows or the durable webhook workers. Set `WhatsApp:AppSecret` correctly before deployment: webhook POST requests now fail closed when the secret/signature is missing or invalid.

## HTTP contracts

All mutations use cookie authentication, `RequestVerificationToken` antiforgery header, and `Idempotency-Key` (1–128 characters). Store the key and reuse it when retrying the same action. Reusing a key with different request data returns 409. Agent and conversation changes require the current `expectedVersion`; stale requests return 409. Business validation returns 422. The UI handles these headers automatically.

| Endpoint | Access / purpose |
| --- | --- |
| `GET /api/ai-sales/setup` | Admin: active accounts and configured team |
| `GET /api/ai-sales/agents` | Admin: identities, capability flags, dead-letter count |
| `POST /api/ai-sales/agents` | Admin: create identity |
| `PATCH /api/ai-sales/agents/{id}` | Admin: settings/version, pause controls |
| `GET /api/ai-sales/candidates?search=...` | Admin: up to 50 linked chats |
| `POST /api/ai-sales/leads/{id}/assign` | Admin: `agentId`, `conversationId`, `expectedVersion`, `reason` |
| `GET /api/ai-sales/conversations/{id}` | Scoped control, summary and latest 30 audit events |
| `POST /api/ai-sales/conversations/{id}/takeover` | Admin/scoped managers: `expectedVersion`, `reason` |
| `POST /api/ai-sales/conversations/{id}/resume` | Admin: `expectedVersion`, `reason` |
| `POST /api/ai-sales/conversations/{id}/release` | Admin: `expectedVersion`, `reason` |

## Durability and concurrency

Valid webhooks are persisted before HTTP 200. Persistence failure returns 503. Payload hashes deduplicate identical deliveries; existing provider message-ID checks deduplicate messages across payloads. Message persistence and intake job insertion share a transaction. Successful jobs erase the raw payload but retain the deduplication tombstone.

A PostgreSQL session advisory lock permits one durable consumer across app processes. It reads batches of 20, retries failures with bounded backoff, and leaves the row for review after eight failures. Process/connection death releases the lock; unfinished rows remain eligible. This deliberately modest throughput is appropriate for the foundation, not a load-tested queue platform.

`DurableWorkItems` rows with `CompletedAt IS NULL AND Attempts >= 8` need operator review. The admin page shows their count. Inspect `Id`, `Kind`, `ErrorCode`, and attempts first; failed raw payloads may contain customer data. After fixing a failure, an authorized operator can reset the selected row's attempts and available time to retry. Do not bulk delete queued rows or retry uncertain provider sends blindly. Automated dead-letter recovery and retention scheduling are not implemented. Define restricted access and a retention policy before live operation.

Conversation writers coordinate on one PostgreSQL advisory key. Human rotation selection, cursor advancement, and lead persistence commit together; failed transactions do not consume a rotation turn. Agent assignment locks enforce caps across processes. EF concurrency tokens and database constraints protect ownership from stale legacy writes.

The durable queue provides at-least-once **processing**, not exactly-once WhatsApp delivery. Existing scripted/human transport paths can have an ambiguous outcome if a process fails during a provider call. `AiOutboundIntents` is blocked foundation storage only; no AI dispatcher is registered. Native mobile activity takes effect once its webhook reaches the server; a message already accepted by the provider cannot be recalled.

## Migration and rollout

Migration: `20261012124131_AiSalesFoundation`. It adds nullable AI owner columns and seven new tables, indexes, constraints and foreign keys. It does not reassign existing human leads or rewrite their data.

The historical EF baseline does not create the full legacy schema. Do not bootstrap an empty installation from this migration chain alone. Use the existing documented schema provisioning process, then verify the historical schema and migration history match the preceding snapshot. Do not use the destructive `migration/migrate.sh` against an existing installation.

The design-time context uses a deliberately unreachable local connection and does not load application secrets or start workers. Migration generation and pending-model checks are offline. Applying a migration requires an explicitly supplied target connection; none was applied to an existing installation during implementation.

Before a deployment, rehearse against a sanitized copy of the actual installation and review the generated SQL, backup/restore procedure, signature configuration and existing data constraints. Apply the schema before starting this binary, even when AI Sales is disabled: durable webhook processing needs its tables. Keep the feature disabled until the manually selected pilot has been reviewed.

The integration test rehearses migration Down/Up on an isolated synthetic database and confirms human data survives. This is not verification of a production database's historical drift.

## Rollback

Prefer feature disablement or a forward fix. Pause agents, explicitly release active AI assignments, reconcile any pending/dead-letter work, and retain/export needed audit records before considering schema removal. `Down` removes foundation history and owner columns; it is not a safe live-data recovery procedure. The rehearsal covers only empty foundation tables with retained human data.

Passwords are now hashed on creation/reset and upgraded after successful legacy plaintext authentication. **The old binary's plaintext comparison cannot authenticate upgraded users.** A binary rollback must retain compatible password verification (or use a forward fix). Do not restore plaintext passwords as a rollback strategy.

## Verification

Run against a dedicated local PostgreSQL instance, never deployment settings:

```powershell
$env:AI_SALES_TEST_DATABASE = 'Host=127.0.0.1;Port=55439;Username=phase1_test;Database=postgres'
dotnet test Tests/RecruitmentSaaS.Tests.csproj
dotnet ef migrations has-pending-model-changes --no-build
git diff --check
```

The fixture rejects other host/port/user combinations, creates uniquely named `phase1_*` databases and deletes only those fixture databases. Use a dedicated local test role with database-creation permission. HTTP tests start an isolated TestServer with synthetic authentication and do not run production `Program`, load production secrets, start application workers, or send network messages.

Twenty passing tests cover ownership/idempotency, concurrent assignment, tenant/account/team scope, human takeover and queued-reply ordering, paused controls, rotation weighting and rollback, durable receipt/restart/retry, signatures, password upgrades, HTTP authorization/antiforgery/version conflicts, rejected autonomous mode, and migration rehearsal. Build succeeds with five pre-existing warnings in Facebook lead handling and the admin form statistics view. An isolated local browser check passed; see [validation results](validation/phase1-validation.md). No live WhatsApp delivery or deployed staging smoke test was performed.
