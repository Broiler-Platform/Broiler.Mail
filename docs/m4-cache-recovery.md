# M1/M4: three confirmed-cache recovery subitems

Updated: 2026-11-03. These receipts cover confirmed outcomes once cache
reconciliation is invoked. The follow-up [mutation-intent implementation](m4-mutation-intents.md)
now covers MOVE/delete network-phase uncertainty and explicit review. Neither is an
automatic server-operation queue.

## 1. Durable confirmed MOVE repair

Before attempting a verified MOVE's local SQLite reconciliation, the cache writes a
receipt to `mail-cache.db.recovery.json`. It contains a unique receipt ID and exact
source/destination account/mailbox/epoch/UID keys, not message text or credentials.
The existing transaction rekeys headers/body before removing the source. Only after
that transaction succeeds is the receipt removed. A failed/canceled local operation
retains the receipt; a repeated operation deduplicates it. A crash after SQLite
commit but before receipt removal is safe because reconciliation is idempotent.

Windows startup retries recorded local repairs before constructing the reader.
Known destination epoch conflicts, missing usable data or corrupt body data leave
receipts pending; no conflicting row/epoch is guessed or overwritten. Pending counts
are reported in the inbox notice. Further server mutations are disabled until repair,
while offline reading and deliberate Receive remain available. Receive can refresh
the destination before another local repair attempt.

## 2. Durable confirmed permanent-deletion cleanup

Confirmed permanent deletion writes the same durable receipt with an explicitly
present null destination. Local repair deletes only the exact keyed cached row/body;
repetition after the row is already absent succeeds. It never sends STORE, MOVE,
EXPUNGE, SMTP or any other network operation. The server confirmation/Trash consent
rules remain unchanged. Unknown and NotApplied server outcomes never create these
receipts because their callers do not invoke confirmed cache reconciliation.

The independently versioned, source-generated JSON journal uses atomic flushed
publication and a write lock. It accepts at most 128 validated receipts and rejects
corrupt/newer data without reset. Every source/destination field is required,
including nullable destination, so a missing MOVE destination cannot become a
purported delete. Invalid scopes, zero UIDs, malformed mailbox Unicode and duplicate
receipt IDs are rejected. This is separate from the unchanged SQLite v2 schema and
cache quota; pending operation metadata is not disposable cache.

## 3. Recovery-aware profile maintenance

Verified backup/restore now includes the optional receipt file, hashes and validates
it with the same configuration-lock/cache-gate snapshot boundary. Older backups
without receipts remain supported. Restore still targets a separate profile and does
not repeat server operations. Cache cleanup refuses to erase downloaded mail while
receipts are pending.

Settings → Storage offers Repair confirmed local mail changes. It uses the existing
maintenance/draft-flush guards, pauses reader work, repairs local transactions and
reloads cached navigation. The status reports repaired/pending counts. Failed repairs
retain receipts; cancel preserves pending counts and committed work. No receipt is
a promise of current server membership: cached mail still needs deliberate Receive
to check changes by other clients.

## Original increment boundary and follow-up

If the receipt cannot be saved, local reconciliation fails without changing SQLite;
no persistent receipt can be promised for that failure. A crash after server
confirmation but before receipt publication, or during an uncertain server operation,
is still outside this increment. Durable pre-send intent/unknown-outcome tracking
is now implemented for MOVE/delete in the [follow-up](m4-mutation-intents.md).
Read/star uncertainty and typed flag repair are also now implemented; see
[flag recovery](m4-flag-recovery.md). Broader fault acceptance remains open. Session undo is still session-only. Other heads must call RepairAsync
when opening their cache to opt into startup repair; Windows does this now.

## Validation

Six added cases cover failed MOVE retention and successful repair after reopen,
idempotent post-commit MOVE and pre-delete receipts, backup/restore isolation,
cleanup refusal, manual repair UI/status/maintenance events, deduplication,
corrupt/future/missing-destination refusal, cancellation before recording and
pending-work mutation guards with continued offline reading/Receive.

Published profile smoke seeds confirmed MOVE/delete receipts, reopens SQLite,
repairs while retaining the moved body, repeats a post-commit receipt, then backs up,
restores and repairs deletion in the separate profile without altering the source.
Validation passed: 1,038 shared tests, 79 focused Windows checks and both Windows
x64 publish variants, including NativeAOT. See [Windows x64 evidence](evidence/m4-three-recovery-items-win-x64/README.md).
