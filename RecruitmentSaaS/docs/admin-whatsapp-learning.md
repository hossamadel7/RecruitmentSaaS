# Admin WhatsApp coaching and inbox transfer

Implemented locally on 4 November 2026. This is the first private-channel/AskAdmin release; it does not activate customer sending or replace historical learning.

## Using the inbox

An administrator opening **تحويل** now sees a **مساعد المبيعات بالذكاء الاصطناعي** group alongside human employees. Only agents matching the selected WhatsApp account and team are offered. Disabled setup, occupied capacity and an existing AI assignment prevent a second assignment; paused agents are labelled and remain paused after transfer. Setup links point to existing agent management.

Confirmation transfers **only this conversation**, retaining the existing lead's salesperson. Direct chats without a linked lead also work: assignment history permits a null LeadId without creating an artificial lead. Existing ownership versions, idempotency receipts, account/team checks, capacity, human takeover and release are reused. Admin-only AI assignment permissions remain unchanged; ordinary team roles keep their existing human-transfer permissions. Release from AI Sales restores the former active chat owner. Switching an already AI-assigned conversation requires releasing its existing assignment first.

Transfer is not an activation switch. Existing draft/automatic-dispatch permissions determine which work may run; no release settings were enabled by development. The confirmation explains this. Pause existing automatic agents before a deployment containing changed prompts.

## Private channel and first AskAdmin flow

Open **تدريب مساعد المبيعات → قناة المدير وأسئلة المساعد** (`/AiSalesTraining/AdminChannel`). Select the company sender account and bind the logged-in active administrator's own international phone number. Send the single-use, 15-minute link challenge from that number to the company WhatsApp number. Five failed attempts exhaust that challenge; renew it from the authenticated dashboard. Challenges are hashed in storage, excluded from saved message bodies, and never sent to the customer. No outgoing verification message is required.

Numbers with existing customer conversations on the selected account are rejected for binding; existing records are not silently hidden or reclassified. One channel per deployment tenant/account is supported. The first release deliberately disallows changing its phone or account owner in place; that needs a separately reviewed migration/rebinding procedure. Only an active role-1 user can configure the channel, and the verified channel owner must validate its answers.

Admin inbound messages are intercepted before contacts/conversations, welcome messages, lead linking, distribution, intake or voice processing. Private messages use separate tables and cannot enter customer conversion reports or historical customer analysis. Coexistence echoes/history for the reserved number are excluded from customer ingestion. This first release does not import historical Admin-device messages into private training history; live private messages sent/received by the new workflow are retained. An unrecognized number saying “I am Admin” remains a customer, with no training authority.

The `PrivateRoutingEnabled` switch is independent of learning and background work. **After binding a number, keep private routing enabled**, including while learning, sending, agents or the Admin account are disabled. Paused/unverified/disabled-user channels stay reserved. Do not roll back to an older binary that does not recognize private routing while the reserved Admin number can still send to the company account.

For an AI-owned, resumed conversation with a new textual customer question:

1. A missing-fact/human-review draft can create a durable clarification. Stop/contact-refusal, explicit requests for a human and uninterpreted media do not trigger this text coaching path. Administrators can also use **اسأل المدير** from the inbox or create a question from the private-channel page. This is a conservative draft-triggered gap detector, not a claim that every ambiguous/conflicting business fact is detected automatically.
2. The request stores the exact customer message, conversation, agent and ownership version, a redacted question, unique `AI-…` code and deadline. One request per exact customer message prevents duplicate notifications on repeated processing.
3. The background worker queues a question to the verified Admin. It uses the existing company sender/transport. Webhook handling never waits for a human or provider response.
4. The Admin replies to the question message or includes its code. A natural reply is accepted only with exactly one open question and no explicit unmatched reference. Conflicting references are never guessed. Duplicate webhook IDs are ignored. Nontext answers require written/dashboard clarification; no Admin-media transcription is performed.
5. The answer is stored **for review**, with no automatic publication. On the dashboard, the channel owner corrects the wording, chooses customer-only/general/temporary/correction, specifies expiry (at most 90 days), and explicitly chooses whether to publish. Customer-only answers cannot be published as company policy. Corrections can explicitly retire a selected existing scoped fact. Global facts use the existing `AiSalesKnowledge` approval/version/expiry/retrieval store; historical analysis is unchanged. Authoritative prices, availability and eligibility must still be verified in their governing system; coaching does not modify that system.
6. A resumable worker or **تجهيز مسودة العميل للمراجعة** reloads the exact customer turn and assignment. New customer messages, existing replies, human takeover, changed ownership or expired answers cancel preparation. A scoped Admin answer is added only to that customer's current question context; private discussion is never copied into the customer history. The resulting draft still needs explicit human review and the existing dispatch workflow. It does not send to the customer automatically.

`إيقاف التدريب` / `استئناف التدريب` (or `pause` / `resume`) controls coaching from the verified number. The dashboard has equivalent controls. General unsolicited coaching is saved privately with guidance to use an identified question; automatic interpretation of proactive instructions and an initial dynamic interview are later phases. Cross-customer semantic grouping, automatic customer resumption, conversational approval commands, automatic human notifications and sales-effectiveness reporting are also outside this first release.

## Messaging, failures and operations

Free-form questions require a valid Admin inbound timestamp within the existing conservative 24-hour window. Outside it, a configured Meta-approved notification template without parameters invites a reply; it contains no arbitrary question/customer details. Sending a template does not itself open a free-form window. Notification attempts are capped at one per channel per day. Template approval, recipient permission and correct language are deployment prerequisites, not inferred by the application. See [WhatsApp Business Messaging Policy](https://business.whatsapp.com/policy).

Outgoing private messages have durable queued/sending/sent/delivered/read/failed/unknown states. Interrupted or uncertain sends are **not automatically retried**, because the provider may have accepted them. Inspect delivery receipts and the private history before manually reconciling an unknown send. No exactly-once network-delivery claim is made. Duplicate processing cannot repeatedly prepare the same customer draft. Existing dispatch still checks customer sending windows and context freshness at review/send time.

Unanswered requests become visible **needs human follow-up** records after the configured deadline when processing runs. No policy is invented. Pausing/stopping processing also stops deadline processing; operate the dashboard/manual preparation or resume the worker to process those records. This first release has no customer acknowledgement send or automatic staff notification.

Private text is bounded/redacted before storage and questions mask known customer names/contact/address fields, but this is not guaranteed anonymization. Private training access is role-1 only, noncacheable and CSRF-protected. State transitions and decisions are audited without putting raw text in audit events. Operational private messages/answers persist until an authorized retention/erasure process removes them; the historical-learning retention worker does not purge these new operational records. Establish the retention schedule before enabling a live channel. No external model is used to parse Admin replies; normal draft generation still requires its existing privacy/provider permissions and funding.

## Configuration and migration

`AiAdminLearning` defaults entirely off: `Enabled`, `PrivateRoutingEnabled`, `BackgroundEnabled`, `OutboundApproved`, explicit `ApprovedAccountIds`, optional `NotificationTemplate`/`TemplateLanguage` (`ar`), `DeadlineHours` (24), `MaxRequestsPerDay` (30). Private learning and sending flags do not activate customer replies.

Apply `20261104145700_AiAdminWhatsAppCoaching` after `20261104133753_AiSalesContinuousLearning` before enabling private routing. It adds `AiAdminChannels`, `AiAdminClarifications`, `AiAdminMessages`, and relaxes the AI-assignment LeadId for direct-chat transfers. Existing human messages/leads are preserved. The supplied upgrade SQL is `docs/deployment/ai-admin-whatsapp-coaching.sql`. No production migration was run.

Enable private routing and the account allowlist first, bind/verify a synthetic Admin pilot, then test manual questions/reviews with outbound disabled. After separate operational approval, configure the approved notification template and enable Admin outbound/background work. Test with the real provider only after credentials are funded. Customer activation is a separate existing release process.

Rollback destroys private training records. Export required evidence first. The migration deliberately refuses Down while conversation-only assignments have null LeadId: preserve and remove those assignment records through a reviewed maintenance plan before restoring an older schema; do not invent placeholder lead IDs. Resolve private-number routing before rolling back the binary, as noted above.

## Validation

Automated tests cover direct-chat assignment/release, phone proof, paused/disabled privacy routing, webhook deduplication and isolation, out-of-order/ambiguous references, one-time exception scope, explicit publication/correction, restart-safe preparation, takeover/new-message cancellation, deadlines, template-only notifications, unknown-send suppression, Admin permissions/CSRF and migration rehearsal. Browser validation uses only `Tests/BrowserHost --admin-demo`, a disposable loopback database, fictional Admin/customer messages, synthetic draft provider and strict no-send transport.

The full regression suite passed **288 tests** with no failures/skips. After tightening delivery-receipt account scope, **22 targeted tests** also passed. Desktop/mobile browser flows passed with no page errors; screenshots are `docs/validation/ai-inbox-transfer.png` and `ai-admin-channel-{desktop,mobile}.png`. Build, JavaScript syntax, whitespace and EF model/migration consistency checks passed (five existing unrelated compiler warnings on a clean compilation). The temporary browser host was stopped. No production deployment, real provider calls or real WhatsApp sends were performed.
