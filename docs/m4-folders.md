# M4 — Folder access and navigation

Updated 2026-11-02. M4 is in progress. The Windows reader now supports folder
selection, folder-specific offline mail, special-use role mapping and explicit
read/unread, star/unstar and move/archive/trash actions with cache reconciliation.
One-level verified move undo and separately confirmed permanent deletion are
available; durable recovery remains pending.

## Implemented

- Optional `IMailFolderReceiver` capability reads a named mailbox; existing
  `GetInboxAsync` delegates to the same path with canonical INBOX identity.
- Page, empty-page epoch, message key, body request, and session-only continuation
  retain the mailbox identity. A continuation from another mailbox is rejected
  before connecting, even when UIDs, UIDVALIDITY and counts coincide. Only INBOX
  is canonicalized case-insensitively; other names remain exact server identities.
- Read access continues to use EXAMINE and bounded BODY.PEEK, with the existing
  TLS, credential binding, message-size and epoch checks. It never selects a
  writable mailbox or marks a message read.
- Named EXAMINE uses quoted, escaped modified UTF-7 for the current IMAP4rev1
  protocol baseline, per [RFC 3501 section 5.1.3](https://www.rfc-editor.org/rfc/rfc3501.html#section-5.1.3).
  Control characters, invalid UTF-16, blank names and names above 1024 UTF-16
  units are rejected before connecting. This does not enable UTF8=ACCEPT.
- The existing cache already scopes rows by account, mailbox, epoch and UID;
  no storage migration or profile format change is needed for this foundation.

## Validation

- Shared Release suite: 788 passed (`artifacts/m4-folders-shared.log`).
- Published Windows x64 NativeAOT protocol probe passed implicit TLS and STARTTLS:
  named Unicode/ampersand folder EXAMINE, body PEEK and distinct overlapping UID
  identities, alongside the existing inbox and SMTP checks
  (`artifacts/m4-protocol-aot.log`).
- Windows x64 NativeAOT application publish/smoke passed, including the
  72-library dependency policy and renderer audit (`artifacts/m4-folders-aot.log`).
  This is an unsigned local validation artifact, not a released build.
- Loopback tests cover named/empty mailbox reads, paging, epoch changes, casing,
  quoted and Unicode names, unsafe input and cross-folder cursor rejection.
  These fixtures exercise wire behavior; they do not certify real providers.

## Folder discovery continuation — 2026-11-02

`IMailFolderReceiver.GetFoldersAsync` now returns a read-only server catalog from
`LIST "" "*"`. Each entry preserves its decoded mailbox identity, hierarchy delimiter
(including NIL), selectability and raw server attributes. No folder is selected or
fetched during discovery; no folder is created, renamed or deleted. At this protocol-only slice the application still showed INBOX; the navigation
continuation below now connects this catalog to the reader.

Parsing supports atom, quoted/escaped and literal mailbox names. Modified UTF-7 is
strictly decoded, with malformed/noncanonical encodings and duplicate canonical
identities rejected. No partial catalog is returned on failure. Wildcards and NIL
in returned atom names retain their literal identity. Attribute metadata is retained
for later role mapping; names such as Sent are not assumed to identify a role.

Limits: 512 folders, 1024 responses, 8192 bytes per response line, 4096 bytes per
mailbox literal and 1 MiB aggregate response content. Existing connection deadlines,
TLS validation and cancellation apply. Unsupported extended LIST trailing data fails
explicitly; this baseline does not negotiate LIST-EXTENDED/SPECIAL-USE or UTF8=ACCEPT.
The protocol grammar follows [RFC 3501 LIST](https://www.rfc-editor.org/rfc/rfc3501.html#section-7.2.2).

Validation for this continuation:

- Shared Release suite: 804 passed (`artifacts/m4-discovery-shared.log`).
- Windows x64 NativeAOT protocol probe: implicit TLS and STARTTLS discovery,
  including a Unicode literal name, hierarchy-only entry and retained Archive
  attribute, followed by existing inbox/folder reads and SMTP checks
  (`artifacts/m4-discovery-protocol-aot.log`).
- Loopback tests cover empty/rejected catalogs, malformed encoding/quoting,
  oversized lines/literals/catalogs, duplicate identities, hierarchy metadata and
  exact wildcard names. Real-provider and UI acceptance remain pending.

## Reader navigation continuation — 2026-11-02

- The named Mail folder selector and Refresh folders action are available in the
  Windows reader. Server hierarchy-only/nonselectable entries are labeled and
  cannot be opened. Receive, older paging and explicit body downloads target the
  selected mailbox. There are no server mutations in these controls.
- Selecting a different folder cancels/invalidate pending receive, body, discovery
  and offline-download callbacks, clears the open reader and session cursor, and
  opens saved headers first. It does not automatically connect. Receive mail checks
  that folder explicitly. Refresh folders is separately retryable/cancelable.
- `IMailFolderCache` enumerates account-scoped existing SQLite mailbox heads without
  a schema migration. Successfully received empty folders are included. Cached
  mailbox identities are merged with discovered names and labeled saved only when
  not confirmed by the current discovery. Downloaded mail remains reachable after
  restart even when offline or disabled; an uncached body remains unavailable.
- Account changes reset selection to INBOX and clear the prior account's catalog.
  Folder choice and complete server LIST metadata are session-only; only folders
  with successfully saved headers/empty-mailbox epochs survive restart. Missing
  folders are not automatically purged. A saved-only label is not proof of deletion.
- Read operations reject a page/continuation for another mailbox before cache writes.
  Overlapping UIDs and UIDVALIDITY across folders retain distinct keys and bodies.
  Existing quota, 500-header session cap and headers-only/on-demand policies apply.
- Folder names are shown verbatim rather than split on an assumed delimiter.
  Basic hierarchy metadata is retained; this is a flat selector, not a folder tree.

Validation:

- Shared Release suite: 815 passed, including model race/offline tests and 640x480
  selector/selection checks at 100% and 200% text size
  (`artifacts/m4-navigation-shared.log`).
- Focused Windows reader, remote image, keyboard/theme and profile smoke checks:
  79 passed (`artifacts/m4-navigation-windows.log`).
- Windows x64 NativeAOT/self-contained and framework-dependent validation packages
  pass published smoke, dependency policy (72 runtime libraries) and renderer audit.
  The published profile fixture reopens saved INBOX/Archive mail with overlapping
  UIDs and verifies the correct bodies through the application view model. These
  are unsigned local artifacts, not a release or real-provider certification.
  See [retained evidence](evidence/m4-win-x64/README.md).

## Special-folder roles continuation — 2026-11-02

Discovery refreshes CAPABILITY after authentication. When SPECIAL-USE is advertised,
LIST requests `RETURN (SPECIAL-USE)`; otherwise it uses baseline LIST and still
retains any unsolicited role attributes. This follows
[RFC 6154](https://www.rfc-editor.org/rfc/rfc6154.html). Existing catalog bounds and
strict parser/error behavior remain in force; extended trailing LIST data is not
silently ignored.

The reader resolves Sent, Drafts, Archive, Trash and Junk from unique selectable
server attributes. It never guesses English/localized names or treats All mail as
Archive. Duplicate candidates remain ambiguous. Role labels appear in the folder
selector; the bounded notice explains unavailable and unconfirmed mappings.

Account / Incoming (IMAP) / Special-folder mapping now provides five optional exact
mailbox overrides. Empty means automatic. Overrides preserve case and whitespace
within names; INBOX alone is canonicalized. They take precedence over automatic
attributes, remain visible offline and are confirmed only by a matching selectable
entry in the current server catalog. A missing/nonselectable override is reported;
there is no silent fallback or automatic folder creation. A manually named folder
can still be received explicitly before discovery, using existing read-only access.

Overrides persist in both account stores and profile backups with source-generated
JSON. Legacy omitted/null mapping objects normalize to automatic defaults; invalid
control characters, Unicode and names over 1024 UTF-16 units are rejected. Profile
switches reload all five fields, and edits participate in unsaved-change protection.
Validation expands the mapping section and reveals the relevant field.

These roles currently describe navigation. They do not save drafts to IMAP or
move/delete messages. Sent-copy handling remains an independent, explicit SMTP
submission policy with its existing exact Sent folder and unknown-outcome behavior.
Setting a Sent navigation override never enables or redirects APPEND.

Validation:

- Shared Release suite: 831 passed (`artifacts/m4-roles-shared.log`), including
  missing/ambiguous/nonselectable roles, override precedence, exact identities,
  editor/store round trips, legacy defaults and 100%/200% validation focus/reveal.
- Focused Windows checks: 79 passed (`artifacts/m4-roles-windows.log`).
- Windows x64 NativeAOT protocol probe passed SPECIAL-USE negotiation and role
  resolution under both implicit TLS and STARTTLS (`artifacts/m4-roles-protocol-aot.log`).
- NativeAOT and framework-dependent application packages passed publish/smoke,
  dependency policy and renderer audit. The separate-process synthetic profile
  fixture now persists all five overrides and verifies unconfirmed offline role
  mapping after restart. See [role validation evidence](evidence/m4-roles-win-x64/README.md).

## Explicit read/unread continuation — 2026-11-02

The reader offers Mark read / Mark unread for its selected message. Merely opening
or receiving mail continues to use EXAMINE/BODY.PEEK and never changes flags.
Only this explicit action uses the optional `IMailReadStateWriter` capability.

The production writer authenticates over the existing validated TLS path on a fresh
connection, selects the exact encoded mailbox, checks UIDVALIDITY and writable
status, and checks permission to change Seen permanently. An explicit PERMANENTFLAGS
list that excludes Seen prevents mutation; when that response is absent the RFC
baseline permits permanent flag changes. The writer verifies the UID exists,
changes only Seen with incremental UID STORE +/-FLAGS.SILENT and fetches UID/FLAGS
again before reporting confirmation. An already matching server state is confirmed
without a STORE. No other flags are replaced; no CLOSE, EXPUNGE or folder creation
is sent. See [RFC 3501 STORE](https://www.rfc-editor.org/rfc/rfc3501.html#section-6.4.6).
Read-state command responses are bounded to 128 lines, 8192 bytes per line and
256 KiB aggregate content, in addition to the existing operation deadline.

The result distinguishes confirmed, not applied and unknown outcomes. A rejected
command, missing UID or changed epoch does not update the UI/cache. A dropped
acknowledgement or unavailable/mismatched verification after STORE is unknown.
The reader keeps the previous displayed flag and explains that the server may have
applied the change. Cancellation is conservative too. Receive mail checks current
server flags; mutations are not automatically retried, queued or replayed offline.
The opposite explicit command reverses a confirmed change; there is no undo history.

Only confirmed outcomes update the selected summary and loaded row. The optional
`IMailReadStateCache` updates the existing account/mailbox/epoch/UID row without
creating headers, touching bodies, changing mailbox heads or advancing last-receive
time. A failed cache update retains the confirmed server/UI state and warns that
offline flags were not saved. Read/unread alone needed no cache schema migration;
starred-state persistence adds the versioned upgrade described below. Folder/account
switches discard stale callbacks; switching/canceling cannot retract an already
sent STORE. Unknown-outcome notices are session-only, not a durable operation log.

Validation:

- Shared Release suite: 850 passed (`artifacts/m4-readstate-shared.log`). New cases
  cover TLS/STARTTLS, named Unicode folders, permissions/epoch/missing UID checks,
  both flag directions, already matching states, rejected/lost/ignored/removed
  outcomes, scoped cache updates, stale callbacks and cache-save failure.
- Reader control tests cover 640x480 at 100% and 200% text size and hand focus to
  Cancel while the explicit change is pending. Reading still preserves the body.
- Focused Windows checks: 79 passed (`artifacts/m4-readstate-windows.log`).
- Windows x64 NativeAOT protocol probe passed confirmed read/unread and dropped
  STORE acknowledgement over implicit TLS and STARTTLS, with no automatic retry
  (`artifacts/m4-readstate-protocol-aot.log`).
- Both Windows x64 validation package variants pass published smoke, including a
  synthetic writer through the application view model and real SQLite updates on
  profile reopen. Retained [read-state evidence](evidence/m4-readstate-win-x64/README.md)
  distinguishes those local fixtures from provider certification.

## Persistent starred-state continuation — 2026-11-02

The reader now offers Star message / Unstar message beside Mark read / Mark unread.
Received FLAGS populate IsStarred, starred rows have a visible star prefix and
accessible Starred status, and the open-message metadata identifies starred mail.
These are explicit actions; receiving/opening a message still never changes flags.

Read/unread and star/unstar share the guarded application/protocol flow. The optional
`IMailStarStateWriter` changes only Flagged using UID STORE +/-FLAGS.SILENT, checks
writable/permanent-flag permission and UIDVALIDITY, verifies the UID and result,
and reports the shared MailFlagChangeStatus (Confirmed / NotApplied / Unknown).
Seen and other flags are preserved. Confirmed outcomes alone update the UI/cache;
cache-save failures are reported separately. Cancel/stale callbacks and unknown
acknowledgements never enqueue automatic retries. Changing either flag is still
possible only for an enabled account and an identified selected message.

### Cache upgrade and recovery

SQLite schema v2 adds constrained `is_starred`, defaulting existing v1 rows to false.
Existing mail, bodies, epoch scopes, read state and receive times are retained;
receive again to learn stars that the old schema could not retain. Before upgrading,
a verified logical v1 snapshot is published beside the database as
`mail-cache.db.schema-v1-<id>.backup`. The subsequent ALTER/version update commits
transactionally with synchronous FULL and the cache quota in force. If backup or
migration fails, the error is surfaced; no empty replacement is created. A failed
attempt can leave a recovery snapshot. These plaintext recovery files require extra
disk space outside the cache quota and are not automatically deleted.

Snapshot validation accepts v1 without modifying it, and validates the star column
for v2. Legacy snapshots copied/restored into a fresh profile migrate on first cache
open. Newer schemas are still refused. The migration snapshot is only a database
backup, not a complete profile or password backup. Close Mail and preserve the
whole profile before manual recovery; use a new directory with compatible profile
configuration, never mix files into an active profile. Older builds that only
understand schema v1 cannot open the upgraded cache; they need compatible complete
backup data. There is no in-place downgrade.

Validation:

- Shared Release suite: 870 passed (`artifacts/m4-stars-shared.log`). Includes
  v1 snapshot validation without writes, retained migration backup, upgrade and
  rollback failure behavior, body/time preservation, reopened stars and exact-row
  isolation; TLS/STARTTLS star/unstar and permission/epoch/refusal/uncertainty cases;
  shared confirmed/unknown/canceled model cases and reader controls at 100%/200%.
- Focused Windows checks: 79 passed (`artifacts/m4-stars-windows.log`).
- Windows x64 NativeAOT protocol probe: confirmed read/unread/star/unstar plus a
  dropped star STORE acknowledgement, without retry, over TLS and STARTTLS
  (`artifacts/m4-stars-protocol-aot.log`).
- NativeAOT and framework-dependent application packages pass publish/smoke,
  dependency policy and renderer audit. Their fresh synthetic profile fixture
  starts with a v1 cache, migrates on reopen, then exercises both flags through the
  real model/cache path. See [starred-state evidence](evidence/m4-stars-win-x64/README.md).

## Verified UID MOVE continuation — 2026-11-02

The optional IMailMover service now performs capability-gated single-message UID
MOVE and returns a verified destination identity only after receipt/epoch/source
and destination checks. Unknown outcomes are never retried. This is a protocol
foundation now has reader controls and transactional cached-body reconciliation;
one-level undo is now available; durable history/recovery remain pending. See
[UID MOVE design and evidence](m4-moves.md).

## Next

The [separate permanent-deletion flow](m4-deletion.md) is implemented. Durable
reconciliation/history across restart remains open. Attachment, search, multiple-draft and outbox work
remains in M4. Broader real-provider, assistive-technology/native UI acceptance,
folder trees, IMAP4rev2/UTF8 negotiation and other-platform certification remain open.
