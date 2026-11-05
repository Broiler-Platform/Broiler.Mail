# Five M1 durable offline-progress items: Windows x64 evidence

Validated locally on 2026-11-03 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned local validation artifacts, not releases or real-provider certification.

- Shared Release suite: **1,195 passed**, including nine added durable-progress/storage cases.
- Windows reader/profile/keyboard/theme/remote-image/viewport suite: **87 passed**.
- NativeAOT protocol probe: loopback TLS and STARTTLS fixtures passed.
- NativeAOT/self-contained and framework-dependent application publish/smoke checks passed; both build manifests are retained beside this record.
- Dependency policy passed with 72 runtime libraries; renderer compatibility audit matched.
- No IL2xxx/IL3xxx warnings observed in protocol/application publish logs.
- `git diff --check` passed apart from the preexisting LF/CRLF notice for `docs/roadmap.md`.

New shared cases cover mid-walk reopen, durable completion and explicit reset across reopen, invalid/account-mismatched/duplicate/zero-UID checkpoints, selection compare rejection, canceled writes, corrupt progress isolation, checkpoint-save failure with body-preserving retry, failed Restart with completion preserved, stale epochs after reopen, concurrent account isolation, legacy no-rewrite loading, and partial/completed checkpoint backup/restore with cache-clear reset. UI tests at 100% and 200% text wait for the durable Restart completion notification before checking controls.

Published synthetic offline checks reopen the store and a reader model to verify continuation/completion without network work, verify checkpoint restoration with saved mail, and retain existing paging, reset, epoch and retry checks. Fixtures use example.test identities and local storage; no real mail is fetched or sent.

Ignored logs: `artifacts/m1-durable-offline-targeted.log`, `m1-durable-offline-shared.log`, `m1-durable-offline-windows.log`, `m1-durable-offline-protocol.log`, `m1-durable-offline-aot.log`, `m1-durable-offline-framework.log`.

See [completed scopes and compatibility](../../m1-durable-offline-progress.md). Complete-folder header synchronization, server sync checkpoints, attachment retention, matching ARM64/Linux certification and renderer process containment remain open. M0–M2 are not declared complete.
