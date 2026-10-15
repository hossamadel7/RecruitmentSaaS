# Historical chat learning — initial playbook review

The owner's 13 October 2026 workflow distinguishes four parts:

| Learning type | Current approach |
| --- | --- |
| Historical learning | Build the initial sales playbook from approved previous chats. |
| Conversation memory | Keep customer-specific details in that customer's scoped, reviewed memory. |
| Continuous improvement | Review new chats, results and objections; approve improved approaches. |
| Fine-tuning | Optional later, after privacy, contractual and performance review. |

This phase prepares local review candidates; it does not create or upload a fine-tuning dataset or launch a training job. Production knowledge, memory and customer sending remain unchanged. Existing memory and fact-review services are implemented locally but are not thereby enabled on the live server.

## Prepared review pool

`tools/prepare_ai_sales_playbook.py` reads the existing private masked corpus, verifies its manifest fingerprint and enforces the Bakar/Mekky/Sherif account allowlist. `testttt` is excluded. It reconstructs message order within each conversation and identifies short customer questions with a nearby confirmed staff text reply. Nearby replies are contextual evidence only: they are not guaranteed to answer that question and are not accepted as truth.

Conversations mentioning Turkey without a Saudi keyword are candidates for market review, not a guaranteed Turkey classification. Audio, system/AI targets and unconfirmed staff replies are not used as source answer evidence. A keyword classifier proposes topics; it can misclassify questions and does not establish a successful sales outcome.

The preparation produced **50 playbook candidates and 10 held-out test candidates**, each from a different conversation. Exact normalized questions do not overlap. The same person across different accounts or near-duplicate questions have not been ruled out; manual review is required. Every selected example is from an ongoing conversation. Beginning-of-conversation coverage must be assessed separately using the existing introduction guidance and future reviewed examples.

Proposed replies are editorial drafts using the owner's currently confirmed facts, not copied historical answers or owner-approved examples. The original masked question, limited historical context, source references and proposed corrected reply stay in the private review directory. All examples are `pending_owner_and_privacy_review`, `trainingReady=false`, with zero approved candidates. Partial masking does not guarantee anonymity.

Private artifacts: `%LOCALAPPDATA%\RecruitmentSaaS\ChatLearningReview\playbook-review-2026-10-13-v1`. On the Windows Store host, Python may resolve LocalAppData into the app's LocalCache; this remains outside the repository. The directory contains `playbook-candidates.jsonl`, `holdout-candidates.jsonl`, `manifest.json` and `first-review.md`. These are review artifacts, not provider-ready training files.

## Conversation continuity correction

The owner explicitly corrected repeated introductions. The provider now supplies `ConversationState.IsFirstReply` based on prior outgoing text history or reviewed conversation memory. It greets and introduces the assistant at the start, answers directly in ongoing conversations, and still answers identity questions honestly. It also distinguishes the assistant's Turkey-only scope from company-wide availability.

Six provider contract tests passed, including first-reply flags for new conversations, prior replies and reviewed memory. Three real-model cases checked ongoing salary/fees questions and an explicit identity question. The ongoing salary and fees replies did not repeat the greeting or assistant/company introduction. The identity reply identified the virtual assistant without restarting the welcome. These are narrow behavior checks, not full release qualification. History is bounded to the CRM's recent context; reviewed memory should preserve relevant continuity when older messages leave that window.

## Next review

Review one candidate at a time. Check personal data, market relevance, whether the proposed response answers the actual question, factual support, tone and unwanted promises. Approve or correct the reply explicitly; reject unsuitable examples. Keep evaluation conversations separate. Approved approaches can then be published through the account-scoped Playbook workflow with an accountable reviewer and validity dates. Customer-specific facts belong in customer memory rather than shared Playbook entries. Fine-tuning remains a separate optional later decision.
