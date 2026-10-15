# Read-only server findings — 13 October 2026

Connected to the owner's supplied OVH SSH host using its existing saved key and strict host-key verification. No server configuration, deployment, services or database objects were changed. PostgreSQL queries used `default_transaction_read_only=on`, statement timeouts and bounded lock waits. No password, API key, environment-file contents or private SSH key were displayed.

## Deployment

The live service is `recruitmentsaas.service`, running as `www-data` in `/var/www/recruitmentsaas`. Its active database is PostgreSQL 16, `RecruitmentCRM`, reached through the deployment's environment configuration. Credentials remain in its existing server configuration. `/etc/recruitmentsaas/app.env` is the service environment file and must be preserved during rollout.

The deployed application is self-contained .NET 8 (runtime config reports included framework version 8.0.16). The server's shared runtime listing only reports .NET 9. A replacement artifact must preserve a compatible self-contained runtime or use a separately reviewed runtime setup; an ordinary framework-dependent .NET 8 publish is not established as runnable there.

Migration history includes the expected last pre-AI migration, `20261011231817_AllowLeadStatusInTurkey`. Existing WhatsApp tables are present. New AI agent/knowledge/voice/fact-candidate tables are absent. Foundation, chat learning, external draft processing and AI dispatch are disabled/unconfigured in effective settings. The six new migrations have **not** been applied. Matching migration history is not a complete schema-drift or production-load rehearsal.

No obvious backup candidates were found in the limited directories inspected; that does not establish that backups do not exist elsewhere. Fresh backup/restore evidence remains required before a live rollout. No backup or migration was performed in this read-only step.

## Authorized history preparation

The owner chose to exclude `testttt`. Selected accounts are Bakar, Mekky and Sherif. The initial count query reported 4,876 conversations and 52,179 messages across them. A subsequent single-query export snapshot contained **52,195 rows**, reflecting intervening live arrivals: 48,980 text messages, 2,218 audio messages and 997 other message types. The export did not restrict history to open or AI-owned chats.

A local review JSONL and manifest were saved under the current user's LocalAppData `RecruitmentSaaS/ChatLearningReview` directory, outside Git. Media content was not downloaded or interpreted. Common contacts/names/numeric identifiers/links/emails were masked before export left the server process. Conversation IDs were pseudonymized per export; internal message IDs remain for review provenance. Unknown personal details can still remain, so this is restricted unreviewed material, **not guaranteed anonymization or an upload-ready training dataset**.

All rows remain training-unapproved. 30,618 text rows are eligible only for manual examination, including customer questions; this count is not a count of approved facts or assistant targets. System/AI responses and failed/unconfirmed outgoing messages are flagged rather than trusted. No OpenAI/Meta upload or model training occurred, and no historical claims were published into live knowledge.

Initial market scanning is keyword-based review assistance, not validated classification: 621 conversation groups mention Turkey and 577 mention Turkey without a Saudi keyword. Mixed or unlabelled groups still need human market/context review. These counts must not be treated as confirmed Turkey recruitment outcomes.

The first owner question concerned documents for the **first office visit**: source messages variously say passport only, passport plus a personal photo, or passport plus an identity card. Its source references and masked excerpts are stored in the private local review directory; no customer details or corpus text were added to the repository. On 2026-10-13 the owner corrected the requirement to passport plus either a personal photo or an identity card. This supersedes the earlier passport-only answer in the prepared knowledge and response checks. Nothing has been published to the live CRM. No historical version was chosen automatically.

## Next boundary

Ask the owner one fact question at a time and record their current answer. Preserve their office-referral rule for fees and other unspecified details. Before publishing to the actual application, recheck source/version and the selected account boundary, rehearse/apply the reviewed schema/code rollout with backup evidence, then configure the learning feature without enabling external drafting or AI dispatch. Actual reply generation needs provider setup; bulk fine-tuning and historical audio interpretation remain separate work.
