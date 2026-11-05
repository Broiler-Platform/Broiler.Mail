# M1: three offline batch continuation items

**Update 2026-11-03:** Windows continuation/completion is now durable. [Durable offline progress](m1-durable-offline-progress.md) supersedes the session-only lifetime/reset descriptions below; bounded paging and epoch guards remain.

Updated 2026-11-03. The selected-folder foundation now supports older saved headers
through explicit bounded batches. Three subitems are complete:

1. **Per-folder key-based continuation.** A batch reads at most 501 current cached headers per remaining selected folder, saves bodies for at most 500, and uses the extra header only to detect more. An unfinished folder's exclusive continuation is the last processed `(AccountId, MailboxId, UIDVALIDITY, UID)` key. Next walks older saved UIDs; completed or empty folders leave the plan. A 1,001-row folder therefore takes three deliberate batches. No header receive, discovery, server mutation or automatic loop is introduced. Newer arrivals are outside a current continuation walk until Restart; newly saved older headers can appear in subsequent pages.
2. **Explicit Next/Restart and completion controls.** After a successful partial batch, the download button becomes Download next offline batch. When the current saved-header walk finishes, download is disabled and the notice explains Restart. Restart offline batches from newest resets only session cursors, enabling a new explicit first batch; it keeps bodies and skips already saved bodies when downloading again. Navigation between reader folders preserves completed continuation state. Folder selection edits, selection Reload, account/profile changes and storage maintenance reset it. Cursors are not persisted or included in backups: after reopening, the user starts from newest again and can walk through cached batches without refetching saved bodies. Folder selections and bodies retain their existing persistence.
3. **Failure/cancellation/epoch-safe advancement.** Proposed cursors are published only after every planned body has been saved or confirmed cached and the operation generation still matches. Failure or cancellation keeps the previous cursor plan, so Retry reprocesses the same page and skips committed bodies. A commit may win cancellation, but cannot make the next attempt skip an unsaved page. Late results after account changes are discarded by the existing generation guards. If a continuation's saved epoch no longer matches the current head (or disappears), it is refused before a body request with guidance to Restart; an old epoch is never silently reused for a new UID. No mutation intent or send is replayed.

## Validation and remaining scope

Eight added shared cases cover multiple batches across overlapping UIDs in different
folders, skipping completed folders, restart without body refetch, older-batch
failure/cancellation with retained cursors and saved bodies, epoch refusal/restart,
selection/reload/maintenance/account invalidation, navigation between batches, and
Next/Restart UI state at 100%/200% text. Existing selection validation, initial-batch
cancellation/stale-result and profile snapshot cases remain passing.

Published profile smoke adds 501 real cached headers, confirms the first 500-body
batch and Next control, fetches the last body in a second batch, restarts without
refetching saved bodies, rejects a changed epoch before network work and downloads
the new epoch only after Restart. This runs in Windows NativeAOT and framework-
dependent packages. See [validation evidence](evidence/m1-three-offline-paging-items-win-x64/README.md).

The command covers supported saved body previews/CID resources, not original MIME
or general attachments. It does not acquire whole-folder headers, schedule sync,
persist continuation checkpoints, resolve folder renames or guarantee a fixed
snapshot while cached headers change. Real-provider/fault/power-loss acceptance,
other-target certification and broader native accessibility remain open. M0–M2
are not declared complete. No JavaScript or new dependency is introduced.
