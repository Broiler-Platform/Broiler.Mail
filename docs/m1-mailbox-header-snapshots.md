# M1 complete current-folder header observations

Implemented 2026-11-04. **Offline folders → Snapshot all headers in current folder** explicitly obtains and persists one bounded complete server-header observation. **Inspect saved header snapshot** reads its durable checkpoint locally, including for disabled accounts. Opening the section, selecting a folder or starting Mail does neither operation automatically.

This closes the gap between a successful partial page/manual offline walk and a verified complete membership observation. It is not continuous synchronization, a QRESYNC/mod-sequence cursor, durable folder rename mapping, or complete body/attachment acquisition. These larger M1/M3 requirements remain open.

## Protocol validation and bounds

The production IMAP receiver uses one authenticated connection and serial commands. It examines the exact canonical mailbox read-only, obtains a `UID SEARCH ALL` census, fetches all header metadata in batches of at most 500, re-examines the mailbox and obtains another complete census. Every sequence position must map to the corresponding initial UID; envelope identity and flags are required. Both censuses, count, UIDVALIDITY and any UIDNEXT must agree before a snapshot is returned. UID-set order is normalized rather than assumed. This follows the distinction between sequence numbers and UIDs in [IMAP4rev1, RFC 3501](https://www.rfc-editor.org/rfc/rfc3501.html); the client expects its SEARCH response format. ESEARCH-only replies fail closed pending explicit extension support.

Limits: **4,096 messages**, **128 KiB per response line**, **2 MiB of response characters per snapshot command**, bounded response-line counts, and the existing operation deadline. An over-limit folder gets an explanation and can still use ordinary paged receiving. It never gets a partial result labeled complete. Missing/repeated/malformed census entries, missing flags, sequence/UID mismatches, observed expunges/epoch changes, changed final membership, disconnects and cancellation produce no complete receiver result. Unsolicited metadata may cause a conservative refusal and require a fresh attempt.

Only read-only EXAMINE, UID SEARCH and header FETCH are added after authentication. No body, original MIME, attachment, STORE, SELECT-for-write, MOVE, EXPUNGE or send operation is requested by this feature. Flags are observed as each header is fetched; this is not an atomic server-wide flag snapshot. A later server change can happen immediately after verification, so the timestamp and membership are explicitly historical.

## Durable cache state

Cache **v5** adds one latest checkpoint per account/folder and its epoch-scoped UID membership. Opening v4 creates and verifies a `mail-cache.db.schema-v4-<id>.backup` before a transactional upgrade. Existing older migrations keep their own recovery copies. If the configured allocation has insufficient migration headroom, v4 reads and indexed search remain available; snapshot capture refuses until the quota is increased. Future cache formats are rejected without resetting data.

Applying a complete observation validates bounded unique full message keys, then commits header/flag upserts, a sorted UID census, its SHA-256, observation time and mailbox head in one SQLite transaction. Valid attached search-index changes participate in that transaction. A source quota/index/census failure rolls back the entire observation; older observations cannot replace a newer saved receive/checkpoint. The checksum detects changed/incomplete local membership, not a dishonest server or authenticated identity.

**Previously saved headers and bodies absent from the census are retained.** They are not inferred to be expendable or silently deleted. The checkpoint reports their count in that epoch, and the reader explains that its saved list can include them. Epoch changes preserve old-epoch data under its original keys. The normal bounded saved-header page and Load older remain available after capture. This does not automatically fetch bodies or broaden the download/retention policy.

Inspection verifies the saved count and checksum before displaying the account, folder, UIDVALIDITY, observation time, header count, retained-absent count and UID-set hash. Later arrivals may also be absent from an older observation; the notice describes historical membership rather than a current deletion determination. An ordinary partial receive does not become a complete checkpoint.

Verified profile backups/restores include these source-cache tables. Account/orphan downloaded-mail cleanup removes only that account's checkpoints/censuses along with its downloaded mail, after the existing recovery backup. Whole-cache cleanup removes all checkpoints/censuses after backup. Separate-profile restore recovers the observation; it never replays a server operation.

## Lifecycle and validation

Capture shares the reader's cancellation, account/folder generation and storage-maintenance guards. The account/folder identity is rechecked before cache application and before presenting the checkpoint. Disabled accounts can inspect but cannot capture. Search mode cannot start a capture. A late ignored cancellation cannot paint another account; cancellation before applying prevents a save. A commit immediately before cancellation can remain, so cancellation/failure guidance says to inspect before retrying. Retry preserves capture versus inspection, including local-only retry for disabled accounts.

[Windows x64 validation evidence](evidence/m1-mailbox-snapshot-win-x64/README.md) covers TLS/STARTTLS fixtures, failure races, transactional rollback, retention, versioned migration, restore/cleanup and explicit bounded controls at 100%/200% text scale. Wider provider/target acceptance, continuous orchestration and folder rename identity remain in the [completion audit](m0-m4-completion-status.md).
