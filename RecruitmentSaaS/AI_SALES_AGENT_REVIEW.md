# AI Sales Agent — Independent Technical Review

Review date: 4 November 2026 · Scope: uncommitted Codex changes on `master` (on top of `8f09697`) · Reviewer: Claude (Opus 5.5)

## Executive summary

The Codex work is **careful, conservative and mostly correct in what it does**, but it does **not** deliver the full workflow described in the brief. It builds a safe *human-in-the-loop* AskAdmin pipeline, not an autonomous one:

- The live gap → AskAdmin → Admin answer → resumption chain exists in code and is persisted, idempotent and restart-safe.
- But resumption ends at a **draft that a human must review and dispatch**. The AI never sends the Admin-grounded answer to the customer by itself.
- Admin answers are **not classified automatically**. The Admin picks customer-only / general / temporary / correction on the dashboard. This is safer than LLM classification, but it is manual.
- **AI-initiated training interviews** (the AI proactively asking the Admin about knowledge gaps) are **not implemented**.
- Gap detection is narrow: AskAdmin fires only when a draft ends in a `handoff` with `NeedsHuman`. It does not detect contradictions or low-confidence answers.

I found and fixed **4 defects** (1 critical correlation bug, 3 reliability/scalability bugs) and added 4 regression tests. After the fixes, all **292 tests pass** against the isolated local PostgreSQL test instance.

**Verdict: READY FOR CONTROLLED TESTING** (feature flags on, a synthetic or pilot Admin number, and a few explicitly assigned conversations). It is not ready for limited production until the open Major items below are addressed. The main ones are the 20-drafts/day default, the 2-concurrent-generation cap, no human notification on escalation, and the customer follow-up message cancelling the clarification.

## Existing vs new functionality

| Area | Before Codex (committed) | Added by Codex (uncommitted) |
|---|---|---|
| AI agent as CRM user, assignment, takeover, release, ownership versions | Existing (`AiSalesControlService`, `ConversationControls`, `AiLeadAssignments`) | Conversation-only transfer from inbox, null `LeadId` support |
| OpenAI draft generation (Responses API, strict JSON schema) | Existing (`OpenAiSalesDraftProvider`) | Language matching, ADMIN_CUSTOMER_ONLY fact handling, playbook instructions |
| Automatic replies / dispatch | Existing (`AiAutomaticReplyService`, `AiSalesDispatchService`) | Skip turns that have an AskAdmin request |
| Historical chat learning | Existing `AiChatLearningService` (pseudonymised) | New `AiSalesLearningService`, `AiLearningProvider`, `AiSemanticKnowledgeService` (embeddings), evaluation, cost estimator |
| Approved knowledge store | Existing `AiSalesKnowledge` (approval, version, expiry) | Reused for Admin-published facts (no new store, good) |
| Admin WhatsApp channel, AskAdmin, resumer | — | All new: `AiAdminLearningService`, `AiAdminLearningWorker`, `AiAdminDraftResumer`, 3 tables, dashboard |

Codex reused the knowledge store, ownership/locking primitives and transport rather than duplicating them. The one overlap is that two historical-learning services now exist (`AiChatLearningService` and `AiSalesLearningService`). They write different tables, so this is a maintainability concern (Minor m1), not a correctness one.

## Implementation status

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 1 | OpenAI integration | Implemented, verified by mocked tests | `OpenAiSalesDraftProvider.cs:111-174` |
| 2 | AI as a CRM sales employee | Implemented and verified | `AiSalesControlService.AssignAsync`, takeover/release tests |
| 3 | Private AI↔Admin channel over WhatsApp | Implemented and verified (mocked transport) | `AiAdminLearningService.BindAsync/ReceiveAsync/DeliverAsync` |
| 4 | AI-initiated questions to Admin | **Partially implemented**: only live-gap questions; no proactive training interview | `AskAsync` called only from `AiSalesDraftService.cs:126-131` |
| 5 | Learning from Admin responses | Partially implemented: stored for **manual** review and classification | `ValidateAnswerAsync` |
| 6 | Persistent approved knowledge | Implemented and verified | Publishes to `AiSalesKnowledge` with approver, source and expiry |
| 7 | Live knowledge-gap detection | **Partially implemented**: handoff-draft trigger only, no contradiction detection | `AiSalesDraftService.cs:126` |
| 8 | Automatic AskAdmin when info missing | Implemented (narrow trigger) and verified | test `Missing_facts_enqueue_one_private_request...` |
| 9 | Asynchronous waiting | Implemented and verified (durable rows, 15 s worker, no in-memory state) | `AiAdminLearningWorker` |
| 10 | Automatic resumption of the right conversation | **Partially implemented**: produces a review draft only; no automatic send | `AiAdminDraftResumer.PrepareAsync` |
| 11 | Continuous improvement from Admin feedback | Partially implemented: corrections retire a chosen fact; no automatic conflict detection | `ValidateAnswerAsync` `replaces` |
| 12 | Integration with historical learning | Implemented: shared `AiSalesKnowledge` + index | `AiSemanticKnowledgeService` |
| — | Initial Admin training interview / follow-up questions | **Missing** | Doc admits: "later phases" |
| — | Admin pause/resume | Implemented and verified | `ReceiveAsync:103-104`, controller `pause` |

## Live workflow trace (customer → AI → Admin → AI → customer)

1. **Webhook** `WhatsAppWebhookController` verifies `X-Hub-Signature-256` (HMAC-SHA256, rejects when the secret is missing). → `WhatsAppWebhookProcessor.ProcessInboundMessageAsync` first calls `AiAdminLearningService.ReceiveAsync`. A message from a reserved Admin number is diverted **before** contact, conversation or lead creation (also for echoes and history: `:499`, `:582`).
2. Customer message is stored. `AiAutomaticReplyWorker` (4 s loop) → `RunConversationAsync` → `AiSalesDraftService.GenerateAutomaticallyAsync`, run under advisory locks, with a daily budget and an idempotency key `auto:{chat}:{msg}:{version}`.
3. Local policy or OpenAI returns a `handoff` + `NeedsHuman` draft → `AskAsync` (`AiSalesDraftService.cs:126`). This creates an `AiAdminClarification` linked to the conversation, the exact customer message, the agent and the ownership version. It gets a unique `AI-xxxxxxxxxxxx` code and a deadline. A unique index on (tenant, conversation, customer message) makes it idempotent.
4. Automatic reply sees the `NeedsHuman` draft or the clarification and **does not reply** (`AiAutomaticReplyService.cs:33-34,40`). The customer receives nothing while waiting (no holding acknowledgement).
5. `AiAdminLearningWorker` → `QueueQuestionsAsync` queues one question per cycle when the Admin's 24 h window is open. Otherwise it queues one parameterless template per day. `DeliverAsync` sends through the existing transport with a durable Queued → Sending → Sent/Unknown state.
6. Admin reply arrives via webhook → `ReceiveAsync` (under a per-channel advisory lock). It deduplicates by `wamid` (plus a unique index) and correlates by **quoted reply context**, then by **code**. A conflicting quote and code results in no match. A plain message matches only if exactly one question is open (**fixed: and that question was actually delivered**, see C1). The answer is stored redacted as `AnswerReceived`, scope `Incomplete`.
7. The Admin validates on the dashboard (`ValidateAnswerAsync`). This requires the verified channel owner, a version check, a chosen scope and an expiry of 90 days or less. Customer-only answers **cannot** be published. Publish/correct runs in one transaction under a knowledge lock.
8. `AiAdminDraftResumer.PrepareAsync` reloads the request, the conversation, the control and the newest message. It **cancels** if the agent, controller, ownership version or newest message changed, or the answer expired. Otherwise it generates a draft. `AiSalesContextService.ReadAsync:65-77` injects the answer only for that conversation **and** that exact customer message (no cross-customer leakage).
9. The draft is `ReviewRequired` with `RequestedById` = the validator, so it is **never auto-sent**. A human reviews it, then the existing dispatch re-checks freshness and the customer service window before sending.

Steps 1-9 are verified in code and, apart from real transport and model calls, by tests. **Step 9 is where the brief's automatic resumption stops.**

## Critical findings

### C1 — Natural Admin message could be applied to a question the Admin never received — **FIXED**
- **File:** `Services/AiAdminLearningService.cs`, `ReceiveAsync` (implicit-match branch, around line 121)
- **Expected:** A plain reply (no quote, no code) is accepted only for a question the Admin has seen.
- **Actual:** It matched any single request in `Queued` *or* `AwaitingAdmin`. A `Queued` request has not been sent yet (for example when the 24 h window was closed, or outbound was disabled). If the Admin then wrote anything ("good morning", or an answer about a different customer), that text became the stored answer to a question they never saw, linked to a specific customer. The answer still needs dashboard validation, but the dashboard presents it as the Admin's answer to that question, which invites a wrong approval.
- **Fix:** Implicit match now requires exactly one open request, in `AwaitingAdmin`, whose `question:` message is `Sent`/`Delivered`/`Read`. A question with `Unknown` transport outcome is not treated as seen. Quoted replies and codes are unchanged.
- **Test:** `Natural_admin_reply_never_answers_a_question_the_admin_was_not_sent`.
- **Severity:** Critical (wrong customer correlation).

## Major findings

### M1 — Admin worker head-of-line blocking and retry storms — **FIXED**
- **File:** `Services/AiAdminLearningWorker.cs`, `ExecuteAsync` and `AiAdminDraftResumer.PrepareAsync`
- **Actual (two problems):**
  - (a) Any exception in `QueueQuestionsAsync` or `DeliverAsync` aborted the whole cycle. For example, `WhatsAppAccounts.SingleAsync(... IsActive)` throws once the sender account is deactivated. Messages are ordered oldest-first and capped at 10, so the same poison message ran first on every cycle. The worker then never reached resumption or deadline escalation again: **all** AskAdmin processing stopped.
  - (b) If `GenerateAsync` rejected a validated request permanently (drafts disabled, conversation outside the draft pilot, agent paused), the request stayed `Validated`. It was retried every 15 s forever. Once 10 such requests existed, every later request starved.
- **Fix:** Each channel, message and request now runs in its own DI scope with its own exception handling and logging (error type only, no content). A permanent `AiSalesValidation` from draft generation now escalates the request with `ErrorCode = "DraftUnavailable"`. Transient `AiSalesConflict` (budget, concurrency) is still retried.
- **Test:** `Resumer_escalates_permanently_refused_drafts_instead_of_retrying_forever`. The worker isolation was verified by code review only, because the hosted loop has no test harness.

### M2 — Auto-reply worker starvation with many concurrent leads — **FIXED**
- **File:** `Services/AiAutomaticReplyService.cs`, `AiAutomaticReplyWorker`
- **Actual:** Each 4 s cycle took the 50 **oldest** assignments that had an unanswered customer message. Turns already handled without a reply (handoff drafts, AskAdmin waits, failed/stale drafts) stay unanswered for up to 24 h. They occupied those 50 slots on every cycle. With more than 50 such conversations, **new customers were never processed**. This existed before, but AskAdmin makes it much more likely.
- **Fix:** Selection moved to `AiAutomaticReplyWorker.CandidatesAsync`. It computes the exact idempotency key `RunConversationAsync` would use and skips turns that already have an automatic draft. A new customer message or a new ownership version produces a new key, so the conversation becomes eligible again. Behaviour for each conversation is unchanged, because the skipped calls returned without action anyway.
- **Test:** `Automatic_reply_candidates_skip_already_handled_turns_but_return_on_a_new_customer_message`.

### M3 — A received answer was lost when the deadline passed before validation — **FIXED**
- **File:** `AiAdminDraftResumer.PrepareAsync` escalates `AnswerReceived` past the deadline. `ValidateAnswerAsync` then refused `Escalated`.
- **Actual:** If the Admin answered on WhatsApp but validated on the dashboard after the deadline (24 h by default), the answer could no longer be used.
- **Fix:** `ValidateAnswerAsync` also accepts `Escalated` when `ErrorCode == "AdminDeadlineExceeded"`, and clears the error code. Escalations for other reasons stay closed. The resumer still re-checks customer freshness.
- **Test:** `Answer_received_before_deadline_can_still_be_validated_after_deadline_escalation`.

### M4 — No automatic customer resumption (requirement gap) — OPEN
- `AiAdminDraftResumer` always produces a `ReviewRequired` draft attributed to the validator. A human must review and dispatch it. This is a deliberate safety choice (documented in `docs/admin-whatsapp-learning.md`), but it does not meet requirement 10.
- **Recommendation:** For `General`/`Temporary`/`Correction` answers that were **published**, allow the existing automatic path. It already re-checks freshness, the 24 h window and the outbound-intent state. Clear the clarification block in `AiAutomaticReplyService.cs:33` once the request is `Validated`/`ReviewReady`, and let `ApproveAutomaticallyAsync` accept it. Keep `CustomerOnly` behind human review. This needs product sign-off, so it is not applied.

### M5 — A customer follow-up while waiting cancels the clarification — OPEN
- **File:** `AiAdminDraftResumer.PrepareAsync:65-71` requires `newest == r.CustomerMessageId`.
- If the customer sends "?" or "any news?" while the Admin is answering, the request is **Cancelled** and the Admin's answer is never used for that customer. A new handoff/AskAdmin may then repeat the same question to the Admin. Customers commonly do this.
- **Recommendation:** Allow resumption when every newer message is an incoming customer message and the controller/version is unchanged. Generate against the latest turn with the scoped answer attached (the context filter needs the same relaxation).

### M6 — Throughput caps far below "thousands of messages per day" — OPEN (configuration)
- `AiSalesEngineOptions.MaxDraftsPerDay` defaults to **20**, and `AiSalesDraftService.cs:62` allows at most **2 concurrent generations** tenant-wide. The Admin channel sends at most **one question per 15 s**, and `MaxRequestsPerDay` is 30.
- **Recommendation:** Size `MaxDraftsPerDay` to the expected volume (with a cost alarm). Raise the concurrency cap to around 8-16, or make it configurable.

### M7 — Escalations notify nobody — OPEN
- Deadline or `DraftUnavailable` escalations only change a row state. The customer gets no reply and no salesperson is alerted.
- **Recommendation:** Notify the team leader / previous owner through the existing `INotificationService`. Optionally release the conversation to a human automatically.

### M8 — Definitive send failures are recorded as `Unknown` and never retried — OPEN
- **File:** `DeliverAsync:233-239` maps every failure (including a 4xx `Success=false`) to `Unknown`. The question then sits `AwaitingAdmin` until the deadline although the Admin never received it.
- **Recommendation:** Use `Failed` when the provider returned a definitive error (`Success=false` with an error code), and allow re-queueing. Keep `Unknown` only for timeouts and exceptions.

## Minor findings

- **m1** — Two historical-learning pipelines (`AiChatLearningService`, `AiSalesLearningService`) with separate tables. Consolidate later.
- **m2** — General knowledge must expire within 90 days (`ValidateAnswerAsync:185`). Permanent company rules need periodic re-approval. Consider a longer maximum for `General`.
- **m3** — Publishing has no duplicate or conflict check against existing approved facts. Correction relies on the Admin choosing `replaces`, and the new fact does not link to the one it superseded.
- **m4** — `GET /api/ai-admin-learning/status` returns private Admin message text to **every** role-1 user, not only the channel owner.
- **m5** — A non-template question queued while the window was open, but delivered after it closed, waits until the deadline instead of triggering a template (`DeliverAsync:229`, `QueueQuestionsAsync:252`).
- **m6** — `AskAsync` re-reads the *latest* incoming message after the draft lock is released. A message arriving in between is linked instead of the drafted one. The resumer's freshness check contains this.
- **m7** — Historical analysis uses the same `AiSales:Model` as drafting. There is no separate cheaper extraction model setting.
- **m8** — The `Cancel` endpoint can cancel requests in any state, including `ReviewReady`. Harmless, but unexpected.

## Security review

| Check | Result |
|---|---|
| Webhook signature | HMAC-SHA256 on `X-Hub-Signature-256`; rejects when the secret is unset (`WhatsAppWebhookController.cs:105-115`) |
| Admin phone identity | Bound by an authenticated role-1 admin, proven by a single-use 15-min hashed code sent *from* the phone; max 5 attempts; constant-time compare; refuses numbers with existing customer threads |
| Spoofed "I am Admin" | Treated as a customer (test) |
| Role-based access | Admin API is `[Authorize(Roles="1")]` plus antiforgery and no-store; validation requires the verified channel owner |
| AI permissions | The agent is not a `User` with a role; it acts only via server-validated drafts and dispatch |
| Tenant/account isolation | All queries filter by `TenantId` and account; delivery receipts scoped by account (test) |
| Prompt injection (customer/history) | Model output is constrained by a strict JSON schema; `sourceIds` limited to an enum of approved fact IDs; server re-validates intent, evidence and sensitive output (`ValidateProposal`); the model has no tools |
| Unauthorized knowledge updates | Only an explicit dashboard action by the channel owner publishes; customer-only answers can never be published |
| Cross-customer leakage | Scoped answer injected only for the same conversation and the same customer message |
| Secrets/PII in logs | Workers log the exception type only; provider errors persisted as bounded codes |
| API key exposure | Server-side only; `/api/ai-sales/agents` returns booleans |
| SQL injection | EF / interpolated `FromSqlInterpolated` (parameterised) only |
| Replay/duplicates | `wamid` unique index and pre-check under lock; idempotency keys on drafts and dispatch |
| Excessive API calls | Daily draft cap, daily learning-call cap, at most 2 concurrent generations, no provider retries |
| Residual risk | Redaction is regex-based (emails and 8+ digit numbers), so names and addresses in free text can reach OpenAI. Mitigated by `store=false`; requires a DPA/privacy sign-off |

No security vulnerabilities requiring a fix were found.

## OpenAI integration

- Uses the **Responses API** (`POST /v1/responses`) with `text.format = json_schema, strict: true` and `store: false`. These are current, documented features. Raw `HttpClient`, no SDK.
- Timeout 45 s (drafts) / 90 s (learning). No retries, by design. Bounded response read (128-256 KB). Checks `status == "completed"`, handles `refusal`, requires exactly one `output_text`.
- Token usage (`input_tokens`/`output_tokens`) and the resolved model are persisted per draft. Learning and embedding calls go through a budgeted `AiLearningUsage` reservation.
- **Model name could not be verified.** `AiSales:Model` is empty in every committed `appsettings*.json`; it is provided by deployment configuration, which I did not read. Make sure it is a model that supports `json_schema` structured outputs on the Responses API. Note that OpenAI's pages now mark `gpt-5-mini` and `gpt-4.1-mini` as deprecated. `max_output_tokens = 800` may be too low for reasoning models, which spend output tokens on reasoning; that surfaces as `ProviderIncomplete`.
- Embeddings: `text-embedding-3-small` with `dimensions=512` (supported).

## Performance and cost

**API calls per customer message:** 0 (local policy) or 1 draft call. Plus 1 query embedding when semantic retrieval is on (cached 5 min). AskAdmin and Admin parsing make **no** model calls. Resumption makes one more draft call.

**Estimate.** Assumptions: 3,000 inbound customer messages/day; 60% reach OpenAI (1,800 calls); about 6,000 input tokens per call (system instructions around 3,000 tokens, plus 20 messages and up to 8 facts); about 750 output tokens including reasoning. Prices are from OpenAI's pricing pages on 4 Nov 2026, per 1M tokens:

| Model | Input $/1M | Output $/1M | Per day | Per month (30 d) |
|---|---|---|---|---|
| gpt-5-mini (deprecated label) | 0.25 | 2.00 | ≈ $5.4 | ≈ $160 |
| gpt-5.4-mini | 0.75 | 4.50 | ≈ $14.2 | ≈ $425 |
| text-embedding-3-small (query embeddings) | 0.02 | — | < $0.01 | < $0.30 |

The static instructions come first in the request, so automatic prompt caching should cut the input cost further. Historical learning is a one-off cost of about 20 messages per call, capped by `MaxCallsPerDay`. Using a cheaper model for historical extraction (m7) is the main saving available. Verify prices again before budgeting.

**Database:** Required indexes exist: clarification (tenant, state, deadline), code, `wamid`, draft (tenant, request key). The auto-reply candidate query now reads at most 1,000 assignments and does one indexed key lookup.

## Test results

| Command | Result |
|---|---|
| `dotnet test Tests/RecruitmentSaaS.Tests.csproj` (no DB variable) | Build OK; 201 DB tests fail fast by design (the fixture requires the local test DB) |
| `$env:AI_SALES_TEST_DATABASE='Host=127.0.0.1;Port=55439;Username=phase1_test;Database=postgres'; dotnet test` — before fixes | **288 / 288 passed** |
| Targeted admin and regression tests after fixes (`--filter ...Admin...`) | **23 / 23 passed** |
| Full suite after fixes | **292 / 292 passed** (288 existing + 4 new) |
| `dotnet ef migrations has-pending-model-changes` | No pending model changes (fixes add no schema change) |

All external services were mocked: strict `IWhatsAppCloudApiService` and `IAiSalesDraftProvider` mocks. **No real WhatsApp messages and no paid API calls were made.** Not verified: live Meta template approval, real webhook delivery ordering, the real OpenAI model's behaviour, and the hosted worker loops under load.

New tests (`Tests/ReviewRegressionTests.cs`):
- `Natural_admin_reply_never_answers_a_question_the_admin_was_not_sent`
- `Answer_received_before_deadline_can_still_be_validated_after_deadline_escalation`
- `Resumer_escalates_permanently_refused_drafts_instead_of_retrying_forever`
- `Automatic_reply_candidates_skip_already_handled_turns_but_return_on_a_new_customer_message`

## Concurrency scenarios

| Scenario | Outcome |
|---|---|
| Two customers, different questions | Separate requests and codes; the Admin must quote or use the code (two open, so no implicit match) — tested |
| Many customers, same question | Separate requests; no cross-customer grouping (documented gap) |
| Out-of-order answers | Correlated by quote or code — tested |
| Unreferenced Admin message | Matched only when exactly one *delivered* question is open (after C1 fix); otherwise a "please reference" reply is sent |
| Duplicate webhook | `wamid` pre-check under lock plus unique index — tested |
| Rapid Admin messages | Serialised by the per-channel advisory lock; a later message on the same request overwrites the pending answer (it is still reviewed) |
| Restart while pending | All state is in the DB; `Sending` becomes `Unknown` (no double send) — tested |
| Customer writes again while waiting | Request cancelled at resume (M5, open) |
| Human takeover during Admin reply | Resumer cancels (ownership version) — tested |
| Conversation closed or reassigned | Resumer cancels (agent/controller/version check) |

## Recommendations (priority order)

1. Decide on M4 (automatic send for published general answers) and implement it with tests.
2. M5: tolerate customer-only follow-ups while waiting.
3. M6: size the draft budget and concurrency for real volume; add a cost alarm.
4. M7: notify humans on escalation.
5. M8: distinguish definitive send failures and allow re-queueing.
6. Configure and verify a non-deprecated structured-output model; raise `MaxOutputTokens` for reasoning models.
7. Add a proactive training interview generated from unanswered and escalated questions (requirement 4).
8. m3: duplicate/conflict detection and supersession links on publish.
9. Privacy: stronger PII redaction before OpenAI, and a retention job for `AiAdminMessages`.

## Production readiness

**READY FOR CONTROLLED TESTING.**

What is in place: core safety (signature, Admin identity proof, isolation, no auto-publication, no cross-customer leakage, idempotency, restart safety), and the correlation and starvation defects found here are fixed. What is still missing before limited production: automatic resumption and proactive training (both required by the brief), notifications on escalation, and capacity settings sized for the stated volume (M4–M8). The two migrations (`20261104133753_AiSalesContinuousLearning`, `20261104145700_AiAdminWhatsAppCoaching`) have **not** been applied to production; this review did not apply them either.

## Changes made in this review

| File | Change |
|---|---|
| `Services/AiAdminLearningService.cs` | C1 correlation fix; M3 late validation; clear `ErrorCode` on validation |
| `Services/AiAdminLearningWorker.cs` | M1 per-item isolation in its own scope; escalate permanently refused drafts |
| `Services/AiAutomaticReplyService.cs` | M2 `CandidatesAsync` skips already-handled turns |
| `Tests/ReviewRegressionTests.cs` | 4 new regression tests |

No migrations, schema changes, secrets, historical chats or human-sales code paths were modified. Nothing was committed or deployed.

Sources: [OpenAI pricing](https://developers.openai.com/api/docs/pricing), [gpt-5-mini](https://developers.openai.com/api/docs/models/gpt-5-mini), [gpt-5.4-mini](https://developers.openai.com/api/docs/models/gpt-5.4-mini), [text-embedding-3-small](https://developers.openai.com/api/docs/models/text-embedding-3-small).
