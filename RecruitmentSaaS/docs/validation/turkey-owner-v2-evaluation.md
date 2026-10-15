# Turkey reply evaluation — 14 October 2026

The owner confirmed the numbered reply revisions as current company facts. The prepared knowledge pack is now `alfahd-turkey-owner-v2`; it remains unimported and customer dispatch remains disabled. These are owner claims, not independent verification of employment or immigration requirements. The two blank reply items, 10 and 19, remain unanswered.

The evaluation criteria were updated before the first trial for worker/skilled monthly salary bands, fees in Egyptian pounds, passport plus ID plus photo, 10 AM opening, eight work hours plus a break, overtime and role-specific language requirements. Existing assistant disclosure, conversation continuity, stop and human-control requirements remain.

## Checks completed

- The final targeted local run passed 30 tests: 22 knowledge/retrieval checks, six provider contract checks and two new invalid stop/handoff regression cases. Local tests used a dedicated PostgreSQL instance and fictional accounts. They do not assess generated wording.
- The first actual `gpt-4.1-mini` trial generated 20 drafts with prompt v5. A focused retest generated 14 drafts with `egyptian-sales-draft-v6-owner-facts-scope` after tightening scope and handoff wording. Both used fictional applicant inputs, the prepared owner knowledge and no CRM history.
- All 34 outputs referenced only supplied Fact IDs. Citation validity does not establish that every claim in a reply is supported.
- The updated salary ranges, fees, documents and work hours appeared in replies. Continuing salary/fee chats avoided another greeting, and the identity question received an honest virtual-assistant answer.

## Release blockers found

| Case | Observed issue in focused trial |
| --- | --- |
| TR04 | Assistant Turkey-only scope became a claim of no Saudi vacancies for the company; an unsupported office invitation remained. |
| TR09 | Friday closure was omitted and the reply offered booking confirmation. No actual booking was performed. |
| TR13 | The current Egyptian-only offer scope was stated but unsupported future options and a visit invitation were added. |
| TR15 | The raw provider returned `stop` with `needsHuman=false` and a greeting. In the CRM, the existing local opt-out route bypasses the model, and the draft validator rejects an invalid stop/handoff flag. New tests confirm rejection creates no outgoing message, outbound intent or booking. |
| TR18 | The reply implied driving availability beyond the fact requiring contracts staff to confirm a matching vacancy. |

The raw provider runner does not exercise the full CRM control/booking path. Its output is not an approved draft, a saved appointment or a production quality result. Coding-agent inspection is recorded separately from CEO/human scoring, which remains pending. A further prompt revision alone should not be assumed to solve these failures.

Detailed raw replies, usage and input hashes are retained privately outside Git under `%LOCALAPPDATA%\RecruitmentSaaS\AiSalesTrials`. `latest-owner-v2-path.txt` and `latest-owner-v2-focused-path.txt` identify the two runs; the focused run contains `agent-assessment.json`. No API credential is included. No actual customer conversations were uploaded, no customer messages were sent, and model weights were not fine-tuned.

Next work is to repair and re-evaluate the failed cases, review actual model wording, then rehearse the full CRM on staging with the approved knowledge and correct branch schedule. The 13 October release candidate predates prompt v6 and must be rebuilt and requalified before use. Live deployment, backup restoration and supervised pilot evidence are still pending.

Evaluation keeps input cases and explicit criteria separate from output inspection, consistent with [official OpenAI evaluation guidance](https://developers.openai.com/api/docs/guides/evaluation-getting-started).
