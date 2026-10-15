# Owner-reviewed facts from CRM chats

The owner requested learning useful facts from CRM chats and asking them which statement is correct. This increment implements that review/publication workflow. No real CRM corpus was extracted or published during development. It does not run model fine-tuning.

## Review screen

Open **المبيعات الافتراضية → مراجعة معلومات المحادثات سؤالًا بسؤال** (`/AiChatLearning`) as a Role 1 administrator. Select an active account already approved in the existing `AiChatLearning` server configuration. The feature defaults off; no production configuration was enabled. Rehearse/apply the additive migration and configure the intended deployment/account boundary before real use. This step itself needs no OpenAI key.

Extract each page of stored history, then review one pending question at a time. The screen shows the masked source statement, speaker, original message date in Cairo time, internal message reference and related current knowledge. Write the correct general company fact, attest to privacy/accuracy review, choose an expiry of 1–90 days, and explicitly retain or retire each related fact. Publish the confirmed answer or reject unusable evidence. The screen reports the decision and advances. A new extraction round is available after a fixed snapshot finishes; duplicate candidates are skipped.

Retiring an entry retires its entire content. If it groups salary, benefits and hours, preserve or replace all still-correct details. Related entries may be compatible rather than contradictory; keep/retire decisions are explicit. Approval is the administrator's accountable confirmation, not independent verification or proof that all retained facts are consistent.

## What extraction means

A local keyword detector proposes salary, fees, office, work/benefits, documents, timing, roles and advertised-age topics. It saves the source statement as a candidate, never a trusted fact. Customer questions, unrelated text and several topics from one statement can be proposed. Synonyms may be missed. No semantic AI extraction or exhaustive contradiction detection is claimed. Sales-style extraction remains manual/later work.

Only eligible text messages produce fact candidates in this increment. Reviewed audio may appear in prepared corpus pages but does not yet become a sourced candidate here. Unreviewed recordings/media require separate interpretation. AI/system replies and unconfirmed/failed outgoing sends are excluded. Text or author/source changes produce a new source version; Sent→Delivered→Read receipts do not repeat the question.

Manual knowledge is matched by keywords. Previously published chat-derived facts are also linked by their reviewed topic, so different owner wording remains discoverable. Publication stops if more than 500 current facts exist in the account instead of silently omitting related knowledge. These are review aids rather than judgments of factual contradiction.

## Persistence and safeguards

`AiChatFactCandidates` stores scope, topic, source reference/digest, masked statement, state/version, reviewer/time and published knowledge reference. Deleted sources clear their FK and cannot be approved. Candidate copies need their own restricted retention/deletion policy; source deletion does not automatically erase the masked proposal.

Source digests are checked when copying and publishing; an edited or deleted statement requires rejection/re-extraction. Source-row protection prevents edits during publication. Candidate locks/expected versions prevent duplicate decisions. A shared account lock now covers knowledge create/approve/retire/publication; related versions must still match. Every current related fact must have an explicit decision. Direct SQL writes bypass these application guarantees.

Publishing atomically creates an Approved Fact using the owner's corrected text, applies explicit retirements, and records provenance and audit. It never edits an existing approved entry in place. The existing context reader retrieves the valid account-scoped fact for subsequent drafts; changed context invalidates older drafts as before. Rejection publishes nothing. Historical customer/staff assertions alone are not verified current business facts.

The page/API require Role 1, antiforgery protection for writes, and configured account scope. Responses are noncacheable and statement rendering uses DOM `textContent`. Routes: `POST /api/ai-chat-learning/extract`, `GET /api/ai-chat-learning/facts/next?accountId=...`, `POST /api/ai-chat-learning/facts/<id>/review`. The decision includes candidate version, publish/reject, corrected title/content, privacy attestation, UTC expiry and related knowledge ID/version/retire decisions. No provider secret or raw media URL is shown.

## Verification and rollout

The full regression run passed **114 tests**. Final source/topic changes then passed nine fact-related checks (including an existing foreign-scope case) and all nine corpus checks. The `AiChatFactReview` migration was rehearsed down/up in a disposable schema while preserving human lead data; EF reported no pending model changes. The five existing build warnings remain; `git diff --check` passed. Do not roll back populated review/knowledge records without a preservation plan.

The browser used only a disposable account and explicitly fictional amounts. It verified source/related-fact display, mandatory review, corrected publication, advancing to no pending questions and a rescan adding zero duplicates. Proof files: `docs/validation/chat-fact-question-browser.png`, `chat-fact-review-browser.png`, and `chat-fact-published-browser.png`. The test app and dedicated PostgreSQL process were stopped afterward. No production migration, real chat extraction, provider request, customer send or historical upload occurred.

This delivers reviewed knowledge learning. Actual CRM processing still needs the reviewed migration/account configuration; real AI drafting needs provider configuration. Model fine-tuning, complete media coverage, semantic extraction, automatic learning from new chats and live dispatch remain separate capabilities.
