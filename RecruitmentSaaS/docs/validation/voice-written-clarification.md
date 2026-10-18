# Written clarification after voice — 19 October 2026

An incoming voice recording without reviewed transcription now produces the acknowledgement draft:

> ممكن توضح طلبك بالكتابة عشان نقدر نساعدك بدقة؟

It does not require a staff handoff. It still requires staff approval and explicit sending through the existing scoped dispatch flow. Audio is not downloaded, transcribed or interpreted by this fallback. Other unsupported media retains its prior handoff behavior. Reviewed transcriptions and genuine requests for a person keep their existing routes.

Ten focused tests passed, including audio clarification queued/sent only after approval through a fake transport, image handoff remaining blocked, and stop/person requests retaining their routing. Initial new-test failures were caused by omitting assignment in the synthetic setup; corrected before the passing run. No model calls or real WhatsApp sends were performed by these tests.

The release changes application code only, with a private prior-DLL recovery copy and health-checked rollback. Existing approved drafts are not rewritten; request a fresh draft to use the new behavior.
