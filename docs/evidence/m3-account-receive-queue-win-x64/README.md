# Independent account receives — Windows x64

Local validation on 2026-11-04 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. This work postdates the earlier local CI snapshot. Everything remains local; no branch, PR, tag or release was published. No additional live provider account/message operation was performed in this follow-up.

Implemented [explicit independent account receives](../../m3-account-receive-queue.md): two FIFO receive slots, one outstanding page job per configured account, isolated status/cancellation, guarded scoped cache publication and application account-write coordination. No polling, sending, bodies or automatic queue replay.

The focused queue, account selection, storage and IMAP regression passed **189 tests**. The full shared Release suite passed **1,638 tests**, including 27 new cases. The full Windows Release suite passed **346 tests**.

Coverage includes enabled/disabled accounts, overlapping mailbox/epoch/UID values, unchanged selected account, progress past a held account, duplicate coalescing, per-account queued/active cancellation, configuration changes before/during receive, serialized config-write/cache commit ordering, malformed/foreign pages, provider/cache failure isolation and explicit retry. Stress cases cover 32 accounts, 256 cancel/requeue cycles with one bounded pending slot, replacement of a queued roster without capacity/late-result leaks, and coalesced UI notifications.

Real loopback TLS and STARTTLS adapters additionally verify separate account/IMAP credential bindings when one account rejects authentication and the other saves successfully. No SMTP credentials, body or mutation commands are used. UI/application cases cover named controls and bounded layout at 100%/200% text scale, safe cancellation focus, independent activity selection, pinned drafts during account switching, configuration-change cancellation, removal/maintenance exclusion, disabled automatic replay and application disposal.

Ordinary header pages now use bounded FETCH metadata responses. Additional real TLS fixture cases exceed the per-line size and response-line count limits and verify an isolated failed job, exactly one IMAP credential read and no cache publication.

An older selector fixture assumed the account area had only one combo box; it now identifies the main selector by its accessible name, preserving the keyboard and secret-isolation assertions. Native smoke uses a queued UI dispatcher and synthetic accounts: one held/canceled account, another successful account, an independent authentication failure followed by explicit retry, scoped cache reopening, reader/draft selection checks and maintenance refusal/release. Sender/body/credential-mutation methods throw if invoked.

The Windows x64 NativeAOT Storage, Protocol and NetSession probes passed. The [NativeAOT self-contained](self-contained-build-manifest.json) and [framework-dependent](framework-dependent-build-manifest.json) packages passed smoke and the 72-library dependency policy. Both are unsigned validation packages. OAuth, broader provider and target acceptance, continuous synchronization, renderer containment and native assistive technology remain open in the [M0–M4 audit](../../m0-m4-completion-status.md).
