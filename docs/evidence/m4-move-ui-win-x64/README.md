# M4 Windows x64 reader move validation

Validated locally on 2026-11-02 against the dirty working tree. These unsigned
local packages are not releases or real-provider certification.

- Shared Release suite: 908 passed (19 new cache/model/reader cases).
- Focused Windows reader/profile/keyboard/theme/remote-image checks: 79 passed.
- NativeAOT/self-contained and framework-dependent application publish/smoke pass
  with the 72-library dependency policy and renderer package audit. No
  IL2xxx/IL3xxx warnings were observed in the final publish logs.
- New synthetic mover/profile smoke drives Archive through the actual application
  model and SQLite cache. A new model/cache instance then finds the source empty
  and opens the moved header/read/star state and rekeyed body offline at the new UID.
  The earlier UID MOVE NativeAOT protocol probe separately validates server commands.

Shared coverage includes transactional rollback on delete failure; body/HTML/CID
rekey and restart; idempotent local retry; preserving fresher received destination
metadata/bodies; Confirmed/NotApplied/Unknown and cancellation/stale callbacks;
verified role/picker restrictions; cache failure with local retry and exactly one
server move; clearing reopened stale source UI; and Move/Archive/Trash controls at
640x480 and 100%/200% text. Unknown outcomes are shown inside the compact reader.

Logs remain in ignored artifacts/m4-move-ui-shared.log,
m4-move-ui-windows.log, m4-move-ui-aot.log and m4-move-ui-framework.log.
See [reader behavior and remaining work](../../m4-moves.md).

Undo, permanent deletion and durable move/cache recovery across restart remain
pending. Local retry metadata is session-only; after restart a failed reconciliation
can leave stale saved source rows for recovery. No server MOVE is automatically
replayed. Broader provider, native accessibility and other-platform acceptance
remain open, as does the earlier HTML process-isolation requirement.
