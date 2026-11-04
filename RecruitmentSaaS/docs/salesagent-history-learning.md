# SalesAgent history learning — 2 November 2026

The owner selected customer WhatsApp conversations as the learning source and requested support for all destinations after checking current offers. The existing Mustafa assistant, account ownership and transport controls remain the starting point.

## Completed analysis

A fresh read-only PostgreSQL snapshot covered all 245,827 stored messages in 22,678 conversations across Bakar, Mekky and Sherif. The previously excluded test account remains excluded. Closed, unassigned and older conversations with messages were included. Source message dates span 5 October to 2 November 2026.

The initial local analysis scanned 231,913 text messages. It counted 8,294 audio messages and 5,620 other media messages without interpreting their content. A subsequent full conversation review is described below.

## Full conversation review completed 4 November 2026

The owner explicitly requested going through all chats rather than stopping at keyword analysis. The masked text of all 22,678 conversations was processed in chronological conversation order through the existing gpt-4.1-mini provider. All 245,827 message records were represented; media without interpreted text remained explicit placeholders. The frozen history is from 2 November, not a live continuously growing export.

The job completed 567 review groups. Each conversation has exactly one accepted review; missing/duplicate references and evidence indexes outside their conversation were rejected. A per-conversation schema constrains the allowed reference and message indexes. Saved receipts allowed recovery from connectivity interruptions without reprocessing successful groups. An empty country classification is preserved explicitly as unknown.

The model labelled 15,749 conversations as containing a candidate lesson, 808 as containing a mistake to avoid, and 6,121 as having insufficient text. These are model-generated review labels, not independent quality or conversion scores. Country labels may overlap within a conversation and are not verified current offer availability.

The owner directed learning to the countries in the system. The catalogue snapshot yields UAE, Saudi Arabia, Turkey, Kuwait, Greece, Belarus, Albania and Iraq. Other-country conversations contribute general communication lessons only. Historical facts are not automatically certified as current company knowledge.

All accepted reviews entered local thematic consolidation. The provider completed 21 of 23 additional synthesis groups before returning `credit_balance_exhausted`; the final editorial consolidation was completed locally from every saved conversation review. No conversation review was lost or omitted. Frequent staff behavior is not necessarily a good rule: unconditional collection of personal details, blocking a requested handoff until intake is complete, unverified payment terms, invented interview availability and repetitive visit pitches were corrected before reuse. The resulting [14-rule communication playbook](knowledge/salesagent-learned-playbook.md) includes source-conversation references and original fictional Egyptian Arabic examples.

Unlike the initial offline scan, this stage sent masked conversation text to the configured model provider with `store=false`; it did not upload a fine-tuning dataset or create a trained model. Partial masking is not guaranteed anonymity. Private review artifacts remain outside Git. No customer was contacted and no production knowledge or service configuration was changed.

Private review directory: `RecruitmentSaaS/ChatLearningReview/salesagent-semantic-2026-11-03-v3` under LocalAppData. It contains the source map, plan, group receipts, exact coverage report, usage totals and `conversation-learning.jsonl`. Provider synthesis is in the sibling `salesagent-playbook-2026-11-03` directory. The complete final playbook and all-review thematic map are in `salesagent-playbook-final-2026-11-04`.

## Local SalesAgent integration

`AiSalesConversationPlaybook` contains the 14 editorial rules and the source snapshot fingerprint, without customer text, identifiers or offer facts. `OpenAiSalesDraftProvider` now appends these rules to the request instructions and identifies this local version as `egyptian-sales-draft-v10-full-history-playbook`. Current factual citations, configured release scope and existing human takeover/transport controls still apply. This change has not been deployed to the production service.

The voice content of 8,294 audio records and the content of 5,620 other media records in the frozen snapshot were not interpreted. The live voice check found no existing reviewed transcript groups to reuse. Completing text review is not a claim that those recordings or attachments were understood.

| Account | Conversations | Messages |
|---|---:|---:|
| Bakar | 10,732 | 124,348 |
| Mekky | 4,522 | 43,486 |
| Sherif | 7,424 | 77,993 |

Customer text yielded approximate, overlapping topic signals: role questions in 5,806 conversations, fees in 2,034, contracts in 1,596 and salaries in 1,428. These are review priorities, not successful-sale labels. Market keywords include Turkey, Oman, Saudi Arabia and other countries; mixed and unlabelled chats require context review.

Candidate review revealed nearby staff responses that do not answer the customer's question, unsupported assurances, and multiple markets within one chat. Historical answers must not be automatically accepted as current business facts or ideal assistant targets. Proposed playbook entries cover answering the actual question, separating destinations/offers, handling objections and requests for time, and preserving conversation continuity.

## Current-offer check

Read-only checks found five packages and nine company-job records marked active. Destinations include UAE, Saudi Arabia, Turkey, Kuwait, Greece, Belarus, Albania and Iraq. The records do not establish an offer expiry date or verified remaining headcount. Oman appears frequently in chats but has no active package/company-job record in this snapshot.

All 24 approved assistant knowledge entries across the three accounts were expired at the check. None was renewed. Current fees, salaries, inclusions, documents and availability must be confirmed before publishing all-destination offer facts. The owner has been asked to verify the concrete catalogue inventory, including Oman.

## Artifacts and operation

`tools/analyse_sales_history.py` uses existing SSH access and read-only database sessions. It masks common identifiers and known names on the server before export, pseudonymizes conversation identifiers per snapshot, validates account scope, reconstructs chronology, excludes AI/system and failed outgoing messages as human reply examples, and saves restricted review artifacts outside the repository. Speaker attribution and nearby-pair relevance still require review. Unknown identifying details can remain after partial masking.

Run with `python tools/analyse_sales_history.py --output <new-private-directory>`. A new directory is required to preserve previous review decisions. Standard output contains aggregate totals and the destination path, not customer excerpts. A single message query provides a consistent message snapshot; the separate offer query may reflect later catalogue changes. Recheck offer versions before publication.

Private artifacts are in the Python runtime's LocalAppData under `RecruitmentSaaS/ChatLearningReview/salesagent-2026-11-02-v1`: masked history, aggregate analysis, candidate excerpts, current offers, an offer review sheet, findings, and four draft Playbook entries. Windows app redirection can place this under the Codex package's LocalCache rather than the shell's LocalAppData. The actual path is printed on completion.

No production data/configuration was changed, customer messages sent, or model fine-tuned. The subsequent provider-based review is recorded above. The existing provider and local policy still contain Turkey-specific behavior; expanding live factual coverage should follow confirmation of market-scoped offer facts and validation against cross-market cases.

## Verification

Local synthetic checks passed for corpus totals, review-only example status, exclusion of AI replies, exclusion of failed sends, and rejection of foreign-account rows. Keyword matching was corrected after inspecting false positives for Alexandria as accommodation and a request for a phone number as an opt-out. The exporter completed against the selected live accounts with read-only queries. This verifies preparation behavior, not sales quality or live all-market reply behavior.

Six additional offline tests cover exact semantic-review coverage, omitted/duplicate chat rejection, evidence bounds, unsupported countries and the per-conversation schema. The full real-provider run has an independently checked receipt for all 22,678 conversation references. These checks establish processing coverage and source-reference integrity, not semantic correctness of every generated lesson.

After local integration, all six Python coverage tests and all six existing provider contract tests passed. The application and test projects built with the five existing warnings in the form-statistics view and Facebook webhook controller. The new instruction version could not receive an additional real-model reply evaluation because the provider credit balance was exhausted; no live deployment or performance claim was made.
