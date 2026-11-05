# M4 retained-body indexed search

Implemented 2026-11-04. New supported message downloads can retain the complete decoded reader-body text separately from the 32,000-character display preview. The optional local FTS5 index can now find phrases after that preview. This is decoded mail-body search, with explicit saved-content coverage; it does not search a server, raw HTML or attachments.

## Retention and completeness

The existing MIME decoder retains its selected non-attachment text body, or its HTML-only text alternative, up to **1,048,576 characters**. Scripts, styles and active resource references are excluded from the text alternative; no renderer/network loader runs during decoding. Preview length and truncation behavior stay bounded independently. MIME parses marked incomplete and texts over the retention limit have no complete retained-text value; they keep their available preview. Empty supported text is retained as empty text, without indexing the reader's placeholder as message content.

The existing **2 MiB encoded message acquisition limit**, per-body **8 MiB serialized cache limit** and configured cache disk quota still apply. This does not add streaming or attachment retention. Data remains in the account/folder/UIDVALIDITY/UID-keyed body record. No new acquisition, background job or account operation occurs merely by searching or rebuilding. Older cached previews cannot grow their discarded text by rebuilding an index; inspect coverage and explicitly reacquire supported mail when needed (for example after recovery-backed local cache cleanup).

Local `.eml` imports/exports and draft/outbox records have separate stores and are not indexed by this mailbox search.

Retained text was introduced in cache format **v4**. Opening v3 first creates and verifies a `mail-cache.db.schema-v3-<id>.backup`, then advances the format in a durable transaction. Current cache **v5** additionally stores [complete header observations](m1-mailbox-header-snapshots.md), with its own verified v4 recovery copy. Existing v1/v2 migrations still preserve their own recovery copies. Backups accept versions 1–5; newer formats fail closed. The format marker makes older applications refuse unfamiliar data. Use a compatible build or restore a pre-upgrade backup into a separate profile. Null retained text is omitted from serialized legacy preview records. A quota-deferred older cache cannot write retained text until it can upgrade.

## Search controls and coverage

Two new field choices are available in Saved search:

- **Indexed complete retained body** matches only complete retained text. Even a filter-only query requires that text to be present.
- **Indexed headers/complete retained body** matches saved sender/subject or complete retained text. Headers remain searchable for messages whose complete bodies are unavailable.

Existing literal and indexed **preview** modes continue to match only previews; adding retained text does not silently widen them. Word phrases use the same quoted/escaped FTS input, Unicode tokenizer and deterministic key paging as before. A result beyond the preview may not be visible in the bounded reader; the coverage notice says so explicitly.

**Sender contains (optional)** is an independent, case-insensitive literal substring filter over the saved sender header. It combines with a body phrase, account/folder scope, UTC date bounds, read/star state and downloaded-body availability. It does not authenticate sender identity and does not interpret SQL/FTS wildcards. Sender-only queries need no keyword. Submitted sender/phrase/field/filter values stay pinned across next/previous pages and retry while the controls can be edited for a future submission. Account changes reset the sender control.

Storage inspection reports complete retained-body documents against all indexed message documents. This is index-wide coverage, including retained older epochs; result scope still excludes non-current epochs and other accounts. The per-search notice distinguishes complete retained text from previews and excludes older preview-only, incomplete, over-limit and uncached bodies.

## Derived index lifecycle

The derived index format is **v2**, adding a separate retained-text column. A v1 index is rejected with rebuild guidance and left intact. Explicit rebuild stages a new v2 index and publishes it only after commit and cancellation checks. Header/body replacement, MOVE and delete maintain valid source/index records in one transaction. Quota or index-write failure rolls back source changes. Disabling removes only the derived index.

Verified profile backups retain the source body text and exclude the disposable index. A separately restored profile can rebuild it without fetching mail. Cache cleanup removes retained text with downloaded mail; recovery backups still contain it. Retained text is plaintext local mail data under the same filesystem protections as the cache.

[Windows x64 evidence](evidence/m4-retained-body-search-win-x64/README.md) records regression, native probe and local package validation. Broader assistive-technology, platform, large real-mailbox latency and power-loss acceptance remain part of the wider reader/platform/recovery gates.
