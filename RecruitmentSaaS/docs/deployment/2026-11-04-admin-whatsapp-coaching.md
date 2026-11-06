# SalesAgent learning and private Admin WhatsApp installation

Deployed on 4 November 2026 to [the live CRM](https://thearabianfahd.com) after the owner's explicit deployment instruction. Source revision: `26abe94b2190f7d1c89a34bcdf61eda6b248c82e`, including the independent-review correlation and worker fixes.

## Installed

- Historical/incremental learning and review dashboard, semantic knowledge retrieval, evaluation and feedback.
- AI conversation transfer in the existing inbox, including direct chats without a linked lead.
- Private Admin channel, phone verification, persistent AskAdmin requests and preparation of human-reviewed customer reply drafts.
- Migrations `20261104133753_AiSalesContinuousLearning` and `20261104145700_AiAdminWhatsAppCoaching`, upgraded from the verified production baseline `20261102133819_AddArmeniaCountryRouting`.

The Linux x64 self-contained .NET 8 DLL SHA-256 is `7cd126e17a81959f16b144128e1beef12941cf7a9d1fab1c1094f20286133291`. The installed binary matches the release package. EF reported no pending model changes, and all **292 tests** passed before publishing.

## Preservation and verification

A fresh full PostgreSQL custom-format backup was captured while the CRM service was stopped. Aggregate original-column row counts and digests matched before/after across **102 existing tables**, excluding the intentional new migration-history rows. No database Down migration or restored overwrite occurred. Existing appsettings, uploaded files and login key files retained their hashes. The prior full application, environment file, service definition, key ring, backup checksum and verification reports are retained in private recovery locations.

The restarted service is active. Database history reaches the expected Admin coaching migration, with zero unvalidated CRM constraints. Six internal HTTP checks passed: health, login, protected Admin page/API redirects, and both inbox/Admin JavaScript assets. Eight public HTTPS checks passed: health, login, protected training/Admin page/API redirects, and exact checksums for inbox, training and Admin-channel JavaScript. Origin HTTPS health also returned 200. Cloudflare cache purge succeeded. The post-installation recent service-error check returned zero entries. These checks do not impersonate a staff member or constitute authenticated production workflow acceptance.

## Pilot configuration

Production previously had AI automatic replies enabled. As communicated during this deployment, `AiSalesEngine__AutoReplyEnabled` and AI dispatch/release/automatic gates are now explicitly **false** for the controlled pilot. New historical-learning and Admin-channel/routing/background/outbound gates also remain **false**. Their values were independently verified in the running process. Existing secrets, tenant/account bindings, manual draft configuration, human inbox workflows and agent assignments were retained; no Admin number was bound or AI agent activated by deployment.

Next configure the intended account and verify the Admin's own WhatsApp number, then test one assigned conversation with customer sending disabled. Admin notifications/background work and later customer activation remain separate steps. The first AskAdmin release prepares review drafts; it does not automatically send Admin-grounded answers to customers. See [setup and limitations](../admin-whatsapp-learning.md).

## Private recovery pointers

Local release/evidence: `%LOCALAPPDATA%\RecruitmentSaaS\ReleaseCandidates\2026-11-04-admin-coaching-26abe94`.

Server installation evidence: `/home/ubuntu/.local/share/recruitmentsaas-installations/20261104-admin-coaching-26abe94-6c60565d`.

Previous application: `/var/www/recruitmentsaas-releases/20261104-admin-coaching-26abe94-6c60565d-previous`.

Recovery material contains private operational data and configuration; do not publish its contents. Routine recovery should restore compatible code while retaining additive schema and keeping AI sending off, rather than dropping populated learning tables. Once a private Admin number is bound, retain its private routing protections when considering any older binary.
