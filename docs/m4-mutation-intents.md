# M4: three durable server-outcome subitems

Updated: 2026-11-03. Windows now records MOVE/delete intent before entering the
network adapter and offers recovery review for uncertain outcomes. This extends
[confirmed local repair](m4-cache-recovery.md); it is not an automatic mutation queue.

## 1. Durable MOVE intent and outcome handoff

The Windows receiver writes a validated intent to `mail-cache.db.intents.json`
before connection/authentication or mutation work. Each entry requires a unique ID,
full source account/mailbox/UIDVALIDITY/UID and an explicitly present destination
mailbox (null only for permanent deletion). Failure to persist intent prevents any
network attempt. Duplicate unresolved work for the same key is refused.

Confirmed MOVE publishes a verified destination-key receipt with the same ID before
returning from the adapter, then retires the intent. NotApplied retires the intent;
Unknown or exceptions leave it recorded. A crash before outcome publication leaves
uncertainty, never a command to replay. If both intent and confirmed receipt survive
a crash, startup correlates their IDs and full identities, performs only confirmed
SQLite repair and removes both after success. Conflicting cross-file identities are
refused without changing mail.

## 2. Durable permanent-deletion intent

Deletion uses the same boundary with an explicitly null target. Confirmed outcome
becomes an exact-key local deletion receipt; Unknown stays an uncertain intent.
Startup never treats an uncertain deletion intent as permission to remove cached
mail, mark flags or expunge anything. Existing Trash confirmation, UIDPLUS/scoped
UID EXPUNGE and no broad fallback rules remain unchanged.

The two journals use bounded, source-generated/versioned JSON with atomic flushed
publication and file write locks. Both are included in verified profile backup and
restore under the cache/configuration snapshot locks. Missing intent files in older
profiles mean no saved intents. New/invalid schemas are rejected without reset.
No credentials or message body are stored in intent metadata.

In-process guards cover active network work and persisted outcomes until local
handoff finishes, including cancellation/stale UI callbacks. Further server
mutations stay blocked while saved work needs attention; cached reading and manual
Receive remain available. Active operations cannot be dismissed while their worker
is still finishing. Read/star mutations now use typed intents and receipts too; see [flag recovery](m4-flag-recovery.md).

## 3. Explicit uncertain-outcome review and dismissal

Settings → Storage → Uncertain server actions lists unresolved records with account,
source folder, cached subject (or UID fallback), and intended destination/action.
The user must check the message/provider outcome before choosing Dismiss checked
record. A separate confirmation focuses Keep unresolved record first; the confirm
action is not a default button. Cancel/Keep retains the record.

Confirmation creates a verified profile backup before removing only that uncertain
record. Cached rows/bodies, receive timestamps and server mail are unchanged.
Confirmed receipts cannot be dismissed as uncertain; they require local repair.
The UI reports that no server action was repeated and asks for deliberate Receive
to check current membership. Stale records or still-active operations are refused.
Restoring the backup restores the uncertainty metadata for review, without replay.

## Validation and remaining work

Ten added cases cover confirmed/lost MOVE and deletion, reopened unknown state with
no replay, intent write refusal before network, definite preflight cleanup, explicit
review/Keep/confirm and backup retention, active-operation dismissal refusal,
confirmed-versus-uncertain separation, conflicting receipt identities, and focused
confirmation at 100%/200% text. Existing shared protocol/model/cache suites remain
in the full run.

The NativeAOT protocol probe now journals real fixture MOVE/reverse/delete and
checks receipts before adapter return, plus lost MOVE reopening without another
command over TLS/STARTTLS. Published profile smoke skips uncertain actions during
repair and exercises backup/confirmation/dismissal while retaining the cached row.
Validation passed: 1,048 shared tests, 79 Windows checks, NativeAOT TLS/STARTTLS
protocol fixtures and both Windows x64 publish variants. See [Windows x64 evidence](evidence/m4-three-intent-items-win-x64/README.md).

Read/star uncertainty and cache-flag repair are now implemented in the [follow-up](m4-flag-recovery.md).
Remaining: wider provider/crash
fault testing, broader native accessibility, other-target certification and the
other M0–M4 gaps. Intent journaling is wired in Windows; other heads using the receiver
must explicitly provide the journal. This does not infer a server result after an
unknown outcome, restore deleted server mail or make cached membership current.
Downgrading to builds unaware of these metadata files is not certified.
