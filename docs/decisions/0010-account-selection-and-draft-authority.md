# Decision 0010: Selected accounts and draft sender authority

Date: 2026-11-02. Status: Windows selection/addition/enable/disable/order implemented;
recovery-backed removal implemented; broader acceptance remains in progress.
Extends decisions 0001, 0002 and 0007.

Windows uses account-collection.json as its authoritative account configuration.
The independently versioned store imports accounts.json once without modifying the
legacy bytes. Profile IDs and credential bindings survive migration. Missing/corrupt
future collections are not silently replaced from the historical legacy copy.

Selection is an explicit persisted ID. The account editor saves and selects a
profile in one atomic store update, so a newly saved identity is also the identity
that reopens. Ordinary low-level saves can preserve selection. Existing generic
single-account stores remain usable for other heads and fixtures.

Switching requires saved account edits and no active account/submission/snapshot
operation. While selection is being persisted, account operations and composition
mutations are unavailable. Successful selection updates the same editor model,
clears typed passwords/test state and refreshes credential presence with generation
checks. Inbox selection cancels prior receive work and invalidates late UI results;
the cache is already partitioned by account, mailbox epoch and UID.

The current single recoverable draft keeps its original AccountId and From address.
Inbox selection never changes those values. Sending requires the matching enabled
account, and switching is unavailable during an in-flight submission. Multiple
drafts and broader job orchestration require subsequent milestone work.

The selector does not automatically receive or send mail. Canceling new-account
setup discards only the unsaved form. Account removal is confirmed separately,
requires a verified recovery snapshot and refuses retained draft owners. Cached
mail is retained; credentials are kept or forgotten by explicit choice. Forgetting
both protocol slots precedes conditional removal of the unchanged selected profile,
so partial cleanup/cancellation leaves the identity listed for repair or retry.
The vault and configuration are not transactional together; partial state is reported.
Enable/disable changes only the saved flag and active operation authority, preserving
data and secret bindings. Reordering preserves selection and does not rebuild the
reader. Both operations use the same unsaved-edit/busy guards as account switching.

## Sender-alias extension — 2026-11-03

The account may explicitly list up to 16 provider-authorized sender aliases. This is
local authorization configuration, not provider provisioning or proof of permission.
Before drafting, the user may choose primary or one configured alias. Account ID and
From address are pinned in the draft and survive restart. The composer and SMTP
boundary require the same enabled account and exact From address still configured;
removing an alias blocks submission without rewriting the draft. Primary sender
comparison retains its exact configured spelling; recipient deduplication excludes
primary and aliases case-insensitively. Credentials/server usernames remain those of
the account, independently of the chosen From address. Automatic reply selection is off by default. An explicit account policy may select
a unique configured address from parsed To/Cc, with explicit user selection taking
priority; no arbitrary header address gains authority. See [alias implementation](../m3-sender-aliases.md).

## Composition defaults extension — 2026-11-03

A saved default From must be primary or a configured alias. New drafts use it unless
the user explicitly chooses another permitted sender. An opt-in reply policy may
select a unique configured primary/alias in visible To/Cc; absent/ambiguous matches
fall back to default and do not infer Bcc/delivery-envelope metadata. A separate
reply signature is also optional. These policies initialize drafts once and never
rewrite restored/existing sender or body. Explicit pre-draft choice is consumed by
the draft, then resets to account defaults for future drafts. See
[composition defaults](../m3-composition-defaults.md).
