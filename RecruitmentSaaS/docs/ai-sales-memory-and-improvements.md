# Customer memory and continuous improvement

Implemented locally on 13 October 2026 while the owner, CEO and sales lead review the historical reply sheet. These features do not depend on completion of that reply review and do not enable customer dispatch, model training or live deployment.

## Reviewed customer memory

The existing customer summary now has structured fields for passport readiness, preferred work, the main objection, the next reviewed step and a visit preference. Staff explicitly reviews and saves these fields. They remain scoped to that conversation and tenant; they do not overwrite the lead's name, age, profession, status or booking records. Passport readiness is a state, never a passport number or document image.

The API uses the existing conversation lock and expected memory version. Concurrent or stale edits conflict. Omitting structured fields preserves them for older summary-only clients; omitting the summary preserves it. Explicit blank fields can clear incorrect existing memory without resetting the version. Updates invalidate drafts produced from older context. Phone numbers and email addresses are redacted from model-visible memory using existing redaction rules; this is not complete anonymization.

Customer preferences and visit notes are context, not current company facts or proof of a confirmed booking. Lead fields collected by intake remain authoritative. Malformed stored details fail closed. The additive `20261013114423_AiStructuredCustomerMemory` migration adds a JSONB column with an empty-object default; existing reviewed summaries and versions are preserved. No migration was applied to production.

## Improvement review queue

The admin screen lists the 20 newest reviewed, rejected or failed drafts for the selected account and configured tenant. It shows only supported outcome evidence: a matching verified reception visit, a booking with attendance unknown, a cancellation with attendance unknown, or unknown. A verified visit must have a real referenced reception row, matching lead/branch/day, be at or after the draft and not in the future. Outcomes are associated with the same conversation and agent, not proof that the draft caused the visit. Missing attendance is never labeled a no-show.

Staff selects a source case and enters an anonymized example, proposed approach and reason, with an explicit privacy check. Source fingerprints reject changes since viewing. Idempotency keys avoid duplicate submissions and reject different content under the same key. The suggestion creates a **Draft Playbook** entry with seven-day initial validity and a provenance reference. It does not create an approved Fact, customer message or booking. A separate existing knowledge approval is required before retrieval uses it. Suggestions must be general approaches; new salaries, fees or vacancies require the fact-review workflow.

Both improvement routes are admin-only with CSRF protection. Customer memory preserves existing admin/manager visibility rules. This does not grant CEO or sales-lead CRM access; they chose to provide suggestions through this chat.

## Validation

- 132 tests passed in the full isolated regression run, including four improvement tests and five initial memory tests.
- An additional migration check passed after that run, proving the new column preserves an existing reviewed summary, reviewer and version.
- EF reports no pending model changes; the seven-migration upgrade script was regenerated.
- Browser checks saved synthetic customer memory and submitted a synthetic improvement that remained pending approval. No real customer data or external provider was used in these checks.
- A Linux x64 self-contained release candidate was built locally; it has not been run on staging or installed on the VPS. Existing unrelated nullable/view warnings remain.

Screenshots: [memory](validation/customer-memory-browser.png) and [improvement review](validation/customer-improvements-browser.png). The disposable browser app and local test cluster are stopped after verification.
