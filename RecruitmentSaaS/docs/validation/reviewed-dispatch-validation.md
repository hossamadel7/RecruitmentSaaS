# Reviewed dispatch validation — 2026-10-12

The main app and test project built successfully. The existing five warnings remain in FacebookLeadWebhookController and Admin/FormStats. All **81 tests passed**, including 15 new dispatch cases; no skipped tests. `git diff --check` passed with only existing LF/CRLF informational warnings.

New coverage verifies queue/request and repeated-send idempotency; one transport attempt for concurrent send requests; virtual authorship without a human sender; all three release gates; stale knowledge; takeover invalidation; a closed reply window; ambiguous success and thrown transport exceptions; recovery of an interrupted attempt without retry; cancellation while disabled; foreign tenant rejection; daily budget reservation including unknown attempts; unreviewed/escalated reply rejection; administrator-only API access and CSRF enforcement. Existing tests cover foundation, provider drafts, voice, bookings, operational reports and synthetic replay checks.

The migration rehearsal now removes the new dispatch schema first, restores the prior phase schemas, then reapplies the dispatch migration, preserving the synthetic human lead. All databases were disposable fixture databases; application production configuration was never loaded. The dedicated PostgreSQL process was stopped after verification.

Transport is strictly mocked. These tests do not establish real Meta connectivity, delivered-message behavior, provider cancellation timing, real model quality, production latency or live release approval. No real messages were sent. No dispatch UI was added or browser workflow claimed. The feature remains disabled by default.
