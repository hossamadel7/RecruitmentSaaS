# Phase 2 — reviewed AI sales drafts

This increment adds a manual draft workflow to `/AiSalesAdmin`: approved knowledge, staff-reviewed customer memory, Egyptian Arabic prompt strategy, a bounded OpenAI Responses adapter, proposal storage, citations, and human review. It extends the validated Phase 1 ownership foundation.

It is a reviewable starting point for the sales engine. Semantic/vector retrieval, autonomous CRM tools, model-generated long-term summaries, automatic message-triggered drafting, and customer-facing AI dispatch remain later work. Reviewed incoming voice interpretation is supplied by [Phase 3](ai-sales-phase3-voice.md), supervised office booking by [Phase 4](ai-sales-phase4-booking.md), and local evaluation/operational readiness by [Phase 5](ai-sales-phase5-readiness.md). There is no model quality claim from the mocked tests.

## Draft lifecycle

1. Admin creates a fact or sales-playbook entry for an existing WhatsApp account. Each entry needs a source reference and validity period of at most 90 days. It begins unapproved.
2. Admin verifies the source and approves it. Approved text is immutable; retire it and publish a new entry to revise it. Expired, future-dated, unapproved, retired, other-account and other-tenant entries are excluded from context.
3. Manually assign an eligible conversation to an unpaused Shadow agent. Finish existing intake or explicitly resume to Assisted. A customer must have the latest eligible message.
4. Optionally record a staff-reviewed summary. The server still seeds name, age, profession, lead status and saved appointment from existing CRM fields. The model cannot overwrite them or the reviewed summary.
5. Request a draft. A durable row reserves the request, captures control/memory versions and a context fingerprint, and counts toward the daily limit. A unique tenant/request key prevents duplicate provider calls. The provider runs outside the conversation lock so staff can take over immediately.
6. Before saving a usable proposal, the server rechecks ownership, enabled/paused state, customer context, history and selected knowledge. Any change makes the draft Stale. Review checks these conditions again.
7. A human marks the draft Reviewed or Rejected. This records an audit event and never invokes WhatsApp transport or modifies a booking. Reviewed text is still only an operator-reviewed proposal, not a message in customer history.

The browser displays source titles and verification references and warns when reviewed drafts become outdated. No automatic clipboard transfer or send button is added.

## Knowledge and memory trust

Local lexical retrieval scores normalized terms from the latest customer message, considers the latest 500 currently valid approved entries for the account, and selects up to six facts and two playbook examples. Playbook content is style guidance only. Model citations must refer to selected approved facts, never playbook examples or unknown IDs. This is bounded lexical retrieval, not semantic RAG; catalog coverage and relevance need staging evaluation before increasing volume.

Context contains up to 20 messages with at most 600 text characters each. Failed/queued outgoing messages are excluded. Phase 3 staff-reviewed voice groups can supply up to 4,000 characters and preserve their recording references; other media is represented as uninterpreted. Reviewed summaries are limited to 2,000 characters; knowledge text to 4,000 per entry. Common phone-number and email patterns are removed. This redaction does not guarantee complete anonymization: customer names and arbitrary sensitive text can remain. External processing is separately disabled by default.

The prompt treats messages, summaries and retrieved text as data rather than instructions. It separates approved business facts from historical examples, avoids repeating known questions, prohibits invented vacancies/salaries/fees/visa guarantees, and requests text in Egyptian Arabic. It does not decide employment eligibility from sensitive attributes. All outputs require human review; citation validation cannot prove that every generated claim is supported.

If no approved fact exists, the server creates a local human-escalation draft without a provider call. Uninterpreted media and recognized human-request/decline phrases also get local handling. No historical customer data is uploaded for training, embeddings or bulk processing.

## Configuration

`AiSalesEngineOptions` defaults to:

| Setting | Default |
| --- | --- |
| `DraftsEnabled` | false |
| `ExternalProcessingApproved` | false |
| `ApiKey` | empty |
| `Model` | empty; explicitly choose a model supporting strict structured outputs |
| `MaxDraftsPerDay` | 20 per deployment tenant, UTC calendar day |
| `MaxOutputTokens` | 800 |

Use deployment secrets/environment variables, such as `AiSalesEngine__ApiKey`; never put a real key in tracked files. Enabling external processing requires enabled drafts, an explicit model, and a key at startup. Phase 1 must also be enabled with its stable deployment tenant GUID. The engine remains disabled in the application's current configuration.

The adapter uses the fixed `https://api.openai.com/v1/responses` endpoint, `store=false`, strict `text.format` JSON schema, a 45-second call timeout, and a 128 KiB response limit. It rejects refusals, incomplete responses, unsupported intents, missing evidence, invented citations and detected contact identifiers in replies. It performs no automatic provider retries. `store=false` controls application-state storage and does not by itself establish a zero-retention agreement; review the account's data controls before enabling real customer processing.

Official references used for the adapter: [Responses migration and request shape](https://developers.openai.com/api/docs/guides/migrate-to-responses), [Structured outputs](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=responses).

## Reliability and usage

At most two fresh draft generations per tenant are permitted concurrently, across processes. The daily cap counts every reserved draft, including local fallbacks and failed attempts. An interrupted Generating row is marked Failed when its same request key is replayed; it is not silently sent to the provider again. A deliberate new request needs a new key and consumes a new quota reservation.

Rows retain requester, prompt version, reported model, input/output token counts where available, decision, review note, source IDs, source versions, and control/memory versions. Usage reported for stale or rejected provider proposals is retained. Missing usage is unknown, not zero; monetary costs are not calculated because model pricing is not configured. Provider failures may incur costs without returning usage. Audit/errors avoid full prompts, raw provider bodies, credentials and unnecessary customer text.

The manually requested synchronous workflow does not add a background draft dispatcher. Raw request prompts are not saved. Draft replies and staff summaries can contain customer data: define retention and restricted operator access for those tables before live use.

## API contracts

Cookie authentication and antiforgery header `RequestVerificationToken` apply to every mutation. Draft generation requires `Idempotency-Key` and `expectedVersion` for conversation ownership. Memory and knowledge mutations use their own `expectedVersion`; initial memory version is 0. Approval cannot modify approved content.

| Endpoint under `/api/ai-sales-engine` | Permission / behavior |
| --- | --- |
| `GET status` | Roles 1/7/8; configuration status without secrets |
| `GET knowledge?accountId=...` | Admin; latest 100 entries |
| `POST knowledge` | Admin; create unapproved account-scoped fact/playbook |
| `POST knowledge/{id}/approve` | Admin; verified approval with version |
| `POST knowledge/{id}/retire` | Admin; retirement with version |
| `GET conversations/{id}/memory` | Existing conversation visibility |
| `PUT conversations/{id}/memory` | Scoped staff summary, versioned and audited |
| `GET conversations/{id}/drafts` | Scoped latest 10 drafts, source references and freshness |
| `POST conversations/{id}/drafts` | Scoped manual generation and daily quota |
| `POST drafts/{id}/review` | Scoped `{accept:true/false}`; audited review only |

## Migration, rollback and acceptance

Migration `20261012132047_AiSalesDraftEngine` adds `AiSalesKnowledge`, `AiCustomerMemories`, and `AiSalesDrafts`. It follows Phase 1 and leaves existing human workflow columns unchanged. Applying to an existing installation still needs the historical-schema preflight described in the Phase 1 notes. No migration has been applied to an existing installation.

Disable `AiSalesEngine__DraftsEnabled` and `AiSalesEngine__ExternalProcessingApproved` for an operational rollback. Preserve draft, memory and knowledge history; prefer a forward fix. Binary rollback to Phase 1 can leave these additive tables in place. Removing them destroys their history and must be separately planned; removing Phase 1 first conflicts with the engine foreign keys. The integration rehearsal reverses/reapplies both migrations only in an empty synthetic foundation database, preserving legacy human data.

Thirty-two integration/contract tests pass, including the earlier 20 foundation tests. New checks cover fact/account/tenant/approval/expiry isolation, immutable CRM data, idempotent paid requests, local fallback, external-processing gate, takeover during generation, memory/message/knowledge staleness, changed CRM fields, inactive accounts, invented citations, daily quota, role/CSRF enforcement, strict Responses payloads, refusal handling and contact redaction. Migration/model consistency and build pass with the same five existing warnings.

The local browser check verifies manual assignment, local fallback drafts, human review, stale warnings after a summary update, knowledge creation/approval, empty conversation selection, and disabled-provider failure display. [Browser evidence](validation/phase2-drafts-browser.png) shows a reviewed local fallback in the narrow browser view. No real OpenAI call, customer message send, staging database migration or live Egyptian Arabic quality evaluation was performed.

Next acceptance gate: approve a small set of current business facts and a synthetic evaluation set; configure a model/key in an approved staging environment; measure factual support, Egyptian Arabic tone, takeover behavior, latency and actual usage before allowing real customer context or expanding the engine.

The [evaluation pack](evaluation/README.md) supplies fillable knowledge templates, 16 synthetic Egyptian Arabic scenarios and a score sheet for that gate.
