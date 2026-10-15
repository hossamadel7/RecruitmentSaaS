# Phase 3 voice validation — 2026-10-12

Implemented and checked on branch `codex/ai-sales-foundation`, using the local checkout. No production migration, external provider call, real customer recording, outbound WhatsApp message, deployment, commit or PR was performed.

## Automated evidence

`dotnet test Tests/RecruitmentSaaS.Tests.csproj --no-restore`: **44 passed, 0 failed, 0 skipped**. Twelve new voice tests extend the previous 32. The fixture uses only the dedicated loopback PostgreSQL instance, creates unique disposable databases and drops them afterward. Provider/media contracts use fake HTTP handlers or strict mocks.

Coverage includes consecutive voice grouping and duplicate reservation; staff intervention splitting groups; historical and cross-chat rejection; staff-reviewed transcripts replacing audio placeholders as one context entry with recording IDs; draft generation from reviewed voice; preservation of CRM fields and original messages; unknown confidence/critical-number confirmation; immediate takeover during a provider call; spoken human requests; partial failure and explicit retry without repeating completed parts; interrupted worker recovery and daily attempt limits; transactional automatic queue rollback without media calls; media host, hash, MIME, size, container and redirect failures; transcription multipart contract with Arabic/JSON/log probabilities; unknown usage/confidence; HTTP authentication, role, CSRF and conversation visibility.

The migration rehearsal now removes voice tables before engine/foundation tables, reapplies all three additive migrations, and confirms existing human lead ownership and age survive. This is an empty rollback rehearsal, not permission to drop populated AI tables.

Build succeeded with the same five pre-existing warnings in `FacebookLeadWebhookController` and `Views/Admin/FormStats.cshtml`. `dotnet ef migrations has-pending-model-changes --no-build` reported no model changes after `20261012150418_AiSalesVoice`. `git diff --check` was clean.

## Browser evidence

The separate BrowserHost ran on `http://127.0.0.1:5189` with `--voice-demo`, a synthetic database, mock providers, no hosted workers and no outbound transport. It seeded one incoming Audio message and a synthetic ReadyForReview transcript with unknown confidence and an appointment number.

The rendered admin page showed the voice section, original recording ID, unknown probability indicator and review controls. Clicking approval without critical-details confirmation returned a validation error. Correcting the transcript to explicitly say no appointment was booked, checking confirmation and approving changed the visible status to **اعتمده الموظف** and displayed the reviewed text. Screenshot: [phase3-voice-browser.png](phase3-voice-browser.png).

## Limits of the evidence

These checks validate code paths and browser review behavior, not real Meta media access, actual OpenAI responses, Egyptian Arabic acoustic accuracy, calibrated confidence, privacy policy compliance, or deployment compatibility with an existing staging schema. Approved real-sample evaluation and retention/access policy remain required before live enablement. Default production configuration leaves the feature disabled.
