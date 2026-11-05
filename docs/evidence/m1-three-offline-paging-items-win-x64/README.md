# Three M1 offline paging items: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Packages are unsigned
local validation artifacts, not releases or real-provider certification.

- Shared Release suite: 1,172 passed, including eight added continuation/store/UI cases.
- Two focused restart/maintenance guards passed after refreshing the maintenance
  notice when it resets session cursors; Restart also clears the prior batch error.
- Focused Windows reader/profile/keyboard/theme/remote-image/viewport suite: 86 passed.
- NativeAOT TLS/STARTTLS protocol probe passed existing discovery/read, flags,
  verified UID MOVE/reverse/delete/uncertainty, alias/pinned-name SMTP and private-Bcc fixtures.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published profile smoke seeds 501 real cached headers, downloads the first 500,
  verifies the Next control and last body in a second batch, restarts without
  refetching saved bodies, rejects changed epoch before body requests and downloads
  the new epoch only after explicit Restart. Existing backup/offline controls,
  disabled-account reading, viewport/raster and resource-policy checks remain passing.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in application or protocol publish logs.

Added cases cover a 1,001-row folder and overlapping folder UIDs across three
batches, skipping finished folders, restart with cached-body reuse, older-page
failure and cancellation preserving cursors/committed bodies, epoch refusal and
restart, selection/reload/maintenance/account reset, folder navigation between
batches, and Next/Restart UI state at 100%/200% text. No real mail is sent; all
account/profile/transport data is synthetic.

Ignored logs: `artifacts/m1-three-offline-paging-items-shared.log`,
`m1-three-offline-paging-items-restart-guards.log`,
`m1-three-offline-paging-items-windows.log`, `m1-three-offline-paging-items-protocol.log`,
`m1-three-offline-paging-items-aot.log`, and `m1-three-offline-paging-items-framework.log`.
Both application build manifests are retained alongside this document.

See [three completed scopes and limits](../../m1-offline-batch-paging.md).
Cursors are session-local; saved bodies and folder selections remain persistent.
Whole-folder header acquisition/sync, persistent continuation checkpoints, original
MIME/general attachments, real-provider/fault/power-loss acceptance, broader native
accessibility and other-target certification remain open. M0–M2 are not complete.
