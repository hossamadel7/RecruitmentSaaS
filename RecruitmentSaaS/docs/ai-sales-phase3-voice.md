# Phase 3 — reviewed incoming voice notes

This increment extends the Phase 2 draft workflow with a separate durable audio queue, secure Meta media retrieval, Arabic transcription, and staff transcript review. Replies remain text drafts requiring review; this feature sends no WhatsApp messages, creates no appointments, and overwrites no original recordings or CRM facts.

## Processing and review

An authorized operator selects one to four recent incoming recordings in an explicitly assigned, resumed Shadow conversation. Defaults accept messages from the last 15 minutes. Outgoing staff recordings, other chats/accounts, duplicate selections and historical messages are rejected. A recording can belong to only one turn. Consecutive selected recordings within 30 seconds are grouped, with ten seconds of quiet before processing; intervening messages split the turn. Late arrivals after processing starts create another turn. The inbox remains the place to listen to the original recording.

Optional `AutoQueueIncoming` adds eligible new incoming audio within the webhook's existing conversation lock and transaction. It is off by default. It does not import historical staff audio, and no media download or transcription occurs on the webhook request.

`AiVoiceWorker` uses a PostgreSQL advisory lock independent of the webhook worker. Each completed part stores its original message/media references, SHA-256, content type, byte count, transcript, model, token counts or seconds when reported, and the minimum token probability when available. Audio is bounded in memory, never written to a local file, and the returned buffer is cleared after processing. URLs and access tokens are not returned to the browser or logged by this feature.

Media IDs must be numeric. Metadata comes from the fixed Meta Graph endpoint; the download URL must use HTTPS port 443 on `lookaside.fbsbx.com` or a `*.fbcdn.net` host, without user information. Redirects are disabled. Metadata and body size, MIME type, SHA-256 and the container signature are checked. Supported containers are OGG, MP3, WAV, M4A/MP4, WebM and FLAC. These checks do not establish that a file is decodable; provider rejection is a failed job.

Transcription requests use the fixed OpenAI audio endpoint, Arabic language and an Egyptian Arabic instruction to preserve what was heard without inventing unclear words or numbers. The [official transcription contract](https://developers.openai.com/api/reference/resources/audio/subresources/transcriptions/methods/create) supports OGG and JSON log probabilities for the selected transcription models. The implementation supports `gpt-4o-transcribe`, `gpt-4o-mini-transcribe` and `gpt-4o-mini-transcribe-2025-12-15`.

All transcripts require staff review. Missing or invalid token probabilities remain unknown. The minimum token probability is a conservative indicator, **not calibrated transcription accuracy**. Unknown/low probabilities and numbers, fees, salary or appointment markers require explicit confirmation of critical details before approval. Operators must clarify uncertain details with the customer, correct the text, and confirm them; a checkbox cannot prove that this happened. Spoken or reviewed requests for a person or refusals stop automation conservatively and mark the turn unavailable to the model.

Only reviewed text enters sales context. A group occupies one history entry with every original recording ID and the review version. Unreviewed media in the current consecutive customer messages blocks external drafting and produces the existing local clarification draft. Transcript review changes context fingerprints, making earlier drafts outdated. Existing staff-reviewed transcripts can remain historical context after control changes; newly pending/processing turns tied to an old ownership version cannot be approved.

## Failures, retries and limits

The provider runs outside the conversation lock. Takeover remains immediate; ownership, agent/account/team availability and feature gates are checked before download, again before transcription, and before completing or reviewing a turn. Results that lose control become Stale and cannot enter model context.

Each attempted provider call is durably reserved against the per-tenant UTC daily limit (default 100) before dispatch. There are no automatic paid-call retries. Worker restart marks interrupted Processing jobs Failed; the global worker lock prevents recovery of another worker's live job. Explicit operator retry may repeat the cost of an ambiguous interrupted call. Completed parts are retained and not transcribed again. Failed or pending parts are retried, and every new attempted call consumes quota. Reported usage can remain unknown after a timeout or process crash. A combined transcript over 4,000 characters fails for manual handling; per-part evidence is retained.

Queue, transcript review and confirmed retry actions are audited. Review and retry require current optimistic versions. API mutations require CSRF tokens; roles 1/7/8 and existing conversation visibility apply. The deployment tenant ID follows the Phase 1 boundary; the legacy CRM is not converted to shared-database tenant isolation.

## Configuration and release

Defaults are disabled. Configure through the deployment's secret/configuration mechanism:

```text
AiSales__Enabled=true
AiSales__TenantId=<deployment tenant GUID>
AiVoice__Enabled=true
AiVoice__ExternalProcessingApproved=true
AiVoice__Model=gpt-4o-mini-transcribe
AiVoice__ApiKey=<secret supplied outside source control>
AiVoice__AutoQueueIncoming=false
```

`MaxBytes` defaults to 10 MiB, maximum 25 MiB. Maximum four parts per turn. Quota, gap, quiet period, message age and probability threshold are validated at startup. The voice and draft provider keys/gates are separate; enabling transcription does not enable external draft generation. Meta credentials continue using the existing server credential store.

Migration `20261012150418_AiSalesVoice` adds two tables and references existing messages, conversations, agents and reviewers. No production database was migrated. Rehearse the historical baseline and additive migrations against a sanitized staging database before deployment; do not run the repository's destructive migration scripts.

Before using real recordings, configure approved external processing and define transcript retention/deletion and privacy access policies. This increment retains transcripts under existing database access/backup controls and does not implement automatic expiry or a new deletion UI. Evaluate Egyptian Arabic, noisy audio, numbers, opt-outs and mixed consecutive voice/text with approved samples. Mock tests verify mechanics and failure handling, not acoustic accuracy or real Meta/OpenAI connectivity.

Supervised bookings are supplied by [Phase 4](ai-sales-phase4-booking.md), and local evaluation/operational readiness by [Phase 5](ai-sales-phase5-readiness.md). Automatic sales dispatch, semantic retrieval, and historical staff audio training remain later work.
