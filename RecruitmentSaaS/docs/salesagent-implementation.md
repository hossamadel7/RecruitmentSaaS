# Self-improving SalesAgent implementation

## Repository assessment and integration plan

This extends the existing ASP.NET Core MVC/.NET 8 application, EF Core/Npgsql PostgreSQL database and cookie/role authentication. The operational CRM is deployment-scoped; the legacy tables have no general runtime tenant resolver. AI records already carry a configured deployment tenant GUID, while WhatsApp account/team checks provide the additional business boundary. Do not describe this as complete legacy shared-database multi-tenancy.

Reusable integration points:

| Area | Existing implementation |
|---|---|
| Incoming/history | WhatsApp webhook, `WhatsAppConversations`, `WhatsAppMessages`, durable receipt/intake workers |
| Identity/customer | WhatsApp contacts, nullable lead link, staff sender attribution, separate AI service identities |
| Draft orchestration | `AiSalesContextService`, `AiSalesDraftService`, `OpenAiSalesDraftProvider` |
| Controlled sends | `AiSalesDispatchService`, `AiAutomaticReplyService`, explicit account/agent/conversation release and takeover controls |
| Knowledge | Versioned, approved/expiring `AiSalesKnowledge`; fact review and improvement APIs |
| Memory | Reviewed customer memory and typed customer details |
| Actions | `CustomerOutcomeService`, follow-ups, supervised office booking and human takeover |
| Media | Meta media cache, transcription adapter, reviewed voice groups; other attachments require interpretation |
| Evaluation | Synthetic replay harness, fixed cases and real-model runner; transport substitutes prevent customer sends |
| Operations | Role-protected AI admin, readiness/usage reports, application options and background services |

The earlier full-history review produced source-linked conversation lessons outside Git and a local communication playbook. It did not implement durable continuous learning, embeddings, staff feedback or an integrated training dashboard.

Implementation sequence:

1. Preserve existing workflows. Add only learning state/analysis, knowledge metadata/embeddings, feedback, evaluation and usage records. Reuse approved knowledge rather than introduce a competing knowledge store.
2. Add resumable account-scoped scanning and durable analysis with source fingerprints, bounded extraction, explicit inferred versus verified labels, retries, privacy gates and administrator review.
3. Store embedding arrays in PostgreSQL using the existing Npgsql provider; perform bounded exact cosine retrieval over approved, unexpired, current-version knowledge. This requires no paid vector service or database extension. Expose bounds and index coverage; an approximate index is a future scale option, not a prerequisite.
4. Connect retrieval and language selection to existing draft context without changing dispatch permissions. Keep retrieved examples separate from approved business facts.
5. Expose controlled existing CRM reads/actions through authenticated, validated services; booking and customer-facing sends retain their existing approval paths.
6. Add continuous learning controls, evidence review, feedback and performance/coverage reporting to the existing admin area.
7. Add fixed evaluation cohorts, baseline comparison, usage/cost assumptions, migration/rollback documentation and isolated regression tests. No new outgoing messages are enabled during development.

## External prerequisites

The existing provider currently reports exhausted credit. Implementations and synthetic verification must work without provider access; real semantic quality, embeddings and multilingual model evaluation cannot be claimed until funded credentials are available. New learning/indexing features default off. Schema changes are generated locally and must be rehearsed before deployment. No live database migration or automatic outbound activation is authorized by this development brief.

## Delivered components

| Phase | Implementation |
|---|---|
| Historical and incremental analysis | `AiSalesLearningService`, `AiLearningSourceService`, `IAiLearningProvider`, `AiSalesLearningWorker` |
| Approved knowledge/index | Existing `AiSalesKnowledge` plus `AiKnowledgeIndex`; eight categories, confidence, source fingerprint, existing immutable approval/version/expiry controls |
| Semantic retrieval | Embeddings adapter and `AiSemanticKnowledgeService`, native PostgreSQL `real[]`, exact cosine, current-version filtering, explicit lexical fallback |
| Conversation engine | Existing draft/context/dispatch services with approved retrieval, CRM stage, language selection and the source-linked conversation playbook |
| CRM actions | `AiSalesCrmActionService` typed reads and reviewed note/follow-up/stage/handoff/booking commands, reusing existing ownership and booking services |
| Continuous review/feedback | Admin `/AiSalesTraining/Index`, account pause/resume, candidate correction/approval/rejection, official knowledge, indexing/search evidence, feedback and erasure |
| Evaluation | Fixed dataset cases, reserved historical cases, no-transport runs, human scoring and paired baseline comparison through `AiSalesEvaluationService` |
| Operations/costs | Call reservations, reported token usage, bounded retries/dead letters, descriptive staff/cohort reports, configurable cost assumptions and existing voice/dispatch operations reports |

No placeholder provider response is used in production. Synthetic providers exist only in the isolated test/browser host. Actual extraction/embedding/evaluation calls require the explicit approvals, a configured model/key and provider credit.

### Database migration

`20261104133753_AiSalesContinuousLearning` adds seven tables: `AiLearningAccounts`, `AiConversationAnalyses`, `AiKnowledgeIndexes`, `AiSalesFeedback`, `AiLearningUsage`, `AiEvaluationCases` and `AiEvaluationResults`. It does not recreate or overwrite customers, messages or conversations. Foreign keys retain the existing message/conversation/knowledge relationships; the analysis job is unique by tenant/account/conversation/source fingerprint. JSON analysis/evaluation payloads use JSONB, embeddings use native float arrays, and account erasure exclusions use UUID arrays. The migration snapshot includes the existing Armenia-routing migration unchanged.

### Analysis and outcome semantics

Account scans use a persisted PostgreSQL UUID keyset cursor. Normal continuous scans examine closed conversations; the explicit historical option also includes open conversations. Repeated scans revisit stored conversations to detect message edits, new messages, reviewed voice changes, CRM outcome changes and recent staff feedback. Fingerprints include the extraction pipeline version. Source changes stale earlier analyses and retire their derived knowledge.

Messages are ordered by provider timestamp, with stored creation time as fallback/tie-breaker. Extraction chunks contain up to twenty source-message fragments and have saved receipts, so retries resume successful chunks. Long texts split at 4,000 UTF-16 characters while preserving Unicode surrogate pairs, source IDs, part numbers and all original text; original CRM messages are never edited. Source version is rechecked before saving candidates and approving them. Processing and account-level advisory locks serialize competing workers; record versions reject stale administrative requests. Failures keep bounded error codes, use increasing delay, stop after five attempts and expose an explicit version-checked retry operation. Quota reservations happen before provider calls, including failures/interrupted requests.

The extractor records intent, sentiment, needs, products/services, questions, objections, techniques, negotiation patterns, buying signals, pricing discussions, follow-ups, escalation reasons, ineffective/possibly effective examples, next actions and candidate knowledge. These are model-inferred observations, not proven successful techniques. It preserves all requested sales-stage labels separately from the CRM's existing lead stages and never overwrites CRM stages from model inference.

Verified labels are composed server-side: a converted lead must link to an existing candidate in its branch; visits require `LeadVisit`; supervised bookings require the existing reviewed booking record. Ordinary recorded appointments and losses are explicitly labelled CRM records rather than proof of attendance or causality. Other outcomes are `UNKNOWN`. Dashboard conversion totals deduplicate candidate evidence when several conversations share a converted customer. Staff reports show both conversation counts and distinct recorded conversions; they are not causal rankings or commission attribution.

Historical authorship is credited only when confirmed human sends have exactly one identifiable author and no uncertain device author. The current conversation owner is not assumed to have written older replies. Staff comparisons are segmented by lead source, campaign and country; unknown outcomes, mixed authorship and sample counts remain visible. These cohorts do not establish equal lead quality or a causal best-salesperson ranking.

### Approval and source safety

Generated knowledge always begins as Draft. Business/pricing/FAQ assertions become fact candidates; strategy/objection/example/workflow/escalation candidates remain style guidance. The administrator reviews current accuracy, scope, privacy and expiry, and can correct or reject content. Facts with the same normalized title as a current fact require explicitly disabling the obsolete entry before replacement. This detects exact-title replacement conflicts, not every possible semantic contradiction. Other relevant conflicting entries must also be reviewed and retired.

The old generic approval endpoint cannot bypass the training review path for extracted candidates. Approval checks source freshness, version and a validity interval within 90 days. Approved facts are not edited in place. Corrections use a new candidate/version; existing generic retirement and the training disable operation remain available.

Retrieval excludes unapproved, expired, wrong-tenant/account, missing-source and changed-source items. It checks embedding model/dimensions/content/version compatibility. Query vectors are cached for five minutes using tenant/account/model/dimension/text-hash keys; permission and source eligibility are checked anew. Provider/quota failure falls back to labelled lexical retrieval, without falsely reporting semantic coverage. Up to 1,000 current entries are considered by default (configurable to 10,000), and up to 25 distinct learned source analyses are checked per request. Unchecked sources are suppressed; the service returns up to 12 facts and four style entries, with normal reply context taking six facts and two style entries. No approximate nearest-neighbour scalability claim is made; a larger index should be benchmarked before increasing those bounds.

Live drafts still save the selected knowledge IDs/versions, source citations, context fingerprint, intent, review note, model and usage through existing draft storage. Retrieval uses the latest five interpreted customer turns so a short follow-up question can retain the previously stated country or offer. Market scope comes from current approved facts, rather than a permanent Turkey-only prompt; country-related style examples alone cannot authorize an offer. Specific driving-availability requests still require explicit role-specific fact evidence or staff review.

A language hint covers Egyptian Arabic, Modern Standard Arabic, English and Arabizi; mixed/ambiguous language uses conversation context with Egyptian Arabic as default. This is heuristic language detection plus model instructions, not a validated multilingual accuracy claim. Local clarification/stop/handoff replies follow that language hint. Media placeholder text is not treated as an English customer message.

### CRM actions and sending

The model receives server-composed CRM reads and produces intent and reviewable reply/action guidance. It cannot execute SQL or unrestricted HTTP calls. The training action route supports `get_customer`, `get_history`, `get_available_products`, `get_current_pricing`, `check_appointment_availability`, `add_customer_note`, `create_followup_task`, `update_lead_stage`, `request_human_handoff` and `book_appointment`.

Writes require an authenticated active administrator, explicit human approval, a request UUID and current ownership version. Notes/follow-ups/non-outcome stages use the existing CRM tables with idempotent request receipts and audit events. Outcome-changing conversion/lost workflows are not exposed as arbitrary inferred-stage writes. Handoff uses `AiSalesControlService`; booking delegates to the existing customer-confirmation, policy, ownership and review checks in `AiOfficeBookingService`. Product catalogue reads indicate that current commercial terms still require verification; catalogue presence does not automatically publish a fact.

Existing lead creation, assignment, customer outcome and booking/reception flows remain the authoritative workflows. The training module does not introduce a second customer store or invent conversion records. Its dashboard links to the existing AI Sales page for draft review and reply-mode controls. It never enables dispatch, assigns customers automatically or bypasses takeover. The existing capability status now reports automatic availability only when the relevant generation/release configuration is enabled.

### Continuous learning, feedback and evaluation

Helpful/incorrect/human-edited/successful/failed/escalation feedback is saved with the real reviewer. The latest 100 feedback records enter their own conversation's next analysis, with corrections treated as unverified review evidence. A feedback label of successful is never a conversion record. The admin dashboard reports exact coverage/outcome counts and questions/objections from the latest 500 completed conversation analyses, not an unbounded all-history JSON scan. It does not fabricate an accuracy score from feedback.

The initial split reserves 10% of contacts by a deterministic hash for evaluation, configurable from 0–50%. Once a conversation has an analysis its split is retained on reprocessing. Reserved conversations produce analysis but no training knowledge candidates. Common contact IDs stay together; duplicate identities across different contact records and near-duplicate text are not guaranteed absent. Changing the percentage does not retroactively move already processed conversations.

Evaluation cases can be manually authored from anonymized/fictional prompts or built from reserved analyses. Datasets freeze after their first result and changes require a new dataset version. Each run uses the actual configured draft provider/prompt, stores its model/version and instruction fingerprint, and calls no WhatsApp transport. There are at most 50 cases per run. Reviewers assess factual support, relevance, language, qualification, context, objections and escalation, then compare scored pairs of the same case IDs against a named baseline. Configuration names are operator labels; stored model/prompt metadata must be checked when comparing releases. No result automatically enables a release.

The earlier 22,678-conversation study already informed the static editorial playbook. Cases drawn from that same historical study must not be described as independently unseen. Future reserved contacts and independently authored scenarios are needed for a defensible held-out quality claim.

### Privacy, erasure and retention

New external processing requires both privacy and external-processing approval and an explicit account allowlist. Common contact identifiers, links, known participant names and address/payment-detail lines are masked before learning calls. Original messages remain in the authorized CRM. Reviewed voice groups can supply masked text once per group; unreviewed voice and other attachments remain explicit uninterpreted media. This change does not bulk-transcribe or interpret historical media.

Masking is partial, not guaranteed anonymity. Unknown identifiers/names/narratives can remain; review is required before publishing examples or evaluation cases. Private notes are not sent as source messages. Historical messages and model-derived notes are untrusted data, never instructions. Financial/current-offer assertions need human verification; no unreviewed foundation-model retraining or automatic policy promotion is implemented.

The derived-data erasure endpoint deletes analysis, its knowledge/index vectors, linked evaluation cases/results and conversation feedback while preserving original CRM messages. It records a conversation exclusion to prevent the next scan from recreating erased learning. Disabling knowledge and erasing derived data remain authorized cleanup operations when the learning feature is off, provided the configured tenant/account scope remains. Automatic retention (90 days by default) deletes old analyses and derived knowledge and purges orphaned source indexes. Retention runs with the enabled background worker; if that worker is stopped, schedule/manual cleanup operationally rather than claiming it continues independently. Usage/audit rows contain no conversation bodies.

## Configuration and APIs

All new flags in `AiSalesLearning` default false. Keep them off during schema rollout. For a reviewed pilot, retain the existing stable `AiSales:TenantId`, set `AiSales:Enabled`, select the intended account GUIDs in `AiSalesLearning:ApprovedAccountIds`, then enable `AiSalesLearning:Enabled`. The dashboard requires role 1 and appears at `/AiSalesTraining/Index`.

Set `BackgroundEnabled` only after checking manual scans. `ExternalProcessingApproved` and `PrivacyApproved` authorize provider calls; the provider uses the existing `AiSalesEngine:ApiKey`/`Model` secrets. Reply evaluation also needs the existing draft-provider configuration. `SemanticRetrievalEnabled` activates embedding retrieval. Default embedding model/dimensions are `text-embedding-3-small`/512. Existing `AiSalesEngine` and `AiSalesDispatch` reply-release settings remain completely separate; do not enable them as part of training setup.

`MaxCallsPerDay` defaults to 100 tenant-wide learning/index/query/evaluation reservations per UTC day. `ScanBatchSize` is 25, `MaxMessagesPerConversation` is 5,000, `PollSeconds` is 60, and `RetentionDays` is 90. Conversations exceeding the message-count bound receive an explicit failed/review-needed job instead of blocking later scan pages; increase the reviewed bound or prepare those separately. Long individual texts are fragmented without silent truncation. Increase budget/bounds only after measuring load. Stop/retry controls preserve completed chunk receipts. Bump `OpenAiLearningProvider.AnalysisVersion` when changing extraction instructions, schema, chunking or privacy preprocessing so old receipts cannot be mistaken for the new pipeline.

All `/api/ai-sales-training` routes require role 1; mutations require the existing antiforgery header. Responses are noncacheable. Important contracts:

| Route | Purpose |
|---|---|
| `GET status`, `GET dashboard?accountId=…` | Configuration/coverage, bounded signals, verified/inferred metrics, usage |
| `POST configure` | Account, paused/history mode and expected settings version |
| `POST scan?accountId=…` | Advance one persisted scan page |
| `GET analyses`, `POST analyses/{id}/process`, `POST analyses/{id}/retry` | Evidence, one job, reviewed dead-letter retry |
| `GET knowledge`, `POST official`, `POST knowledge/{id}/review`, `POST knowledge/{id}/disable` | Existing knowledge with metadata and approval lifecycle |
| `POST index`, `GET search` | Index approved entries and inspect semantic/lexical selection reasons |
| `GET drafts`, `POST feedback` | Existing replies and reviewer feedback with an idempotent UUID |
| `POST actions/{conversationId}` | Explicit reviewed typed CRM action, ownership version/request UUID |
| `POST evaluations/quick-case`, `POST evaluations/from-analysis`, `POST evaluations` | Fictional/manual or reserved historical cases |
| `POST evaluations/run`, `POST evaluations/{resultId}/score`, `GET evaluations/compare` | No-transport evaluation and human-scored paired comparison |
| `DELETE conversations/{id}/derived?accountId=…` | Privacy erasure/exclusion without deleting original messages |
| `GET cost-estimate` | Configurable volume/rate assumptions, not a current vendor price quote |

Legacy APIs continue providing staff draft review, memory, scoped ownership/takeover, release and supervised booking. A training action does not impersonate a model as a human reviewer; system activity and authenticated human approvals have distinct audit attribution.

## Cost assumptions

Configure `InputCostPerMillion`, `OutputCostPerMillion` and `EmbeddingCostPerMillion` from the actual provider account's rates, using one consistent currency. Zeros mean rates are not configured, not that processing is free. A daily estimate uses:

`(reply input + learning input) × input rate / 1,000,000 + (reply output + learning output) × output rate / 1,000,000 + embedding tokens × embedding rate / 1,000,000`.

For a deliberately fictional example of 100 replies/day at 1,000 input + 100 output tokens, 10 learning chunks/day at 2,000 input + 200 output tokens, and 10,000 embedding tokens/day: rates of 1/2/0.1 units per million respectively give **0.145 units/day, 4.35 units for 30 days**. These are configurable arithmetic assumptions, not asserted current OpenAI prices. Hosting, WhatsApp charges, transcription, retries/unknown interrupted usage and taxes are excluded. Existing AI operations reports retain voice attempt/token/seconds visibility.

Embedding API/model details were checked against [official OpenAI embedding guidance](https://developers.openai.com/api/docs/guides/embeddings). No new paid search/vector hosting service was introduced.

## Deployment and rollback

1. Compare the actual target's migration history/schema with the repository; preserve the original service configuration and a tested database backup. The legacy migration chain does not bootstrap a complete empty CRM.
2. Rehearse the additive learning migration on a sanitized representative copy. The supplied upgrade SQL starts after `20261102133819_AddArmeniaCountryRouting`; use the actual baseline if the deployment is older. No SQL was applied to the live database during development.
3. Apply the reviewed migration before enabling the learning dashboard/flags. Feature-off context retrieval preserves legacy behavior when no learned entries exist, but enabled learning needs its tables.
4. Before publishing the new prompt/provider binary, pause existing AI agents and automatic reply release using the existing controls. New learning flags do not gate prompt changes for already enabled legacy reply agents. Publish using the existing compatible deployment method. The documented OVH install is self-contained .NET 8; do not assume a framework-dependent binary can use a different shared runtime. Keep secrets in deployment configuration.
5. Run the dashboard and synthetic evaluation with customer sending disabled. Select scoped accounts, approve privacy/provider processing, check budgets, scan a small historical batch, review candidates, index and evaluate before wider background processing. Provider funding and real semantic/multilingual quality review remain prerequisites.
6. Turn off `BackgroundEnabled`, `SemanticRetrievalEnabled` and the learning feature to stop new learning; existing transport/ownership release controls are independent. Prefer a forward fix. Before an older binary or schema removal, export required analysis/audit records and **retire knowledge derived from learning** (`AiKnowledgeIndexes.SourceFingerprint` nonempty), so an old lexical reader cannot serve entries whose source-check service has been removed.
7. The migration Down drops only new tables and destroys their learning/index/feedback/evaluation history. It does not delete original CRM messages. Do not run Down on populated production learning data without an approved preservation plan. The isolated migration rehearsal demonstrates schema compatibility and preservation of synthetic human data, not a complete production recovery/load test.

## Verification

The final full regression run passed **276 tests, with no failures or skips**, including the additive migration rehearsal and long-message handling. A subsequent 26-test targeted run also passed, including the added country-fact guard. After the final evaluation-metadata corrections, another 13 targeted tests passed. Checks cover strict structured output, source-ID validation, scoped vectors, source changes, pause, budgets, retry receipts, human-approved CRM action idempotency, fixed evaluation pairs, admin/CSRF/account boundaries and erasure without relearning. The migration rehearsal preserves original synthetic human records. Six Python history-processing tests passed. Final build and EF model/migration consistency checks passed; no pending model changes were reported.

`tools/verify_sales_training_ui.py` passed against the isolated `Tests/BrowserHost --training-demo` using only loopback, fictional data and strict synthetic transport/provider substitutes, with no browser page errors. It covers resume/scan/analysis, correction/approval, indexing/search explanation, fixed-case creation, baseline/candidate runs, human scoring and comparison. Screenshots are under `docs/validation/sales-training-*.png`. No real customer or provider was contacted by these checks. Browser/mobile layout was visually inspected and the temporary browser host was stopped. JavaScript syntax and whitespace checks passed. These checks establish implementation behavior, not real-model sales quality, production load capacity or conversion lift.
