# Five M4 saved-recipient items: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Packages are unsigned
local validation artifacts, not releases or real-provider certification.

- Shared Release suite: 1,090 passed, including 25 added recipient/store/UI/lifecycle cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT TLS/STARTTLS protocol probe passed existing discovery/read, flags,
  verified UID MOVE/reverse/delete/uncertainty and SMTP/private-Bcc fixtures.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published profile smoke executes saved-recipient source-generated JSON round trips,
  account isolation, local suggestions, Bcc insertion/autosave, verified backup and
  separate-profile restore, and removal without changing saved draft recipients.
- Published account-switch smoke preserves the draft's original recipient scope
  while switching the selected inbox account, then renders its composer controls.
- Published corrupt-book smoke reports the error, keeps the file unchanged and
  permits manual draft editing/saving. A shared late-load check also covers closing.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in application or protocol publish logs.

Added cases cover concurrent updates, account isolation, case-insensitive upsert/
removal, validation/header-injection refusal, corrupt/future/duplicate files without
reset, cancellation/limits, bounded prefix matching, no Bcc harvesting, all three
insertion destinations, preserved identity/thread/content/other fields, invalid-field
and foreign-entry refusal, submitted-state refusal, late-load rejection, UI Bcc
field display, and backup/restore/cache-clear behavior. All fixture addresses and
profile data are synthetic. No real email is sent.

Ignored logs: `artifacts/m4-five-recipient-items-shared.log`,
`m4-five-recipient-items-windows.log`, `m4-five-recipient-items-protocol.log`,
`m4-five-recipient-items-aot.log`, and `m4-five-recipient-items-framework.log`.
Both application build manifests are retained alongside this document.

See [five completed scopes and limits](../../m4-saved-recipients.md).
Multiple simultaneous autosaved drafts, an outbox, inline type-ahead replacement,
contact synchronization/import, broader accessibility and other-target certification
remain outside this increment; other M0–M4 gaps remain open.
