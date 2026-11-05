# Five M1 selected-folder offline items: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Packages are unsigned
local validation artifacts, not releases or real-provider certification.

- Shared Release suite: 1,154 passed, including 20 added selection/store/batch/UI/lifecycle cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT TLS/STARTTLS protocol probe passed existing discovery/read, flags,
  verified UID MOVE/reverse/delete/uncertainty, alias/pinned-name SMTP and private-Bcc fixtures.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published profile fixture persists selected INBOX/Archive folders, downloads their
  saved-header bodies explicitly with no discovery/header receive, constructs the
  controls, verifies backup/separate-profile restore, unselects without deleting
  mail and reads cached Archive mail with the account disabled and no extra network.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in application or protocol publish logs.

Added cases cover account-partitioned persistence/concurrent updates, literal folder
IDs without filesystem interpretation, duplicate/control/empty/length/count/Unicode
validation, pre-cancellation, corrupt/future files without reset, mail remaining
usable, current saved-header batches across overlapping UIDs, cached-body skipping,
partial failure/retry, cancellation/account switching with uncancellable late bodies,
epoch changes, the 500-header cap and missing coverage, unselection and disabled
accounts, late preference loads, backup/restore/cache clear and named disclosure/
Cancel focus at 640×480 with 100%/200% text. All account/profile/transport data is
synthetic. No real mail is sent.

Ignored logs: `artifacts/m1-five-offline-folder-items-shared.log`,
`m1-five-offline-folder-items-windows.log`, `m1-five-offline-folder-items-protocol.log`,
`m1-five-offline-folder-items-aot.log`, and `m1-five-offline-folder-items-framework.log`.
Both application build manifests are retained alongside this document.

See [five completed scopes and limits](../../m1-selected-offline-folders.md).
This is saved body-preview coverage, not a whole-folder mirror. Larger-folder
batch paging, header acquisition/sync across folders, general attachments, broader
native accessibility, real-provider/fault/power-loss acceptance and other-target
certification remain open. M0–M3 are not declared complete.
