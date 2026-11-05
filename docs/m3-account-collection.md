# M3 accounts and identity progress

Updated 2026-11-02. Windows startup, account selection, add/edit/setup and draft
sender guards are connected to the versioned collection. M3 is not complete;
broader acceptance, aliases and per-account orchestration remain pending. M2's containment
gate also remains open.

## Windows workflow

The **Mail account** selector above the inbox shows the active display name/address
and marks disabled profiles. Selection is remembered across restart. **Add account**
opens the existing setup form; **Save account** atomically adds and selects the new
identity, after which its IMAP/SMTP passwords and tests use its own credential keys.
First-account setup can be reopened without discarding already typed details.

To abandon a new identity before saving, close setup and choose **Cancel new account**.
This explicitly discards the new form and restores the previous selected account.
Ordinary switching refuses unsaved account edits and active account/send/storage
operations. The selector participates in keyboard Tab order and is unavailable
behind modal dialogs. Switching clears unsubmitted password text, resets test and
credential-presence state, and loads the selected form without silently converting
stored unsupported authentication modes.

Receive work for the old identity is canceled on selection; late results cannot
repaint the new inbox. Cached INBOX records remain partitioned by account even with
overlapping UIDs. Disabled accounts can inspect cached mail but cannot receive or
start a composition. No receive is automatically started by choosing an account.

**Manage accounts** opens bounded, scrollable controls for the selected identity.
**Disable account** cancels receiving and prevents new receive/send actions while
retaining configuration, credentials, cached messages and the current draft.
**Enable account** restores eligibility without automatically receiving or sending.
An active submission/account operation or unsaved account edits makes management
unavailable. Disabled identities remain selectable for offline reading.

**Move account earlier/later** changes only display order. It retains the selected
ID, current reader, credential/test state and draft sender. Order persists across
restart; boundary buttons disable and leave focus on a usable control. The management
area scrolls within a bounded share of the window, including at 640 x 480 with
200 percent text size. Closing it returns focus from any hidden action.

There is still one recoverable draft per profile. Switching retains its original
From address and account ID; Send is disabled until the matching enabled account
is selected again. Submission already in progress prevents switching. The draft
is never silently retargeted to the newly selected account.

`IAccountCollectionStore` extends account storage with a durable selected ID and
explicit account ordering. `JsonAccountCollectionStore` stores an independently
versioned, source-generated JSON envelope in account-collection.json. It supports
up to 32 non-secret profiles, preserving stable IDs, server configuration, order,
display names and enabled/disabled state. Editing by ID retains the position;
new identities append. The editor uses atomic save-and-select, while low-level saves
can preserve the selection. Selecting a disabled account is permitted as
configuration state, not permission to connect or send.

## Import and write guarantees

The first collection access imports the current legacy accounts.json under its
existing writer lock. The legacy file is left byte-for-byte unchanged. Publishing
the collection uses the normal same-directory flushed temporary file, independent
writer lock and atomic replacement. Concurrent imports keep the winner's published
collection; subsequent updates reread under the collection lock to avoid lost writes.

An empty saved collection is authoritative and never reimports removed legacy
accounts. Unsupported schemas, duplicate IDs, unknown fields, missing selection,
invalid profiles and oversized collections are refused before overwriting data.
Invalid reorder/selection requests and cancellation preserve the current file.
There is no fallback that silently resets a damaged collection to the legacy copy.

The original legacy file is an import snapshot, not a second writable copy kept in
sync. Older application versions continue to show that historical single-account
configuration; they do not understand collection edits. Do not use an older build
as a way to edit or downgrade the new collection.

## Backup and removal boundaries

**Remove account…** opens a second, account-named confirmation. Choosing **Remove,
keep passwords** or **Remove, forget passwords** first creates a verified full
profile recovery snapshot. The resulting backup folder is shown in the account
status. Downloaded mail stays in the database; no server mail is changed. Selection
moves to the first remaining account in saved order. Removing the last identity
leaves an authoritative empty collection and a reachable **Add account** action.

Removal is refused while the selected identity owns any retained draft/submission
record, or draft data is unreadable. Resolve it explicitly in Compose first. An
unrelated account's draft is flushed and retained. Receive/download jobs pause during
the operation, and current-account and composition mutations remain guarded.

The forget choice deletes both account/protocol credential slots, including an old
SMTP slot when outgoing settings are no longer configured. No other account's
slots are deleted. Passwords are never exported into the backup. Password deletion
precedes the conditional configuration removal: if cleanup partly fails or is
canceled, the account remains listed and may already need a password saved again.
Retry is safe; the UI reports this partial state and keeps the completed snapshot.
The OS vault and JSON file are not one transaction, so forgetting cannot be rolled
back. Cancellation racing the final committed replacement may lose that race.

The store removes only an unchanged, still-selected profile under its writer lock.
Changed confirmations and failed backups refuse removal. Restore uses the existing
separate-profile recovery flow; forgotten passwords must be entered again. Local
per-account cache purging is deliberately not part of this action.

Profile snapshots now lock, validate, hash, copy and restore account-collection.json
when present, alongside the existing configuration. Old profiles/backups without
the optional file remain valid and do not create it during snapshot validation.
Backups with the added file require a build that understands it; older restore
implementations reject the additional manifest entry. The backup allowlist's count
limit follows the allowlist rather than the old fixed five-file ceiling.

Low-level collection removal changes non-secret configuration only. The management
service adds verified backup, draft validation, explicit credential choice and
conditional removal. No live account connections are made by these operations.

## Validation and next work

Tests cover byte-preserving import, stable identities, order/selection/disabled-state
restart, concurrent store instances, explicit empty state, future/corrupt data,
duplicate IDs, invalid selection/reorder, cancellation, account limits and verified
backup/restore with all configuration files present. Published Windows profile smoke
adds a synthetic second account, changes selection/order, reopens the collection,
and verifies its recovery snapshot. It also exercises selected-account startup,
switching, pinned draft sender authority and selector/shell layout without networking.

Local validation: 776 shared Release tests, 79 focused Windows checks, and
both Windows x64 package variants pass. See [retained evidence](evidence/m3-win-x64/README.md).

Next: finish broader native UI/account
switch acceptance, per-account status/job orchestration, aliases and sender choices.
OAuth and multiple drafts remain later milestone work; other platform heads are
not certified by this Windows implementation. Synthetic tests do not certify live
provider authentication or sending.

Account removal now [checks unresolved mail changes and confirmation stability](m3-account-removal-guards.md) before/after backup and before final configuration removal. Persisted sender overrides compare by content rather than collection reference. These guards do not replay server operations or make vault/configuration writes one transaction.
