# M3 independent account receive queue

Implemented 2026-11-04. **Show account receives** in the account selector exposes an explicit session-only header receive queue. Opening Mail, expanding the section, selecting an account, re-enabling an account or reopening a profile starts no network job.

**Queue enabled accounts** requests one latest INBOX header page for each currently configured enabled account. **Queue selected account receive** acts on the activity picker's account, independently of the main reader's account selector. Each job requests at most 50 headers and saves them before reporting success. It never fetches bodies/attachments, sends messages, creates Sent copies or performs mailbox mutations. Cached bodies and older headers remain retained; this is not a complete-folder census or continuous synchronization.

## Scheduling and isolation

There are two receive slots and at most one outstanding job for each of up to 32 configured account IDs. Jobs dispatch in FIFO order; completion order can differ. A repeated request for an outstanding account coalesces rather than starting a second connection. Completed/failed accounts can be queued again only by an explicit request. One slow/failing account leaves the other slot available to subsequent accounts. Production IMAP operations retain their existing transport deadline; there are no automatic retries or polling timers.

Each account has independent queued/receiving/canceling/completed/failed/canceled/configuration-changed status, confirmed header count, completion time and detail. Errors stay in the activity section and do not replace another inbox's error or switch its reader. Activity selection never changes the selected account, current message, draft identity or pinned sender. Results are read later through the existing saved-header controls; the reader is not forcibly refreshed.

The pending queue removes canceled/replaced entries even while both slots are held. Repeated cancel/requeue cannot accumulate stale pending work. UI notifications coalesce until the dispatcher can show current state. Roster replacement removes canceled, unstarted old-account entries immediately. At most two removed-but-still-canceling workers can remain alongside the 32 configured account jobs; their capacity is released only on completion. No active operation is silently abandoned to make room for new work.

## Configuration and credential boundaries

Every request captures an immutable non-secret account configuration, including a copied sender-override collection. Before connecting and before saving, the job reloads the saved collection and checks the same enabled account ID and configuration. Removed, disabled or changed configurations reject the result. The production IMAP adapter independently reads the account's configuration-bound IMAP credential; it does not read SMTP credentials for these jobs.

Application account-store writes and final receive validation/cache publication share a short async gate through `CoordinatedAccountCollectionStore`. Network I/O never holds it. Saving an account can proceed while its connection is waiting; a stale result is then rejected. A configuration write that overlaps a validated cache commit waits until that commit completes. The application's exposed account store, editor and account-management paths use the same coordinator. This is process-local coordination under the existing profile ownership model, not a transaction across arbitrary external file writers.

Password/app-password IMAP is supported by the existing adapter. OAuth sign-in/token refresh remains unsupported and fails with existing guidance; no OAuth credential lifecycle is claimed here.

Ordinary header pages now use the same bounded metadata reader as complete header observations: 128 KiB per response line, 2 MiB aggregate response characters and a requested-message-count plus 33 response-line limit (including the tagged completion). Oversized responses fail before cache publication. This bounds the FETCH metadata path; it does not claim containment of every protocol command or continuous synchronization.

## Cancellation, maintenance and restart

**Cancel selected account receive** affects only that job. **Cancel all account receives** clears pending work and requests cancellation of active transports. A queued cancellation never connects or publishes a receipt. An active job stays Canceling until its operation acknowledges completion; a provider that ignores cancellation continues to occupy its slot. Production transport deadlines and cooperative I/O apply. Late results are checked before caching, and disposed views cannot be repainted by queued notifications. Closing the shell/application requests cancellation.

A cache save committed just before cancellation can remain. The notice directs users to inspect saved headers before another request. Canceling does not delete downloaded mail or undo an already committed save.

Account switching and draft editing remain available. Removal, account/orphan mail cleanup and local storage operations wait for queued/active/canceling receives to finish. Existing compose/draft/recovery guards still apply. New jobs are refused while storage work, unsaved account edits, account operations or destructive confirmations are active. This prevents a late background receive from repopulating a cleared account during the UI's recovery workflow.

Jobs and failures are session-only. Backups contain saved mail/configuration, not a replayable receive queue. Restart opens saved mail and requires another explicit queue request. Background operation here means while this application process is running; no OS service, notification or M5 periodic scheduler is installed.

[Windows x64 validation](evidence/m3-account-receive-queue-win-x64/README.md) covers provider/error isolation, real TLS/STARTTLS credential bindings, FIFO progress with a held account, cancellation, config-write ordering, queue bounds, roster replacement, UI dispatch, maintenance, pinned drafts and NativeAOT smoke. Other job types' continuous/full-folder synchronization and broader provider/target acceptance remain in the [completion audit](m0-m4-completion-status.md).
