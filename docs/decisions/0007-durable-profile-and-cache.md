# Decision 0007: Durable profile and account-scoped cache

Date: 2026-11-02. Status: profile/identity contracts and initial storage design accepted;
provider validated on Windows x64, additional platform certification pending.
Implements the foundation of M0; does not enable caching or multiple accounts yet.

## Profile and identity

The [Broiler.Net reuse review](../broiler-net-session-profile-review.md) confirms
that its published preview.4 supplies HTTP/cookie sessions, not durable Mail profiles
or IMAP/SMTP session management. Reuse its HTTP/policy layer for the later isolated,
cookie-free resource broker; keep profile/account/cache ownership here. Browser's
internal profile wrapper supplies no missing durable store. The NuGet API/native
probe is part of M0; production broker integration is M2 work.

Keep the existing per-user root and explicit data-directory override. M1 adds a
versioned profile manifest, metadata database, and bounded blob area under that
root. Preserve existing AccountId values, credential bindings, settings, and drafts
when migrating accounts.json/settings.json/drafts.json. Credentials remain in the
platform credential service, never in the cache or profile backup by default.
Do not derive directory names from mailbox names or email addresses.

Use the existing MailMessageKey (AccountId, MailboxId, UIDVALIDITY, UID).
MailboxKey defines an account/mailbox epoch for cache operations; zero UIDVALIDITY,
empty account IDs, and empty mailbox IDs are invalid. Protocol adapters normalize
INBOX and maintain folder rename mappings; arbitrary folder names remain case
sensitive. A UIDVALIDITY reset creates a new scope; stale bodies must never satisfy
new keys. Message-ID and session-only MailInboxCursor are not durable cursors.

IMailCache defines bounded reads and atomic upserts; it is intentionally not wired
to the app before M1. Partial receive batches never imply deletion. Successful
receive timestamps are not incremental-sync cursors. M5 must add capability-aware
checkpoints and reconciliation explicitly. Store operations receive cancellation
tokens, run off the UI thread, and return only after durable commit. Commit may win
a cancellation race; callers must safely repeat idempotent upserts. Never report
downloaded/offline availability before commit. Keep existing IMailReceiver account
parameters, key validation, and cancellation contract rather than adding a second
connection abstraction. A future scheduler owns account-scoped cancellation and
bounded work; account switches discard late results rather than reassigning them.

M1 must implement a single-writer lock, transactional migrations with backups,
refusal to overwrite newer schemas, and disk-full/corrupt-store recovery. Local
drafts and pending operations are not disposable cache. Cache content is sensitive
plaintext unless optional encryption is explicitly implemented; protected passwords
alone do not encrypt mail. Search indexes are rebuildable.

## Engine evaluation and decision gate

| Candidate | Benefits | Costs and required proof |
| --- | --- | --- |
| SQLite metadata + bounded MIME/blob files | Transactions, indexes, paging, schema migration; streaming blobs separately bounds working memory. | Choose an AOT-compatible provider; package/validate native libraries per RID; coordinate blob commit with metadata and recover orphans. |
| Single SQLite database including blobs | Transactional metadata/content boundary; simpler backup snapshot. | Still needs provider/native-library proof; measure large-message streaming, quota enforcement, compaction and backup contention. |
| Versioned JSON metadata + file blobs | Reuses current source-generated JSON and atomic replacement. | Whole-index rewrite, query/index and concurrency complexity; insufficient evidence for large mailboxes or crash-safe multi-file commit. |

The [M0 provider probe](../m0-foundation-status.md) now validates
Microsoft.Data.Sqlite 10.0.14 / SQLitePCLRaw 2.1.13 as Windows x64 NativeAOT.
**Select a single SQLite database for initial mailbox metadata and message blobs**,
using incremental blob I/O and FTS5. Keep current configuration/draft JSON until
M1 defines its backed-up migration. A single transaction for metadata/content is
preferable to implementing a separate orphan/missing-file recovery engine in the
first durable reader. Split blob files remain an optimization if later attachment
workloads justify the added consistency/backup complexity. This revises the original
conditional preference for split storage using the two candidate probes.

Both shapes passed an 8 MiB stream/hash round trip with 64 KiB writes. One local
NativeAOT run measured file writes at 15 ms and SQLite writes at 56 ms, with a
27 MiB process peak; this single synthetic run is not a performance guarantee or
a comparison of complete equivalent mailbox implementations. SQLite gives the
initial design a simpler atomicity boundary at an acceptable unbudgeted prototype
cost. M1 must measure representative mailbox/body workloads before setting budgets.

The original probe was development-only. M1 now uses that provider in Infrastructure
for the Windows cache, with bounded decoded-body JSON blobs (at most 8 MiB serialized)
and source-generated serialization. Streaming source MIME/attachments is still future
work; the initial bounded cache is not an archival `.eml` store. See
[M1 implementation and recovery](../m1-persistence.md).
Provider packaging follows [Microsoft's SQLite bundle documentation](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions)
and the pinned [Microsoft.Data.Sqlite package](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.14);
compatibility conclusions above come from executing the probe, not package metadata alone.
Windows ARM64 and Linux CI lanes are configured but their results remain pending.
Android requires a separate device/provider proof before advertising support.
The production package must retain the required e_sqlite3 native library (the
Windows x64 probe emits e_sqlite3.dll); NativeAOT does not make this a one-file app.

The probe must continue to publish/run NativeAOT on Windows x64/ARM64 and later Linux targets,
exercise transactions/rollback, reopen after interruption, schema upgrade/newer
schema refusal, blob recovery, full disk, concurrent writers, and representative
mailbox paging/search. Record package versions, native assets, licenses, warnings,
and throughput/memory. Android needs its own provider/device proof. If split-store
recovery is too complex, prefer the single-database alternative after measuring it.
Current probe coverage and its limits are in the M0 progress record. Actual cache
implementation/migration contract tests are M1 work; provider probes do not prove
the as-yet unwritten application cache or backup implementation.

## Confirmed cache-repair receipts — 2026-11-03

An optional `mail-cache.db.recovery.json` file durably records verified MOVE/delete
cache reconciliation before the local SQLite transaction and removes receipts only
after success. Atomic flushed/versioned JSON, required fields, bounded validated
keys and idempotent SQLite repair make either side of a local commit recoverable.
Backup/restore includes receipts under configuration locks and the cache gate;
cleanup cannot discard pending work. Windows attempts local repair before showing
cached mail. Unknown server outcomes and crashes before receipt publication remain
outside this boundary and are never replayed. See [recovery scope](../m4-cache-recovery.md).

## MOVE/delete intent extension — 2026-11-03

Windows persists pre-network MOVE/delete intent in `mail-cache.db.intents.json`.
Confirmed outcomes publish same-ID full-key cache receipts before returning; unknown
outcomes remain intent metadata and are never replayed. Startup coalesces matching
intent/receipt identities and repairs only confirmed local work. Backup/restore
locks and validates both files, including cross-file identity consistency. Explicit
uncertain dismissal requires a verified backup and changes no cached/server mail.
Active/pending guards survive the network-to-cache handoff within the process.
See [exact scope](../m4-mutation-intents.md); read/star journaling remains open.

## Typed flag recovery extension — 2026-11-03

Read/unread and star/unstar use the same pre-network intent and confirmed-receipt
handoff with an explicit flag operation, validated flag name and required boolean
payload. Local repair updates only the exact keyed flag and never sends STORE.
Malformed flag records cannot become delete receipts. Intent/receipt correlation
includes operation/flag/value. Unknown states retain the existing backed-up manual
review flow. Legacy mailbox records remain readable; older readers of new payloads
are not certified for downgrade. See [flag recovery](../m4-flag-recovery.md).

## Saved-text indexing increment (2026-11-03)

Schema v3 adds source revision triggers after a verified v2 backup. The optional
SQLite FTS5 index lives in a separate disposable database, outside profile backups.
A valid index participates in the source transaction using attached DELETE-journal
databases with FULL synchronization. Revision mismatches disable indexed queries;
literal search remains available. Rebuild/disable controls preserve source mail.
Coverage is saved headers and downloaded plain-text previews, not complete MIME
or attachments. See [five completed indexing items](../m4-indexed-search.md).
