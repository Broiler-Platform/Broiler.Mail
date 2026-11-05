# M4 — Verified UID MOVE foundation

Updated 2026-11-02. This slice adds the protocol/application service contract for
single-message moves. The reader continuation now adds an explicit destination
picker, Archive/Trash shortcuts, transactional cache reconciliation and one-level
verified move undo. Separate [permanent deletion](m4-deletion.md) is now implemented;
durable recovery remains pending.

## Contract and server behavior

`IMailMover.MoveAsync` takes the saved account, full source key and exact destination
mailbox identity. `MailMoveResult` carries Confirmed, NotApplied or Unknown status.
Only Confirmed includes a verified full destination account/mailbox/UIDVALIDITY/UID
key. Destination UIDs are never inferred from source UIDs or Message-ID.

Before sending a move, the production receiver:

- Validates account/key scope, nonzero source UID/epoch and bounded control-free
  mailbox names. Canonical INBOX targeting its own source is refused without a
  connection; other mailbox names keep exact casing.
- Authenticates through the existing certificate-validated TLS/STARTTLS path.
- Uses bounded LIST discovery to refresh post-authentication capabilities and
  confirm that the exact destination is selectable. MOVE must be advertised.
- SELECTs the source read-write, checks UIDVALIDITY, and verifies the source UID.
  Seen/Flagged permanent-flag permission is not needed to perform MOVE; actual
  move ACL rights are enforced by the server.

It then sends exactly one `UID MOVE <uid> <quoted modified-UTF7 destination>`.
It never substitutes UID COPY + STORE Deleted + EXPUNGE, creates folders or sends
CLOSE. The implementation follows [RFC 6851](https://www.rfc-editor.org/rfc/rfc6851.html).
A server without advertised MOVE gets an explicit NotApplied result.

Confirmation requires tagged OK, an unambiguous single-message COPYUID receipt
matching the requested source UID, absence of that UID in the selected source,
and presence of the mapped UID under the matching destination UIDVALIDITY after
read-only EXAMINE. Tagged and untagged COPYUID receipts and singleton ranges such
as 42:42 are supported. Conflicting/malformed/multiple-message mappings are refused.
The existing mutation response bounds (128 lines / 8192 bytes per line / 256 KiB),
connection deadlines and cancellation continue to apply.

## Uncertain outcomes

Before UID MOVE is attempted, unsupported capabilities, unavailable/nonselectable
destinations, stale source epochs, read-only sources and missing source UIDs yield
NotApplied. After sending it, lost acknowledgements, cancellation, malformed or
missing COPYUID, failed verification and tagged rejection all yield Unknown.
RFC 6851 permits partial effects even with NO responses; the service does not
promise that rejection proves no remote change. A server may successfully move a
message without returning COPYUID; that is also Unknown in this conservative API.

No move is automatically repeated or queued. Callers must ask the user to check
both folders after Unknown. No destination key is exposed as confirmed in that
case. Opening/receiving messages continues to be read-only. The protocol service performs no cache writes. The reader continuation below
uses only confirmed destination keys for local reconciliation.

## Validation

- Shared Release suite: 889 passed (`artifacts/m4-moves-shared.log`), including 19
  new move checks over TLS and STARTTLS. Covers Unicode destinations, preserved
  MIME/read/star state, supported/unsupported destinations/capabilities, epoch and
  writable checks, single-UID receipts in tagged/untagged forms, lost/rejected and
  unverifiable moves, no same-folder connection and unsafe input refusal.
- Windows x64 NativeAOT protocol probe confirms source removal, verified destination
  identity and a dropped MOVE acknowledgement with exactly one attempt under both
  TLS modes (`artifacts/m4-moves-protocol-aot.log`).
- Existing focused Windows checks and both Windows x64 package variants remain
  regression gates. The application package does not expose move UI yet; the
  standalone NativeAOT protocol probe exercises the new move path directly.
  See [retained move evidence](evidence/m4-moves-win-x64/README.md).

## Reader and cache continuation — 2026-11-02

The reader now provides an accessible Move destination picker, Move message,
Archive and Move to trash actions. Choices are selectable server-discovered
mailboxes other than the current folder. Nothing moves merely by changing the
picker. Archive/Trash require a confirmed role mapping and a different destination;
missing, ambiguous or unverified role overrides stay unavailable until discovery
confirms them. The backend revalidates the destination/capability before sending.
Move to trash uses UID MOVE; it never sets Deleted or permanently deletes mail.

A confirmed move removes the source row from the loaded list, closes its reader,
and invalidates older-message paging. Receive refreshes server membership and
paging explicitly. NotApplied/Unknown leave loaded mail and cache untouched, with
a visible notice in both list and compact-reader layouts. Cancel or folder/account switches discard late callbacks and
never replay the move. Moving messages does not automatically download mail.

### Transactional local reconciliation

`IMailMoveCache.ReconcileMoveAsync` uses one SQLite transaction to:

- Read the exact source account/mailbox/epoch/UID, validate its bounded body and
  serialize that body under the confirmed destination key, retaining HTML/CID data.
- Merge the destination header/body. An already received destination keeps its
  fresher metadata and valid body; a missing destination body is filled from the
  source. Only after a usable destination row/head exists is the source row removed.
- Keep unrelated rows/epochs and existing mailbox receive timestamps unchanged.
  A newly introduced destination head inherits the source's original receive time;
  this is retained snapshot metadata, not a claim of a fresh full-folder receive.
- Commit atomically under the existing quota/synchronous policy. Errors/cancellation
  before commit roll back the transfer; no schema change is needed beyond v2.

The operation is safe to repeat locally after completion. A conflicting known
destination epoch is refused until an explicit receive establishes the verified
current epoch. Corrupt/oversized/misidentified bodies are not silently discarded.
Headers-only policy transfers only already cached bodies; it does not save a
previously unsaved on-screen body.

If the server confirms but cache reconciliation fails, the reader reports that
split outcome, preserves local source data and offers Retry local move update.
That action calls only SQLite, never IMAP MOVE. A successful local retry also clears
any reopened stale source row/reader and refreshes an affected cached folder.
Additional moves are disabled until
the pending local update is resolved. The destination may be refreshed first to
resolve an epoch conflict. The immediate UI retry state is account-scoped and session-only. Confirmed local
reconciliation now also uses [durable receipts and Windows startup repair](m4-cache-recovery.md).
No automatic server replay occurs; pre-receipt/network-phase intent recovery remains open.

Validation for this continuation:

- Shared Release suite: 908 passed (`artifacts/m4-move-ui-shared.log`), including
  atomic rollback, rekeyed body/CID preservation across restart, idempotent local
  updates, fresh destination metadata/body merging, Confirmed/NotApplied/Unknown,
  stale callbacks/cancel, role restrictions and local retry without a second MOVE.
- Reader action checks cover Move/Archive/Trash at 640x480 and 100%/200% text size,
  explicit picker selection and focus handoff to Cancel while moving.
- Focused Windows checks: 79 passed (`artifacts/m4-move-ui-windows.log`).
- NativeAOT and framework-dependent Windows x64 packages pass publish/smoke,
  dependency policy and renderer audit. A fresh synthetic mover drives Archive
  through the real model/SQLite path, then a new model/cache instance reopens the
  moved header, flags and body offline under its destination UID. Network/protocol
  move behavior remains separately covered by the preceding NativeAOT probe.
  See [reader move evidence](evidence/m4-move-ui-win-x64/README.md).

## Verified one-level undo continuation — 2026-11-02

Undo last move is a reader-toolbar action available after a confirmed move and
successful local reconciliation. It works without a selected message and across
folder selection changes in the same account. It requires an enabled account,
no active operation/pending cache update and a currently discovered selectable
original source folder. Account/profile changes and app restart clear the entry.
A later move attempt replaces it; there is no multi-step history or redo.

Undo calls the same verified IMailMover with the confirmed destination key as its
new source and the original source mailbox as its target. The backend rechecks
MOVE support, mailbox epochs, writability, UID presence and the destination receipt.
The restored message uses the newly returned UID, never an assumed original UID.
Cache reconciliation rekeys its body/HTML/CID data and flags atomically through the
existing move-cache contract. An affected destination folder can reopen saved
headers; a live folder still needs Receive to refresh full server membership.
An unrelated open reader and unrelated paging cursor remain unchanged.

The undo entry is consumed before starting the request. NotApplied, Unknown,
cancellation, stale callbacks and lost acknowledgements never restore a replayable
entry or automatically send another MOVE. Unknown leaves local mail as it was and
asks the user to check both folders. A confirmed reverse move with failed cache
reconciliation offers the existing local-only retry; that sends no reverse MOVE
and creates no new undo/redo entry. Forward-move undo stays unavailable until its
pending local update succeeds.

Validation:

- Shared Release suite: 920 passed (`artifacts/m4-undo-shared.log`), including new
  assigned UID/body restoration, uncertain/refused/canceled/stale undo, account
  boundaries, preservation of unrelated readers/paging and local retry after a
  confirmed reverse move without another server request.
- Undo toolbar access/focus is tested at 640x480 and 100%/200% text size, including
  no selected message, focus handoff to Cancel and consumed-action disappearance.
- TLS and STARTTLS fixtures verify a forward/reverse UID MOVE round trip to a new
  UID. A dropped reverse acknowledgement is Unknown with no retry.
- Windows x64 NativeAOT protocol probe exercises the actual verified forward and
  reverse service over both TLS modes and reads the preserved message at its new UID.
- Focused Windows checks and both Windows x64 publish variants pass. The published
  synthetic profile fixture reverses a confirmed Archive move through the real
  model/cache, opens its rekeyed body offline, then archives again to a different UID
  and verifies restart. See [undo evidence](evidence/m4-undo-win-x64/README.md).

## Next

The separate [permanent deletion flow](m4-deletion.md) now requires explicit
confirmation and scoped UID EXPUNGE. Durable move/cache recovery across restart, broader
provider/native accessibility acceptance and other-platform certification remain
open. M4 also retains attachment, search, multiple-draft and outbox work.
