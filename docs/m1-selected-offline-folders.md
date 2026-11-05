# M1: five selected-folder offline items

Updated 2026-11-03. Windows now supports an explicit, bounded manual download
across selected folders. Five subitems are complete:

1. **Persistent account-scoped selections.** `offline-folders.json` stores up to eight exact mailbox identities per stable account ID, using schema-1 source-generated JSON, locked read-modify-write updates and flushed atomic replacement. The file is optional, bounded to 512 KiB and at most 32 account selection groups. Folder identities have a 1,024-character limit, valid Unicode, no controls or duplicates; they are never filesystem paths. Reading a missing file starts empty without creating it. Corrupt or future files disable selection editing/batches until Reload succeeds; saved reading and normal Receive remain available, and no file is silently reset.
2. **Reader selection controls.** Expand Offline folders, choose a folder in the existing picker, and select/unselect it for offline batches. A marker identifies selected folders, and the notice lists selections and coverage. Unselecting keeps downloaded mail. Saved selections remain navigable even if a folder has not been rediscovered; they do not prove that the server folder still exists. Disabled accounts can review/edit selections and read saved mail but cannot download. Reload retries selection loading. The disclosure keeps the main toolbar compact and exposes named keyboard controls.
3. **Explicit bounded multi-folder download.** Download selected offline folders snapshots this account's selections and plans from current saved mailbox heads only. It considers the newest 500 saved headers per selected folder, skips saved bodies and saves missing supported decoded bodies/CID resources before reporting availability. It never receives headers, discovers folders, changes the selected folder/message, sends mail or starts background synchronization. Empty/missing saved folders and the 500-header cap are reported. The first batch covers the newest 500 saved headers; [manual continuation](m1-offline-batch-paging.md) now walks older saved headers in additional bounded batches.
4. **Progress, cancellation and explicit retry.** Progress reports confirmed saved-body availability and the current folder. The first read/network/save/epoch error stops the batch while retaining successful saves. Retry selected-folder download replans the selected folders and skips bodies already saved. Cancel, account/folder switching, storage maintenance and disposal invalidate late progress/results; a receiver that ignores cancellation cannot save its late body. Full account/folder/UIDVALIDITY/UID identity and the current cached epoch are checked before saving. No server mutation is replayed. A commit may win a cancellation race; completed saved content remains, and retry discovers it.
5. **Profile backup/restore and release integration.** The optional selection file participates in configuration locks, checksum verification and full-file validation before backup or separate-profile restore is published. Older backups without it mean no selections. Cache clearing and account removal retain selection preferences; a newly created account ID does not inherit a removed account's selection. Windows wires the store under its profile root. Published profile smoke downloads across two fixture folders, constructs the controls, restores selections and saved bodies, unselects without deleting mail and reads cached Archive mail with the account disabled and no network.

## Coverage and validation

This is saved-header/body-preview coverage, not a full mailbox mirror. Original
MIME, general attachments, text beyond preview bounds, authoritative synchronization,
automatic refresh/scheduling/retention and
folder rename resolution remain open. Quota failures stop without eviction or
clearing drafts/pending changes. Selections never bypass the existing headers-only
policy automatically: the user must deliberately request this explicit download.

Twenty added shared cases cover account-isolated persistence/concurrent updates,
validation and no-reset cancellation, corrupt/future files with ordinary mail still
usable, multiple folders with overlapping UIDs, saved-body skipping, retained saves
and retry, cancellation/account switching with late bodies, epoch changes, the
500-header cap/empty coverage, unselection/disabled accounts, late preference loads,
backup/restore/cache clear and keyboard Cancel focus at 640×480 with 100%/200% text.
The Windows suite and both published variants run the synthetic integration fixture.
See [Windows x64 validation evidence](evidence/m1-five-offline-folder-items-win-x64/README.md).

Other-target certification, broader native accessibility, real-provider/fault/power-
loss acceptance and remaining M0–M3 items are not closed by these checks. No
JavaScript, runtime code generation or new package dependency is introduced.

## Continuation extension (2026-11-03)

Next/Restart now extend the initial 500-header cap through additional bounded,
explicit batches. Session cursors advance only after success and are refused after
epoch changes. See [three continuation items](m1-offline-batch-paging.md) for the
new behavior and evidence; the evidence above records the initial foundation.

Windows manual continuation/completion now survives restart; see [durable offline progress](m1-durable-offline-progress.md) for reset, backup and compatibility details.

A separate [manual selected-folder header acquisition workflow](m1-offline-header-acquisition.md) now receives bounded header pages explicitly. Body batches above still use saved headers only; automatic/authoritative synchronization remains open.
