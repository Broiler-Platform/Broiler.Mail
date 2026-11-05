# M4 Windows x64 verified move undo validation

Validated locally on 2026-11-02 against the dirty working tree. These unsigned
packages are validation artifacts, not releases or real-provider certification.

- Shared Release suite: 920 passed (12 new undo/protocol/reader cases).
- Focused Windows reader/profile/keyboard/theme/remote-image checks: 79 passed.
- NativeAOT protocol probe passed TLS and STARTTLS: forward UID MOVE, verified
  reverse MOVE from its assigned destination identity, a different returned UID,
  and reading the original content at that new identity. Existing uncertain-move,
  read-only body, flag and SMTP fixture checks also pass.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
  The fresh synthetic profile fixture performs Archive, Undo and Archive again
  through the real model/cache path, verifies offline body restoration at the new
  UID, then reopens the final move at another assigned UID in a new model/cache.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
  No IL2xxx/IL3xxx warnings were observed in the final publish logs.

New coverage includes consumed undo on NotApplied/Unknown/cancel/stale results,
account boundaries, reverse-copy identity/body persistence, unrelated open reader
and paging preservation, cache failure followed by local-only retry without another
reverse MOVE, lost reverse acknowledgement and toolbar focus/access at 640x480 and
100%/200% text size with no selected message.

Logs remain in ignored artifacts/m4-undo-shared.log, m4-undo-windows.log,
m4-undo-protocol-aot.log, m4-undo-aot.log and m4-undo-framework.log.
See [undo behavior and remaining work](../../m4-moves.md). Undo is single-level,
session-only and has no redo; an attempted undo consumes its entry. Permanent
deletion, durable operation/cache recovery, real-provider and broader native
accessibility/other-platform acceptance remain open.
