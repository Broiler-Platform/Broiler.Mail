# M4: three durable flag-recovery subitems

Updated: 2026-11-03. This extends [MOVE/delete intents](m4-mutation-intents.md)
to read/unread and star/unstar without adding automatic server replay.

## 1. Durable read/unread intent

The Windows receiver writes a typed read-state intent before connection or mutation
work. It records the full source key, flag `seen`, and requested boolean, including
false for unread. Confirmed state (including already-confirmed state) publishes a
same-ID typed cache receipt before returning. NotApplied retires intent; Unknown or
exceptions retain it for review. Failing to persist intent prevents a network attempt.
Original UID/epoch, permanent-flag permission, incremental STORE and re-fetch
verification rules remain unchanged.

## 2. Durable star/unstar intent

Star state follows the same boundary using flag `flagged` and an explicit boolean.
It shares source-key active/pending guards with other mutations. Lost acknowledgement
is never a request for another STORE. The requested read/star state is named in the
uncertain-action review list. Only primary flag names and complete boolean payloads
are accepted; arbitrary flags are not supported.

## 3. Typed confirmed flag repair and uncertain review

Local cache updates now write typed flag receipts before their exact-key SQL update
and retire them after successful commit. Startup/manual repair updates only the
named flag. Other flags, cached bodies, row identity and successful receive timestamps
are preserved. A missing header leaves the receipt pending until a deliberate
receive supplies that exact account/folder/epoch/UID; it does not invent a row or
apply the value to another epoch.

A `flag` operation discriminator, supported flag name, nonnull boolean and null
mailbox destination are validated together. Missing or mixed payloads are refused
rather than falling through to deletion. Legacy MOVE/delete records use the legacy
mailbox operation default. Correlated intent/receipt IDs must also match operation,
flag and value. Source-generated JSON and existing atomic publication/backup locks
are reused; SQLite schema is unchanged. Downgrade to older ledger readers is not
certified.

Unknown flag intents are skipped by local repair. The existing Settings review,
Keep/confirmation, active-operation refusal and verified-backup-before-dismissal
flow now supports all four flag actions. Dismissing metadata changes no cached flag
or server state. Cached reading and manual Receive remain available while pending
work prevents further mutations. New intents/receipts remain visible through the
network-to-cache handoff and cancellation just as for MOVE/delete.

## Validation and remaining scope

Ten added cases cover read/star × true/false × confirmed/lost outcomes, reopening
without replay, retained body/other flag/receive time, missing-header repair after
receive and incomplete flag-payload refusal without deletion. The NativeAOT protocol
probe now saves fixture headers, reconciles confirmed read/unread/star/unstar in
SQLite and retains a lost-star intent across reopening without another STORE over
TLS and STARTTLS. Existing MOVE/delete/SMTP/TLS invariants stay in the same probe.

Published profile smoke repairs typed read/star receipts and exercises backed-up
confirmation/dismissal of an uncertain star while retaining cached mail.
Validation passed: 1,058 shared tests, 79 Windows checks, NativeAOT TLS/STARTTLS
protocol fixtures and both Windows x64 publish variants. See [Windows x64 evidence](evidence/m4-three-flag-recovery-items-win-x64/README.md).

Remaining: broader provider/process-crash/power-loss fault acceptance, other-target
certification, broader native accessibility and remaining M0–M4 features. These are
local cached observations of previously verified server state, not guarantees of
current server flags after another client acts. Deliberate Receive checks freshness.
Journaling is wired in Windows; other heads must provide the intent journal and
invoke local repair when opening their profile.
