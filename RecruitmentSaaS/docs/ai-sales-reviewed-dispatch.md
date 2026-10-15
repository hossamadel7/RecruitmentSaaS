# Reviewed text dispatch

This increment adds explicit administrator dispatch of a reviewed sales reply. It does not enable sending in this checkout, add an autonomous worker, change human rotation, or qualify a live pilot. The existing AI identity remains Shadow. Draft review alone still does not send a message.

## Release boundary

`AiSalesDispatchOptions` defaults to `Enabled=false`, `ReleaseApproved=false`, no approved accounts and a maximum of ten transport attempts per deployment tenant per UTC day. Both flags, a nonempty server-side account allowlist and enabled foundation/drafting are required. Startup validates limits and enabled configuration. These switches express the operator's release decision; the software does not verify that real company facts, privacy approvals or model evaluation are complete. Complete the [Phase 5 acceptance work](ai-sales-phase5-readiness.md) before an operator changes them. No credentials, approval flags or real account configuration were added to appsettings.

No feature automatically queues or sends a reply. The API requires an authenticated Role 1 administrator and the normal antiforgery token for writes. Team staff can continue reviewing drafts through the existing engine, but dispatch is reserved for administrators. There is no dispatch button in this increment.

## API workflow

1. Review a current draft through the existing engine API. A draft must have an actual reviewer/time, a supported answer/qualification/consultation intent and no `NeedsHuman` flag. Handoff/stop drafts stay with staff.
2. `POST /api/ai-sales-dispatch` with `Idempotency-Key` and `{ "draftId": "<guid>", "expectedOwnershipVersion": 1 }` creates a `Queued` intent after release, account, ownership, context and reply-window checks. It creates no outgoing inbox message. A key cannot be reused for another actor/draft/version. A draft can have only one dispatch record, including after cancellation or failure.
3. Inspect `GET /api/ai-sales-dispatch/conversations/<guid>` and explicitly `POST /api/ai-sales-dispatch/<intent-guid>/send`. Sending rechecks the draft's current context, knowledge, ownership, resumed control, agent/account availability and reply window. Customer free text requires a latest incoming timestamp within 24 hours, with a five-minute closing margin. Future timestamps beyond one second of legacy database rounding are rejected. Closed windows require the existing staff-approved template workflow; this dispatcher never sends templates or media.
4. `POST /api/ai-sales-dispatch/<intent-guid>/cancel` cancels an unattempted queue record even while release is disabled. Takeover and agent pause also invalidate queued intents. Cancellation cannot undo a started or uncertain transport attempt.
5. `GET /api/ai-sales-dispatch/status` reports configuration without credentials. This is configuration evidence, not release qualification.

## Attempt and uncertainty rules

The dispatcher holds the same conversation advisory lock as staff sends/takeover. Immediately before transport, a transaction reserves the daily budget, writes `Sending`, creates a queued text message and records the attempt audit. Transport runs outside the database transaction with a 20-second cancellation deadline. A takeover started during that call waits for its conversation lock; an accepted customer message cannot be recalled. Model generation still releases its conversation lock while waiting for its provider.

A success with a bounded nonempty Meta message ID records `Sent` and the inbox message ID. It means Meta accepted the message, not that the customer received/read it; the existing status webhook continues updating the message. Message authorship uses the nullable `SenderAiSalesAgentId`, with no human `SenderUserId`. A database constraint prevents dual authorship. The reviewer and dispatching administrator are recorded separately in draft/intent/audit records. Historical inbox retrieval and realtime events label the sender as the virtual assistant.

All other outcomes, including provider errors, timeouts, missing IDs, thrown exceptions and a recovered `Sending` record, become `Unknown`. No raw transport body is stored in the new intent or message error. A repeated send request never retries an attempted intent. The existing transport may try another credential only after an explicit permission refusal; the dispatcher has no general transport retry policy.

Unknown attempts consume the daily budget and block further AI queueing for that conversation. Keep the conversation with a human and inspect Meta/WhatsApp records before deciding on a follow-up. This increment deliberately has no automated unknown-outcome reconciliation or API for clearing that block. Even a later delivery webhook does not clear the intent's unknown state automatically. A queued placeholder remains visible with an uncertainty error and must not be treated as proof of non-delivery. Staff can take over, but should check for a prior accepted reply before sending another. A database failure after acceptance leaves `Sending`; the next explicit send request marks it `Unknown` without another transport call. This avoids automatic duplicates but is not a claim of exactly-once delivery at Meta.

## Schema and rollback

The additive `AiReviewedDispatch` migration extends existing intents with draft, requesting administrator, message, attempt/completion timestamps and bounded error code; adds unique draft dispatch and foreign keys; and adds virtual message authorship. Existing blocked Phase 1 audit records remain valid with nullable new fields. Human message ownership remains unchanged. The rehearsal upgrades and rolls back an empty isolated schema while preserving a synthetic human lead. Do not roll back populated dispatch/author records without a reviewed preservation plan. Disable dispatch first and preserve audit/history.

## Validation and remaining work

Validation uses a disposable database on the dedicated local test PostgreSQL instance and strict mocks. No real WhatsApp or OpenAI calls, production database writes or live flags were used. See [validation evidence](validation/reviewed-dispatch-validation.md).

The next implementation work is operational handling of uncertain outcomes, staff UI for controlled dispatch, and mixed human/AI weighted rotation with proper service identity and attribution. Autonomous sending, approved-template initiation, release-qualified live mode and real-model/staging acceptance remain separate work. This backend increment should not be described as completing those phases.
