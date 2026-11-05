# M4 Windows x64 deliberate permanent deletion validation

Validated locally on 2026-11-02 against the dirty working tree. These unsigned
validation packages are not releases or real-provider certification.

- Shared Release suite: 943 passed (23 new protocol/model/cache/confirmation cases).
- Focused Windows reader/profile/keyboard/theme/remote-image checks: 79 passed.
- NativeAOT protocol probe passed TLS and STARTTLS: one UID is marked Deleted and
  expunged with UID EXPUNGE while an unrelated pre-deleted UID is retained. Existing
  flag, move/reverse/unknown, read-only-body and SMTP fixture checks still pass.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
  A fresh synthetic profile fixture drives request/cancel/confirm through the actual
  application model/cache and reopens SQLite to verify the selected row/body is
  absent while another message and body remain.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
  No IL2xxx/IL3xxx warnings were observed in the final publish logs.

Shared coverage includes already-marked deletion; UIDPLUS/epoch/writability/Deleted
permission/missing-source refusal; interrupted marking and expunge; exact cache row
and receive-time preservation; context-bound confirmation; cancel/stale callbacks;
server-confirmed/cache-failed cleanup retry without another server request; and
confirmation/focus at 640x480 with 100% and 200% text. The confirmation fixture exposes
read, star and move capabilities too, exercising the production action layout.
The last loaded row disappearing no longer implies that the whole server folder is
empty; the UI asks for Receive to refresh membership and paging.

Logs remain in ignored artifacts/m4-delete-shared.log, m4-delete-windows.log,
m4-delete-protocol-aot.log, m4-delete-aot.log and m4-delete-framework.log.
See [deletion behavior and remaining work](../../m4-deletion.md). Only a confirmed
Trash role exposes permanent deletion in the UI. No mailbox-wide EXPUNGE/CLOSE or
broad fallback is sent; Unknown is never automatically retried. Historical backups
may retain copies. Local retry metadata is session-only; durable recovery across
restart, broad provider/native accessibility acceptance and other-platform
certification remain open.
