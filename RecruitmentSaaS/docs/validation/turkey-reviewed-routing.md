# Reviewed reply routing — 14 October 2026

Follow-up to the [owner v2 evaluation](turkey-owner-v2-evaluation.md). The draft service now calls a shared local policy before the external provider. These outputs remain human-review drafts: the policy does not send a message, persist an opt-out, create an appointment, assign a job or determine employment eligibility.

- Recognized refusal/stop phrases produce a short `stop` draft with `needsHuman=true`. Stop takes precedence when a message also asks for a person.
- A recognized person request produces a short `handoff` draft without another qualification question.
- A Friday office-visit request requires staff review. Friday closure is stated only when the supplied current Fact explicitly contains a supported closure declaration about the office. Playbook text, expired Facts, prior messages and negated closure statements do not authorize that statement. Otherwise the draft asks staff to review the schedule. This is intentionally narrow language matching, not a general calendar parser.
- Saudi enquiries and driving-job availability questions are routed to staff without claiming that the company has no Saudi jobs or promising a driving position or a later transfer.
- Individual matching questions containing “ينفع” are conservatively routed to contracts staff, excluding ordinary office-visit and information questions. This does not infer an eligibility result from age or nationality. It is not a comprehensive intent classifier.

All local routes have `needsHuman=true`, so the existing reviewed-dispatch gate excludes them. Existing intake/control handling and staff action remain necessary. Ordinary information questions still use approved knowledge and the provider. The prompt is now `egyptian-sales-draft-v7-reviewed-routing` and emphasizes preserving offer scope without inventing alternative opportunities.

The focused actual trial completed 11 fictional cases: five local drafts and six `gpt-4.1-mini` calls. The local cases corrected the Friday, stop, person, Saudi and driving failures observed earlier. Provider replies used updated salaries, fees and documents; continuing salary and fee questions did not repeat the greeting. One salary reply had awkward wording, and the nationality-scope answer still appended an office invitation. The subsequent individual-matching fallback covers that question through staff review; an integration test verifies provider bypass without another paid model run. Human review of final wording is still required.

The final full isolated regression run passed **148 tests**. The 13 new routing tests cover provider bypass, review status, no outgoing messages/intents/bookings, stop precedence, Fact expiry/type and absence of unsupported Friday-closure assertions. The local test cluster was stopped after verification.

Raw results and separate coding-agent assessment remain outside Git, identified by `%LOCALAPPDATA%\RecruitmentSaaS\AiSalesTrials\latest-routing-v7-path.txt`. No real CRM chats were uploaded, no fine-tuning occurred, and no live server changes were made. This focused trial does not qualify a live pilot. The release candidate must be rebuilt after these source changes; backup restoration, staging rehearsal and actual reply review remain pending.
