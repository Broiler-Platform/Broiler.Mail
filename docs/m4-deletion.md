# M4 — Deliberate permanent deletion

Updated 2026-11-02. The reader now supports explicit permanent deletion from a
currently confirmed Trash role. This is separate from Move to trash and move undo.
It never automatically deletes messages or replays an uncertain deletion.

## Confirmation and scope

Delete permanently opens an inline confirmation naming the message subject,
folder and account. Keep message receives focus; Confirm permanent deletion is a
separate non-default button. Nothing is sent by opening/canceling the confirmation.
A new receive/body operation, selection/folder/account change or maintenance clears
the snapshot. Confirmation rechecks the same selected key/profile and confirmed
Trash role before proceeding. Flag, move and undo actions are unavailable during
confirmation. Permanent deletion consumes move undo and offers no undo of its own.

The optional `IMailPermanentDeleter` is an explicitly invoked single-message API;
its caller must obtain confirmation. The UI restricts it to an enabled account,
confirmed Trash folder, current selected message and supported cache capability.
It does not infer Trash from an English folder name or an unverified override.

## Server operation

The production receiver uses the existing validated TLS/STARTTLS/credential path,
refreshes post-authentication capabilities, and requires UIDPLUS. It checks the
exact account/mailbox/UIDVALIDITY/UID, SELECT read-write status and permanent Deleted
permission. If the source UID is missing, no deletion is sent. It adds only Deleted
with UID STORE +FLAGS.SILENT when needed, verifies that flag, then sends exactly one
`UID EXPUNGE <uid>` and verifies absence of the same UID.

[UID EXPUNGE in RFC 4315](https://www.rfc-editor.org/rfc/rfc4315.html#section-2.1)
scopes removal to messages both marked Deleted and included in the UID set. Broiler
sends one numeric UID, with no wildcard/range, mailbox-wide EXPUNGE, CLOSE, or
fallback that temporarily alters other messages' flags. Other already-deleted
messages remain untouched. UIDPLUS is required even if some other server mode could
provide equivalent functionality; IMAP4rev2 negotiation remains outside this slice.
Mutation responses retain the existing 128-line / 8192-byte line / 256-KiB aggregate
bounds and operation timeout. Receiving/opening mail remains read-only.

## Outcomes and local data

Before any STORE/UID EXPUNGE attempt, unsupported capability, stale epoch,
read-only/permission refusal, missing UID or failed connection returns NotApplied.
After an attempted mutation, rejection, disconnect, cancellation, unverified marking
or unverified removal returns Unknown. The message can be gone or still marked
Deleted; the user must refresh the folder. The implementation does not restore flags
or automatically retry. A later explicit attempt requires a new confirmation.

Only Confirmed removes the active cached row/body and visible source selection.
`IMailDeleteCache` uses a transaction scoped to the exact account/mailbox/epoch/UID,
is safe to repeat locally, preserves other rows and mailbox receive timestamps,
and requires no schema change. Paging is invalidated. With no loaded rows left,
the reader asks for a receive rather than claiming that the entire server folder
is empty; receive refreshes membership and paging explicitly.

If the server confirms but cache cleanup fails, the cached copy is retained and
Retry local deletion cleanup is offered. That action only retries SQLite and never
sends STORE/EXPUNGE. It clears any reopened stale selected row after successful
cleanup. The immediate UI retry is session-scoped; confirmed local cleanup now also
uses [durable receipts and Windows startup repair](m4-cache-recovery.md). Network-phase
intent/unknown recovery remains open. Unknown outcomes likewise leave the active local copy unchanged.

This removes the server message and confirmed active cache copy, not historical
profile/migration backups. Backups can retain mail. It is not secure erasure. The confirmation states the
active-copy/backups distinction; no server restore/undo is promised.

## Validation

- Shared Release suite: 943 passed (`artifacts/m4-delete-shared.log`): TLS/STARTTLS,
  already-marked messages, preservation of an unrelated pre-deleted UID,
  UIDPLUS/epoch/permission/read-only/missing-source refusal, interrupted marking
  and expunge, exact cache scope/time preservation, confirmation/context changes,
  canceled/stale callbacks, and local cleanup without another server request.
- Reader confirmation checks at 640x480 and 100%/200% text verify focus on Keep
  message, non-default confirmation, no deletion before confirmation and focus
  handoff to Cancel during the operation.
- Focused Windows checks: 79 passed (`artifacts/m4-delete-windows.log`).
- Windows x64 NativeAOT protocol probe performs a scoped permanent deletion over
  TLS and STARTTLS while retaining an unrelated pre-deleted message
  (`artifacts/m4-delete-protocol-aot.log`).
- Both Windows x64 package variants pass published smoke/dependency/renderer gates.
  A fresh synthetic profile fixture exercises request/cancel/confirm through the
  real model/cache path and reopens the database to verify only the confirmed row
  disappeared. See [retained deletion evidence](evidence/m4-delete-win-x64/README.md).

## Next

Durable mutation/cache recovery across restart remains open, alongside real-provider
and broader native accessibility/other-platform acceptance. M4 still includes
attachments, search, multiple drafts and outbox work. M4 is not complete.
