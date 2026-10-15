# Phase 4 — supervised open office consultations

This increment adds staff-reviewed consultation bookings to `/AiSalesAdmin`, compatible with `Lead.AppointmentDate`, status 5 (Booked), status 6 (Visited), internal reminders, the reservations report, and reception check-in. It does not send customer messages or expose an autonomous model booking tool. A reviewer saves a concrete booking only after checking customer evidence; a draft suggesting a consultation remains a draft.

## Approved office availability

An admin approves a policy for an existing active branch and WhatsApp account: working days, opening/closing time, reference, enablement, and expiry within 90 days. Times are Cairo local time; validity is an absolute UTC instant. Hours are a single daily interval on selected weekdays; overnight hours and holiday exceptions are not represented. No real office schedule was supplied or seeded in production. Disable the policy for a closure, or use human handling for an exception.

Consultations are open visits, without capacity reservations or exclusive slots. Several customers may choose the same approximate arrival. The policy must cover that future date and arrival minute, and remain valid through arrival. Opening is inclusive; closing exclusive. The default horizon is 30 days (configurable 1–90). Clock-change gaps and ambiguous Cairo times are rejected. API arrival timestamps must omit UTC suffixes and offsets.

## Booking lifecycle and customer evidence

1. Explicitly assign and resume an eligible Shadow conversation. A linked existing lead is required; its original branch, name, age, profession and assignment are preserved.
2. Review the customer's recent incoming confirmation message. Text is readable evidence; audio must have a staff-approved Phase 3 transcript. Media placeholders, staff messages, other conversations/accounts and evidence older than 24 hours are rejected.
3. Choose branch, Cairo date, approximate time and visit purpose. Confirm that the customer agreed to all four, and that general-consultation/no-confirmed-matching-vacancy disclosure was made. A checkbox records the operator's attestation; it does not independently establish that a conversation contained consent.
4. A transaction saves the booking, `Lead.AppointmentDate`, status 5, legacy funnel entry, internal reminder, booking event, audit record and idempotency reservation. Only a successful committed response displays a saved booking. Customer messaging remains manual in the inbox.
5. Rescheduling requires current booking/control versions and a different, fresh readable customer confirmation received after the last booking update. Because legacy message timestamps have whole-second precision, evidence must be more than one second after that update; the previous confirmation ID is explicitly rejected. It validates current office policy, dismisses the old reminder, stores a replacement and records history. It may be performed by an authorized reviewer after human takeover; original AI attribution remains the source of the booking.
6. Cancellation requires a reason and current versions. It dismisses the linked reminder, clears the matching appointment and moves status 5 back to 4 (Interested), retaining history. Cancellation remains available when the booking feature is disabled or the account is inactive. It never downgrades an attended/converted lead or silently overwrites mismatched legacy CRM state.

New bookings reject existing legacy appointments and ineligible, duplicate, visited, converted or lost leads. Operators must resolve those through the existing human workflow. An active AI booking also blocks the inbox outcome shortcuts that would replace its appointment or claim attendance; use the supervised booking actions and actual reception check-in. Other legacy admin screens are not comprehensively rewritten: conflicting changes are detected by booking actions and require human reconciliation.

The request key and payload fingerprint prevent duplicate booking side effects. Conversation advisory locks, a lead row lock, optimistic booking versions, and a unique active booking per lead protect concurrent booking/reception operations. Office policy changes serialize with booking validation. A takeover changes the ownership version and invalidates an in-flight stale booking request. Booking changes also alter Phase 2 context fingerprints, so earlier drafts become outdated.

## Reminders and attendance

The existing `FollowUpReminders` workflow receives one reminder addressed to the human reviewer. Its date is the day before arrival; the existing background service processes due reminders on its next 09:00 Cairo run. Same-day bookings made after that run may need manual follow-up. Rescheduling/cancellation/verified check-in dismiss the linked reminder. No automated WhatsApp customer reminder or template dispatch is introduced. A notification already dispatched cannot be recalled.

Reception uses its existing Role 2 CheckIn action. Saving the actual visit, lead status 6 and optional matching AI booking attribution occurs in one transaction under a lead row lock. Same-day check-in deduplication now uses the Cairo day. The service attaches the persisted `LeadVisit` only when branch and Cairo visit date match the active booking. Wrong-date, wrong-branch or missing reception evidence leaves AI attendance **unknown**, never an automatic no-show. Such evidence can still affect legacy lead status through reception; reconcile inconsistent records manually. No automatic matching of historical visits or attendance backfill occurs.

The reservations report displays a separate AI consultation section, scoped to deployment tenant, conversation visibility and selected team. It lists up to 500 bookings created in the selected date range and shows current booking state and the explicit visit evidence link ID. Supervised AI funnel entries are excluded from human booking totals, including when the reviewer is a TeleSales manager. Legacy human figures retain their previous semantics. AI attribution snapshots agent/team and does not create a fake user or human commission credit.

## Data, APIs and enablement

Migration `20261012152606_AiOfficeBookings` adds `AiOfficePolicies`, `AiOfficeBookings`, and `AiOfficeBookingEvents`. Events preserve date, branch, purpose, confirmation reference/fingerprint, actor and reason. Save/reschedule events identify the approved office policy/version; policy audit records include the approved policy snapshot. Retain those records alongside the customer evidence under deployment privacy/access rules.

APIs under `/api/ai-office-bookings` expose status, conversation bookings/history/eligible evidence, creation, rescheduling and cancellation. Mutations enforce CSRF and roles 1/7/8 with existing conversation visibility. Branch/policy administration is Role 1 only. No arbitrary model SQL, vacancy/salary promises, automatic WhatsApp sends or fixed-capacity calendar is added.

Default is disabled. Configure after staging validation:

```text
AiSales__Enabled=true
AiSales__TenantId=<deployment tenant GUID>
AiOfficeBooking__Enabled=true
AiOfficeBooking__MaxDaysAhead=30
```

Approve actual office policies through the admin UI. No provider key is needed for the supervised booking action; draft/voice external-processing gates remain independent. The legacy deployment tenant boundary from Phase 1 still applies, not a retrofit of every legacy table into shared-database tenant isolation.

## Migration, rollback and acceptance

No production database was changed. Rehearse the historical baseline and four additive phase migrations on a sanitized staging copy. Empty-table upgrade/down/reapply tests preserve human ownership and age. Do not run destructive repository scripts.

For rollback, first disable new booking/reschedule actions, resolve active consultations and reminders with staff, preserve booking/policy audit and attribution, and verify reception/report compatibility. Prefer retaining additive tables while reverting application code. Dropping populated tables would discard evidence and attribution; use a separately reviewed data migration or restore plan rather than the tested empty rollback.

Acceptance requires: no duplicate active booking or reminder under concurrency; no confirmed UI before commit; explicit customer evidence/disclosure; approved valid office hours; cancellation/reschedule preserve history; failed requests leave CRM unchanged; reviewed voice is required for audio evidence; original assignment/CRM data remain intact; human workflow remains intact; attended attribution requires matching reception evidence; absence of evidence never means no-show; AI activity does not inflate human booking totals.

Next is Phase 5 evaluation and release: approved real business facts and representative conversations/audio, replay scoring, shadow and supervised pilot, operational limits/monitoring, followed by separately approved controlled rotation and customer dispatch. Current tests establish mechanics, not sales quality, customer consent truth, acoustic accuracy, actual attendance rates or production performance.
