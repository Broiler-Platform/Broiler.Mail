# M1 persistent profile and offline reader

Updated 2026-11-03. Status: Windows offline reading and storage management are
implemented and locally verified, including configurable quota/download policy and
bounded loaded-message and selected-folder manual downloads. M1 now also has [manual selected-folder header acquisition](m1-offline-header-acquisition.md). M1 remains open for authoritative folder synchronization, durable server synchronization checkpoints, attachment retention
and further platform certification; automatic retention/eviction is intentionally disabled.
M0 ARM64/Linux CI evidence is still pending.

## Current behavior

Windows opens a versioned per-user profile at `%LOCALAPPDATA%/Broiler.Mail`, or the
explicit `--data-directory` path. Only one current Mail process may own that profile;
opening a second instance with the same directory fails safely. Demo mode stays
ephemeral. Other profiles use different directories. Account IDs and OS credential
bindings survive adoption; no browser/Net cookie session is used as a profile store.

At startup the inbox loads saved headers without contacting the server. Saved
headers page locally in descending UID order. Receive mail explicitly contacts
the server, commits returned headers, then displays them. Opening a message uses
its saved body if present; otherwise it downloads and displays it, committing first
when the saved policy is save-on-open (the default). Headers-only mode leaves new
reads ephemeral unless explicitly downloaded. Saved
plain text, untrusted HTML, bounded CID image bytes, truncation/fallback flags and
reply metadata survive restart. HTML uses the restricted inline reader described in M2; process containment remains open. Uncached bodies need a connection and explain that
they have not been downloaded when the fetch fails.

The reader labels offline availability and the inbox explains the last receive
time/staleness. Receiving remains read-only on the server. No automatic refresh,
server mutation, send retry, or background folder download is introduced.

### Download policy and explicit loaded-message batches

Settings → Local mail storage → Offline download policy offers two named modes:

- **Save bodies when opened**: the default for new and existing profiles. Opened
  bodies are committed before being reported as offline-available.
- **Headers only unless explicitly downloaded**: received headers still persist;
  opening an uncached body requires a connection but does not add it to the cache.
  Previously saved bodies remain readable and are not deleted by a policy change.

**Download loaded messages** in the inbox toolbar deliberately saves bodies for
the current loaded header set, up to the existing 500-row session limit, in either
policy mode. It does not fetch more headers, select another folder, or promise to
download an entire large mailbox. Load older headers explicitly before starting a
batch. Named-folder navigation is now available, and the later [selected-folder increment](m1-selected-offline-folders.md) adds explicit batches over saved headers in up to eight folders.

Batches fetch serially, reuse an already open body, skip valid cached bodies, and
show confirmed available-body progress. The first provider/storage/identity failure
stops the batch without discarding completed saves. **Retry offline download**
repeats that loaded set and skips completed copies. Cancel, account switches,
cleanup, and applying policy invalidate late results/progress. A commit can win a
cancellation race; saved content remains available after reopening. There is no
SMTP work, automatic send retry, or automatic receive triggered by a batch.
The cache is downloaded content, not authoritative synchronization: deleted/moved
messages may remain in saved history until reconciliation is implemented. New
UIDVALIDITY epochs never read older-epoch bodies under new identities.

## Profile layout and adoption

| Path | Purpose |
| --- | --- |
| profile.json | Schema version, stable local profile ID, creation date, cache quota/download policy, legacy backup reference. |
| profile.lock | Process lease; its existence alone does not indicate a live lock. Never delete it to bypass a running process. |
| accounts.json / settings.json / drafts.json | Existing independently versioned configuration and draft journal; adopted in place. |
| backups/legacy-&lt;id&gt;/ | Byte-for-byte copies of existing configuration/drafts made before adoption commits. No OS credentials are exported. |
| mail-cache.db.intents.json | Optional MOVE/delete pre-network intents; uncertain outcomes require explicit backed-up review and are never replayed. See [intents](m4-mutation-intents.md). |
| mail-cache.db.recovery.json | Optional durable confirmed MOVE/delete cache-repair receipts; included in verified backup/restore and protected from cache cleanup. See [recovery](m4-cache-recovery.md). |
| offline-folders.json (optional) | Account-scoped explicit folder selections; included in verified backups and retained on cache cleanup. |
| recipients.json (optional) | Explicit saved recipients partitioned by account; locked/atomic schema-1 JSON; included in verified backups and retained on cache cleanup. |
| mail-cache.db | SQLite schema/application identity, mailbox epoch/last-receive heads, headers/flags/dates and bounded decoded bodies. |
| mail-cache.db.search.db (optional) | Disposable FTS5 saved-header/body-preview index; separate 256 MiB limit; excluded from backups and rebuilt after restore. |

Before the first manifest is written, adoption takes legacy write locks and
validates all three existing stores, then flushes backup copies. It flushes and
atomically renames the manifest last. Interrupted adoption can repeat because the
source files never move. Partial backup/temp directories are not treated as a
completed migration. Invalid or future-version data is refused without reset.
A database without its manifest is refused instead of guessed/adopted.

The cache keys each row by `(AccountId, MailboxId, UIDVALIDITY, UID)`. Received
checkpoints here mean successful local header saves, not an IMAP incremental-sync
cursor. Transactions atomically update a batch and its receive timestamp. Body
commits require the matching header; headers are never marked offline-complete
when their body write fails. Old epochs are retained, not silently deleted.

Older manifests without a download-policy field retain save-on-open behavior.
Policy writes store a named enum value; unsupported modes are refused without a
reset. Older manifest-aware builds may refuse the newly written field rather than
silently discard it. Policy changes affect future opens/download jobs; they do not
retroactively save an already displayed ephemeral body or delete prior copies.

## Limits, cancellation, and security

- Policies persist received headers with optional save-on-open bodies and explicit
  bounded loaded-header batches. The existing
  50-header page/500-row view and network/MIME limits still apply. This cache stores
  decoded supported content, not original raw MIME or non-inline attachments.
- Default database quota: 256 MiB, enforced through SQLite page allocation limits.
  Settings accepts whole limits from 1 to 16384 MiB and applies them live. A lower
  limit is refused if the existing database allocation will not fit, even when some
  pages are free. No mail is deleted to satisfy a smaller quota; use a larger quota
  or deliberate recovery-backed cleanup instead. Invalid values, cancellation before
  commit, a changed/future manifest, or a failed manifest publication preserve the
  previous effective policy. Policy writes use a flushed temporary manifest and
  atomic rename while holding the cache gate; receive work is quiesced first.
  There is no automatic eviction, including after disk-full/quota failures. Existing
  bodies/drafts remain. Backups and rollback journals do not count toward the active
  database quota. SQLite's temporary rollback journal needs additional disk
  space; the quota is not a strict ceiling on every transient profile byte.
- Bound each serialized body to 8 MiB, plus existing text/HTML/CID limits; reads check
  lengths and full identity. Reply header lists are bounded and serialized through
  generated JSON metadata for NativeAOT. Malformed cache content fails visibly.
- Disk/parsing work runs off the UI thread; bounded operations accept cancellation.
  A committed transaction can win a cancellation race, but superseded UI operations
  cannot repaint another selection/account. No partially saved body is announced as
  available offline. No SQLite connection pool keeps closed profiles locked.
- Credentials remain OS-protected. Cached mail/configuration/backups are **not
  encrypted**. Windows uses the selected directory's inherited ACLs; newly created
  Unix profile directories use user-only mode. A custom/shared directory changes
  confidentiality. Optional database encryption/key recovery is not implemented;
  it needs a separate platform/key-backup decision before being offered.
- Android/CLI composition retains its previous behavior; the new Windows profile/cache
  path is not advertised as device-validated there. SQLite is now an Infrastructure
  dependency; packages must retain native e_sqlite3 libraries.

## Storage management, backup, and restore

Open **Settings → Local mail storage** to see the profile folder, refresh database
usage/header/body counts and the quota, create a verified backup, restore into a
separate profile, or clear downloaded mail. The section explains plaintext storage
and the current on-demand body policy. It is collapsed initially to keep ordinary
appearance settings short. Paths accept spaces; choose a new destination folder
for each operation. Use **Use a new default backup folder** to generate another
unused path without editing one manually.

**Create verified backup** first flushes the draft journal and prevents editing,
discarding, or sending that draft until the snapshot finishes. A running compose
operation must finish first. The service takes configuration-file write locks and
the same cache gate as receive/read/write operations, copies the current saved
configuration, and uses SQLite's online backup API for a consistent database copy.
It verifies SHA-256 file sizes/hashes, profile identity/schema, and database integrity
and schema before publishing the new backup directory. Backup creation does not
contact a server or export credentials. Other queued configuration writes may wait
or report busy; existing files remain intact.

Backups are logical snapshots: accounts.json, settings.json and drafts.json when
present, profile.json, mail-cache.db, and a versioned backup.json checksum manifest.
They also include account-collection.json when present; Windows now uses this
versioned collection for saved accounts and selected identity.
They do not recursively include older backups, lock/temp files, logs or unknown
files in the profile folder. The legacy-backup name in profile metadata remains
historical; those older copies are not part of the exported snapshot. Generated
backups are plaintext and need the same protection as mail. Hashes detect accidental
changes; they are not a signature establishing the source's trustworthiness.

Under **Restore as a separate profile**, choose a backup and an unused destination
folder, then **Restore backup**. The service allowlists filenames, checks bounded
manifest/file sizes, rejects traversal/duplicate entries and unsupported versions,
copies to a private staging directory, verifies the copied bytes and profile/cache,
then atomically publishes the destination. Existing folders are never overwritten.
After success, **Open restored profile** starts another Mail window on that copy.
The current profile stays open and unchanged. Interrupted/failed staging is not a
published profile and is removed when the operation unwinds normally.

**Clear downloaded mail…** requires a second, explicit **Back up and clear
downloaded mail** action. It quiesces inbox operations and creates a verified full
recovery snapshot in the chosen backup folder before deleting any cached rows.
A failed/canceled backup prevents deletion. The cache transaction clears headers,
bodies and receive heads, then attempts compaction. Failed compaction is reported
separately after a successful clear; existing database space can still be reused.
The reader reloads the empty local state. Late receive results from before cleanup
cannot repopulate it. Drafts, settings, account IDs, credentials and server mail are
preserved. The recovery backup retains any offline-only copies.

Backups remain on disk after cleanup: clearing the active cache does not guarantee
lower total profile usage. Choose a backup on another volume, or move an existing
backup elsewhere, to reclaim total space. No automatic deletion/retention policy
removes the last local copy. Cancellation is checked between copy/hash chunks and
before commit/publication; an in-progress native SQLite snapshot may finish before
observing it. A commit/publication may win a cancellation race and remains valid.

### Manual recovery and compatibility

Close Mail before copying profile files. A full manual backup is a copy of the
whole profile directory, including profile.json, the database, configuration and
drafts. The legacy adoption backup contains only the original configuration/draft
files; it is not a complete backup of later downloaded mail. Protect backups as
mail content. OS credentials are not copied; moving to another OS user/machine
requires signing in again.

An older manual folder backup can still be copied into a **new directory**, then opened with
`--data-directory <restored-directory>`. Do not merge a backup's cache/manifest with
another profile's configuration or replace active files while Mail is open. Newer
schemas are refused; use a compatible build or an earlier complete backup. Legacy
rollback uses the original JSON copies in a separate directory with an older
compatible build; it does not downgrade a newer cache schema or recover later edits.

For a damaged/full cache, first close Mail and back up the whole profile. Move the
cache database aside within that backup/recovery workflow and leave profile.json,
accounts.json, settings.json, and drafts.json intact. The next start creates an empty
cache and can receive again. Keeping the old database preserves offline-only copies
for recovery; do not delete the only copy of mail no longer present on the server.
This is local recovery only: it never deletes server mail or saved drafts.
Do not repair corrupt data by deleting the manifest or guessing a schema version.

## Encryption decision

Optional database encryption is deferred. The current provider ships ordinary
SQLite, and password protection in the OS vault does not encrypt cached messages
or exported backups. Before offering encryption, select and validate an encrypted
provider on NativeAOT/device targets, generate a separate random data key protected
by the OS vault, and define a portable password/recovery-key-wrapped key export for
backups. Losing both the OS key and its recovery export must be documented as
unrecoverable; silently falling back to plaintext would not be acceptable. None of
this key-management flow is implemented or implied by the backup checksum feature.

## Validation and remaining work

Shared tests cover byte-preserving adoption, stable account/draft identity, competing
profile leases, invalid/future manifests, interrupted adoption, body/HTML/CID/reply
metadata round trips, epoch/account isolation, cancellation, quota failure, corrupt
database/identity rejection, local paging, and offline restart/missing-body behavior.
The Windows published executable smoke saves headers/body/CID/reply metadata through
the real cache and reopens them in a second process, alongside its credential/MIME
and actual HTML raster checks. No real account/provider is used.

Local results: **731 shared Release tests passed**, including 13 original
profile/cache/offline tests, 12 maintenance/UI cases and 13 download-policy/quota
cases. The focused
Windows smoke exercises verified backup/restore and recovery-backed cleanup as well
as real second-process cache/profile checks. Both Windows x64 package variants pass
that scope, dependency policy (72 runtime libraries), and the renderer audit. The NativeAOT
package includes the required e_sqlite3.dll. No IL2xxx/IL3xxx warnings were observed.
See [M1 evidence](evidence/m1-win-x64/README.md); current ignored logs are
artifacts/m1-policy-shared.log, m1-download-policy-tests.log,
m1-policy-windows.log, m1-policy-aot.log and m1-policy-framework.log.
These are local dirty-checkout validation artifacts,
not provider certification or published releases.

Remaining M1 work: real-device/cross-architecture validation and offline-folder
coverage beyond the current INBOX-only protocol/UI. Retention is explicit keep-until-
cleared with recovery backup; automatic eviction needs a separate proof that it
cannot remove an only local copy. M4/M5 add attachment streaming, search, folders and authoritative sync.
This slice intentionally does not claim those features are implemented.


### M4 starred-state cache upgrade

The current cache uses SQLite schema v3. A verified v2 backup precedes the transactional search-revision upgrade; see [indexed search](m4-indexed-search.md). First opening a v1 cache creates a verified
`mail-cache.db.schema-v1-<id>.backup` beside it before a transactional upgrade;
existing stars default to unset until refreshed from the server. Headers, bodies,
read state and receive times are retained. The extra database snapshot is sensitive
mail content and consumes space outside the configured cache quota. It is retained
for recovery, not automatically cleaned or included as an extra file in logical
profile exports. Those exports already contain their own verified current cache.
Legacy v1 snapshots remain accepted and migrate on opening in a new profile.
See [M4 cache migration and recovery](m4-folders.md#cache-upgrade-and-recovery) for
compatibility and rollback limits.

### Saved-recipient profile data (2026-11-03)

The optional `recipients.json` is now part of the verified snapshot allowlist and
configuration-lock set. Restore validates every saved entry, not only the selected
account, before publishing a separate profile. Missing files in older backups mean
an empty book. Cache cleanup keeps this explicitly saved user data. See
[saved-recipient completion](m4-saved-recipients.md) for limits and validation.

### Multiple-draft retention (2026-11-03)

`drafts.json` now optionally contains up to 20 saved drafts/submission records in
addition to its active autosaved draft. The existing 4 MiB file quota applies to
all records together. Verified snapshots and restores validate the entire library;
cache cleanup keeps it. First use preserves a verified pre-library file, and older
strict readers refuse the additive format. See [multiple drafts](m4-multiple-drafts.md).

### Selected-folder batches (2026-11-03)

The Windows reader now persists up to eight selections per account and explicitly
downloads supported body previews for at most the newest 500 saved headers in each
selected folder. It does not receive headers or start automatic synchronization.
Progress/cancellation/retry retain confirmed saves; profile snapshots include the
selection file. See [five selected-folder items](m1-selected-offline-folders.md).

### Older saved-header batches (2026-11-03)

Selected-folder downloads now offer explicit Next and Restart controls. Each click
covers up to 500 saved headers per unfinished folder; full epoch/UID continuations
advance only after a successful batch. Windows now persists continuation/completion
alongside selections and downloaded bodies. Reload/reopening does not download;
Restart durably resets the walk. See [offline batch paging](m1-offline-batch-paging.md)
and [durable progress](m1-durable-offline-progress.md).

### Account navigation preferences (2026-11-03)

Optional `account-navigation.json` now keeps bounded per-account start/pinned folders.
Verified snapshots validate and restore it; cache clear preserves these explicit
preferences. See [account navigation](m3-account-navigation.md) for scope and limits.

### Per-account downloaded-mail cleanup (2026-11-03)

Manage accounts now provides local counts and separately confirmed, recovery-backed
cleanup of one account's cached headers/body previews. Other accounts, local edits,
settings and credentials stay; only the target account's offline checkpoint resets.
See [per-account cleanup](m3-account-downloaded-mail-cleanup.md).

### Explicit original-source exports (2026-11-03)

The reader can explicitly fetch an original message up to 2 MiB and save unique
`.eml` files in `source-exports`. These independently bounded exports are kept on
cache cleanup but excluded from profile snapshots/cache quota; back them up
separately. See [original source export](m4-original-source-export.md).

Local file imports are irreplaceable account-scoped records in `local-originals.json`, independently keyed by local GUIDs. Verified profile snapshots lock, validate, hash and restore this optional file. Cache cleanup retains it. Old backups without it remain supported; backups containing it need a build with local-original support. See [local originals](m4-local-originals.md).
