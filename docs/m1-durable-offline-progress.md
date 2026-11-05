# M1 — Durable selected-folder download progress

Implemented 2026-11-03. Five items:

1. Store bounded per-account continuation keys in the same atomic, locked, versioned `offline-folders.json` document as exact folder selections. Optional `remaining` is absent/null for a new walk, empty for a completed walk, or at most one full account/mailbox/UIDVALIDITY/UID key per unfinished selected folder. Keys require nonzero epoch/UID and exact account/folder membership; no folder name becomes a path. Existing 32-account, eight-folder and 512 KiB limits remain.
2. Resume manual Next batches and retain completion after reopening, account switching and Reload. Loading progress never receives headers or downloads bodies. Completed/empty folders are skipped. New arrivals require explicit Restart; this is a walk of saved headers, not complete-folder synchronization.
3. Persist progress only after every planned body is saved or confirmed cached. Failed/canceled body or checkpoint writes leave the prior plan and confirmed bodies available; Retry skips those bodies. Selection comparison inside the write lock rejects changed selections. Continuation epoch checks remain mandatory before fetching. Corrupt/future configuration disables editing/downloads until repaired and Reloaded; ordinary reading/receive remains available.
4. Make Restart asynchronous and durable before enabling a newest-first walk. Failed reset keeps the previous plan, reports failure and preserves all bodies. Successful selection edits atomically clear progress. Ordinary storage maintenance keeps progress; no automatic server command or deletion is introduced.
5. Include progress in existing verified profile snapshots/restores and published execution. Cache clear takes its recovery snapshot first, then resets all checkpoints under the configuration locks before deleting cached messages. An interruption can repeat work but cannot retain a completed checkpoint for cleared bodies. The recovery snapshot retains the previous checkpoints and mail together.

## Compatibility and limits

Legacy documents without `remaining` need no rewrite on load. Existing strict older binaries reject a document containing this optional field rather than silently dropping progress. Keep a verified profile backup before downgrading; this change does not make older readers understand the new field. Resetting progress omits the field again. No credentials, queries, original MIME or general attachments are stored here.

Hosts implementing only `IOfflineFolderStore` keep the previous session-only behavior; Windows uses the optional `IOfflineFolderProgressStore`. Each explicit batch remains at most 500 saved headers per folder. There is no scheduler, durable server sync checkpoint, deletion reconciliation, attachment retention or automatic resume. Application shutdown during a body batch replays only local planning after the next explicit click; it never automatically sends or mutates server mail.

Validation and package manifests: [evidence](evidence/m1-five-durable-offline-items-win-x64/README.md).

The separate [selected-folder header controls](m1-offline-header-acquisition.md) now acquire older headers manually. Their server continuations are session-only and independent of these durable saved-header/body checkpoints. Restart body batches explicitly to include newly received headers.
