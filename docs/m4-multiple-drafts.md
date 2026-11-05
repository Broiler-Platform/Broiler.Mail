# M4: five multiple-draft items

Updated 2026-11-03. The Windows profile-backed composer now supports one active
autosaved draft plus up to 20 other saved drafts/submission records. This completes
five bounded parts of M4's multiple-draft work:

1. **Durable collection storage.** `drafts.json` retains its revisioned active draft and adds an optional `savedDrafts` collection. Existing single-draft files load without conversion and active autosaves/discard preserve saved records. IDs must be unique across active and saved entries. Every snapshot receives the existing identity/state/header/text validation. The complete file remains bounded to 4 MiB, so that limit may be reached before the 20-record cap. CAS revisions and the locked/flushed atomic writer cover park, swap and removal. Before the first library operation, a flushed, byte-verified `drafts.json.before-library-<id>.backup` preserves the prior single-draft file.
2. **Save and close current draft.** Expand Saved drafts and choose Save and close current draft. The active journal flushes first, then the active snapshot moves into the library in one file transaction. The composer becomes free for a new draft. Incomplete recipients and original sender, body, reply threading and submission/Sent-copy metadata survive. Accepted or unknown records can also be saved and closed; no send/retry occurs. A flush, conflict, quota or write failure retains the open text.
3. **Account-scoped browse and atomic resume/swap.** The picker shows only saved records owned by the selected account, with bounded subject, submission state and independent Sent-copy state. Choose Resume selected draft to flush and save any open draft (even one from another account) and activate the selected record atomically. At capacity, swapping still works because it removes one saved record before adding the active one. IDs, pinned account/From, raw recipient edits, threading and body are retained. The existing sender authorization checks still govern submission. A stored interrupted Sending or Pending Sent-copy is treated as Unknown on activation and persisted through the normal journal, never automatically retried.
4. **Separately confirmed local removal.** Remove selected saved draft opens a named confirmation with Keep and a non-default permanent-removal button. Choosing another row/account cancels the confirmation. Only the chosen saved ID is removed; active edits and server mail remain unchanged. This also permits deliberate removal of retained accepted/unknown local records after the user checks the outcome. Such removal does not recall mail or authorize resend and requires an existing profile backup to undo.
5. **Profile and account safeguards.** Verified profile backup/restore validates and retains the complete collection through the existing draft-file lock and checksum path. Cache clearing keeps both active and saved drafts. Account removal refuses any owner of an active or saved draft/submission record before deleting credentials or configuration. Published application smoke exercises the same APIs and controls, reopening, backup/restore, confirmed removal and interrupted-send recovery in NativeAOT and framework-dependent builds.

The additive property is omitted until the library is used. The surrounding
configuration envelope remains schema 1. Older strict application readers reject
files containing `savedDrafts`; use a compatible build or the verified pre-library
backup for rollback. Restoring that earlier file loses later edits, so first keep
a complete current profile backup. No downgrade or silent reset is performed.

## Validation and limits

Seventeen added shared cases cover exact pre-library backup bytes, reopen and
active-save/discard retention, stale/cancelled changes, invalid/duplicate/null saved
records, capacity and swap-at-capacity, raw edits and identities across accounts,
accepted/unknown/interrupted submission and Sent-copy states, separate confirmation,
failed active flush, backup/restore/cache clear, retained-owner removal refusal,
and the library disclosure/confirmation at 640×480 and 1100×720 with a usable body
editor. One existing attachment-reminder fixture now queues UI callbacks to avoid
an autosave/test-thread race found during verification.

See [Windows x64 validation evidence](evidence/m4-five-draft-items-win-x64/README.md).
The collection is local and explicit: there is no scheduled send, automatic replay,
server Drafts synchronization, outbox queue, attachment support or simultaneous
multi-window editing of a single profile. A [visible manual outbox](m4-visible-outbox.md)
now projects retained outcomes without dispatch. General attachments, complete-body search, broader accessibility,
provider/crash/power-loss acceptance and other-platform certification. No JavaScript
or new package dependency is introduced.
