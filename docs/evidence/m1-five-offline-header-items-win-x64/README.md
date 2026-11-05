# Five M1 selected-folder header items: Windows x64 evidence

Local validation on 2026-11-04 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned validation artifacts, not releases/provider certification.

- Shared Release suite: **1,375 passed**.
- Windows reader/profile/keyboard/theme/remote-image/viewport suite: **87 passed**.
- **31 new header-workflow cases** are included in the full suite, including the canceled-commit/new-retry race and bounded 100%/200% control layout.

- NativeAOT TLS/STARTTLS production protocol probe: **both passed**.
- NativeAOT/self-contained and framework-dependent application publish/smoke: **passed**. Retained [NativeAOT manifest](self-contained-build-manifest.json) and [framework-dependent manifest](framework-dependent-build-manifest.json).
- Dependency gate: **72 runtime libraries**; renderer compatibility audit matched.
- No IL2xxx/IL3xxx warnings in protocol or either application publish log.
- `git diff --check` passed apart from the preexisting LF/CRLF notice for `docs/roadmap.md`.

Coverage includes bounded multi-folder continuation beyond 500 loaded rows, exact scope/order/epoch/cursor validation, partial failure and failed cache commits, safe retry including a held committed save across cancellation/new attempt, canceled/account/folder/maintenance/disposed late results, queued stale callbacks, session resets, INBOX-only hosts, empty folders, disabled-account reopen, explicit local display and 100%/200% cancellation focus.

Published smoke uses synthetic mail through the actual header view model and verifies the header control, two-folder Next, local display without another receive, zero body downloads, disabled-account restart, verified backup/restore and recovery-backed cache-clear reset. Production protocol probes separately cover the reused read-only TLS/STARTTLS adapters. No real accounts or provider certification are involved.

Ignored logs: `artifacts/m1-offline-headers-build.log`, `m1-offline-headers-targeted.log`, `m1-offline-headers-targeted-new.log`, `m1-offline-headers-shared.log`, `m1-offline-headers-windows.log`, `m1-offline-headers-protocol.log`, `m1-offline-headers-aot.log`, `m1-offline-headers-framework.log`.

[Scope and remaining work](../../m1-offline-header-acquisition.md). Header cursors remain session-only; durable server checkpoints, authoritative/background sync, broader provider/target/accessibility acceptance and renderer containment remain open. M0–M3 are not declared complete.
