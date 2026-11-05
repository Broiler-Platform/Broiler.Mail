# Five M3 account downloaded-mail cleanup items: Windows x64 evidence

Local validation on 2026-11-03 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned validation artifacts, not releases or real-provider certification.

- Shared Release suite: **1,229 passed**, including 13 added cleanup/storage/UI cases.
- Windows reader/profile/keyboard/theme/remote-image/viewport suite: **87 passed**.
- Focused cleanup/account-selection suite: **30 passed**; focused Windows profile smoke: **2 passed**.
- NativeAOT loopback TLS and STARTTLS protocol fixtures passed.
- NativeAOT/self-contained and framework-dependent application publish/smoke checks passed; manifests are retained alongside this record.
- Dependency policy passed with 72 runtime libraries and renderer compatibility audit matched.
- No IL2xxx/IL3xxx warnings observed in application/protocol publish logs.
- `git diff --check` passed apart from the preexisting LF/CRLF notice for `docs/roadmap.md`.

Added coverage includes per-account counts across old epochs, no-write inspection, scoped source/index deletion, retained other-account data and valid indexed searches, unchanged drafts/configuration/recipients/navigation/credential stand-in, target-only checkpoint reset, verified recovery restore, cancellation after backup, existing destination, unresolved/corrupt mutation journals, invalid/pre-canceled account requests and injected SQLite deletion rollback with recovery backup kept. UI/shell cases at 100%/200% text verify Keep-first explicit confirmation, non-default clear, blocked switch/removal, journal-flushed retained draft, reader reload and other account's saved body; failure/pre-cancellation release maintenance and preserve mail.

Published synthetic smoke exercises actual application composition with two accounts, an unsent draft and private in-memory credential store. Inspection and confirmed cleanup retain account/draft/password and the other account's body/index/checkpoint, then restore the recovery snapshot. No real mail/server or user credentials are touched.

Ignored logs: `artifacts/m3-account-mail-cleanup-targeted.log`, `m3-account-mail-cleanup-shared.log`, `m3-account-mail-cleanup-windows-targeted.log`, `m3-account-mail-cleanup-windows.log`, `m3-account-mail-cleanup-protocol.log`, `m3-account-mail-cleanup-aot.log`, `m3-account-mail-cleanup-framework.log`.

[Scope and remaining work](../../m3-account-downloaded-mail-cleanup.md). Orphan-account local data controls, broader process-kill/power-loss/provider recovery, matching ARM64/Linux certification and renderer containment remain open. M0–M3 are not declared complete.
