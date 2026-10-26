# Inbox fixes and team monitoring — 22 October 2026

Deployed to the live CRM at https://thearabianfahd.com.

- Chat transfers preserve the new owner, update historical routing references and reject inactive automatic recipients.
- Switching a normal office booking to follow-up or waiting synchronizes the lead and chat and dismisses the obsolete appointment reminder.
- Role 9, **مراقب الفرق**, provides a read-only dashboard and access to all teams' conversations. Viewing a conversation does not clear the salesperson's unread count. Writes are rejected before request binding or conversation takeover.
- Migration `20261022030000_AllowTeamMonitoringRole` is installed. Administrators can select the new role when creating or editing users.

The self-contained Linux release DLL SHA-256 is `66a924cf6b46ddbd5f5e6c6fc27f8e176d5141ced8950b9ada456f0a141772cc`. The running installation matches the release manifest. The service is active, with zero startup error entries.

A fresh database backup and prior application were retained for recovery. Quiescent before/after comparisons verified original row counts and contents across all 99 existing tables, excluding the intentional new migration-history row. Configuration, existing uploads and login keys were preserved; the environment file was unchanged. No database rollback or customer reassignment was performed during installation.

Cloudflare cache purge succeeded. Eight public checks passed: health, login, protected monitoring/inbox/API redirects, and exact checksums for the monitoring script, inbox script and inbox stylesheet. Production checks did not impersonate a staff member. Authenticated monitoring permissions and workflow behavior were verified through the isolated integration tests before deployment.

Private recovery artifacts and verification reports are recorded outside Git in the local `2026-10-22-team-monitoring` release folder and its corresponding server installation directory. To migrate routing references left by an earlier transfer, repeat that user's transfer with the intended recipient through the admin interface.
