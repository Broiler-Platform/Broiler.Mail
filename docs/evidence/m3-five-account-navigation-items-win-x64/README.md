# Five M3 account navigation items: Windows x64 evidence

Local validation on 2026-11-03 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned validation artifacts, not releases or real-provider certification.

- Shared Release suite: **1,216 passed**, including 21 added navigation/view/storage cases and the canceled-restore regression.
- Windows reader/profile/keyboard/theme/remote-image/viewport suite: **87 passed**.
- NativeAOT protocol probe: loopback TLS and STARTTLS fixtures passed.
- NativeAOT/self-contained and framework-dependent application publish/smoke checks passed; build manifests are retained beside this record.
- Dependency policy passed with 72 runtime libraries; renderer compatibility audit matched.
- No IL2xxx/IL3xxx warnings observed in protocol/application publish logs.
- `git diff --check` passed apart from the preexisting LF/CRLF notice for `docs/roadmap.md`.

Coverage: missing-file defaults without a write, atomic concurrent account updates, literal mailbox/path handling, invalid Unicode/control/length/duplicate/count/account validation, canceled writes, explicit start/pin/unpin/reset/reload, two-account overlapping folders, disabled-account offline navigation, corrupt/future-state isolation, failed preference saves, late read/write results, canceled restore with ordinary navigation released, and wrong-account responses, checksum-verified backup/restore, cache-clear preference retention and corrupt-state cleanup refusal. UI checks exercise the collapsed/bounded controls, pin click completion, explicit shortcut opening and cleared account choices at 100%/200% text.

Published synthetic smoke covers two accounts, start restoration with no body selection/network calls, disabled account handling, visible controls, unpin, verified backup/restore and cache clear preserving preferences, reset and Reload. Source-generated JSON is exercised in the published binary.

Ignored logs: `artifacts/m3-account-navigation-targeted.log`, `m3-account-navigation-shared.log`, `m3-account-navigation-windows.log`, `m3-account-navigation-protocol.log`, `m3-account-navigation-aot.log`, `m3-account-navigation-framework.log`.

[Scope and limits](../../m3-account-navigation.md). Unified inbox, background scheduling, OAuth/provider certification, matching ARM64/Linux certification and renderer containment remain open. M0–M3 are not declared complete.
