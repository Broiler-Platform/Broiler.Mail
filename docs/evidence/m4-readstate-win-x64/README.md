# M4 Windows x64 explicit read-state validation

Validated locally on 2026-11-02 against the dirty working tree. These are unsigned
local artifacts, not published releases or real-provider certification.

- Shared Release: 850 passed, including 19 added protocol/model/cache/control cases.
- Focused Windows reader/profile/keyboard/theme/remote-image checks: 79 passed.
- NativeAOT protocol probe passed TLS and STARTTLS. It first verifies that ordinary
  folder/body reads issue no SELECT/STORE, then explicitly marks read and unread,
  verifies each result and tests a dropped STORE acknowledgement as Unknown with
  exactly one STORE attempt. No CLOSE/EXPUNGE is issued.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
  The fresh synthetic profile/process-restart fixture drives a synthetic confirmed
  writer through the real application model and SQLite read-state update path,
  restoring unread state and preserving its body and other mailbox data.
- Dependency policy: 72 runtime libraries. Renderer package audit matched.
  No IL2xxx/IL3xxx warnings were observed in the final application publish logs.

New checks cover epoch/permission/read-only/missing-message refusal; incremental
read/unread updates and no-op confirmation; rejection/lost/mismatched/removed
verification; stale-result/cancel handling; exact cache row isolation and unchanged
receive time; confirmed server state despite cache-save failure; and reader control
access/focus at 640x480 with 100% and 200% text size.

The initial extended profile fixture omitted the optional folder-receiver capability
and correctly could not mutate its Archive selection. After adding that capability
to the synthetic fixture, focused Windows checks and both package smokes passed.

Logs remain in ignored artifacts/m4-readstate-shared.log,
m4-readstate-windows.log, m4-readstate-protocol-aot.log, m4-readstate-aot.log and
m4-readstate-framework.log. See [M4 progress](../../m4-folders.md) for limits.
Starred-state controls, movement/deletion, durable operation history, broader
provider/native accessibility acceptance and other-platform certification remain open.
