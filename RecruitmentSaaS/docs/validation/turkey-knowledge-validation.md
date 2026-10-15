# Turkey knowledge integration — 13 October 2026

**18 targeted integration tests passed**, none skipped: the sixteen supplied question scenarios, account isolation/retirement and unpublished-pack classification. Application and test-project incremental builds succeeded. These new tests change no deployed application code or schema.

The actual production context reader and draft service loaded the six owner Fact groups and two Playbook entries into a disposable test account for every question. The salary range, after-deductions wording and exact supplied map URL survived context redaction. A mocked provider received the scoped context for provider-backed paths; local opt-out/human-request paths bypassed it. Drafts remained review-required and repeated requests did not invoke the mock twice. No scenario created an outgoing message, booking or dispatch intent.

The pack was inaccessible to a different WhatsApp account, even when queried with the first tenant ID. Retiring its salary fact made a previously prepared draft stale and prevented acceptance. The source pack still has no real tenant/account/branch/reviewer or publication dates, keeps live approval false, and is rejected by the synthetic-business runner rather than silently misclassified as fictional company data.

The fixture uses only the dedicated local PostgreSQL instance and deletes its disposable database. The process was stopped after validation. Production settings and databases were not loaded. No OpenAI/Meta call, customer message, passport upload or historical customer-data transfer occurred.

These checks do **not** measure semantic reply quality, real model behavior, employment eligibility or external validity of the owner's claims. [Reply examples](../knowledge/turkey-reply-preview.md) are manually written for review, not model output. The real-model run remains pending secure provider/model configuration and approved isolated execution. No production knowledge import or release was performed.
