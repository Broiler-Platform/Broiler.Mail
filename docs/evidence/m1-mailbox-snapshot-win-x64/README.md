# Complete mailbox header observations — Windows x64

Local validation on 2026-11-04 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. This work postdates the earlier local CI snapshot and retained-body search validation. No branch, PR, tag or release was published. Synthetic suites used fixture accounts; a separately authorized read-only IONOS follow-up is recorded below. No additional provider message was sent.

Implemented [complete bounded header observations](../../m1-mailbox-header-snapshots.md): serial read-only before/after UID censuses, validated metadata batches, atomic cache-v5 source/census/checkpoint persistence, retention of absent downloaded mail, and explicit current-folder capture/local inspection.

Coverage includes TLS/STARTTLS 501-header acquisition, empty folders, unordered UID responses, malformed/missing/duplicate/extended census refusal, changed membership, missing flags, response bounds, over-limit folders and cancellation. Storage cases cover restart, empty observations, account/epoch isolation, retained absent bodies, stale-observation rejection, corruption detection, quota and post-header/index-write rollback, verified v4 migration/deferral, profile restore and account/whole-cache checkpoint cleanup. Workflow/UI cases cover disabled local inspection/retry, bounded display/control layout at 100%/200%, explicit actions, quota guidance, foreign result refusal and ignored/queued account/cancel/dispose results.

Final full shared Release suite: **1,611 passed**, including 40 new snapshot cases and one new queued export-cancellation case. Final full Windows Release suite: **346 passed**.

- NativeAOT storage, TLS/STARTTLS protocol and Broiler.Net session probes: **all passed**. The protocol probe verifies production full-header capture and durable checkpoint reopening under both TLS modes.
- NativeAOT/self-contained and framework-dependent application publish/smoke: **both passed**. Retained [NativeAOT manifest](self-contained-build-manifest.json) and [framework-dependent manifest](framework-dependent-build-manifest.json).
- Application dependency policy: **72 runtime libraries**; renderer compatibility audit matched. NativeAOT provider diagnostic dependency policy: **7 runtime libraries**. No IL2xxx/IL3xxx warnings appeared in application, provider or M0 probe publish logs.
- `git diff --check` passed, apart from the existing LF/CRLF notice for `docs/roadmap.md`.

Local packages remain unsigned validation artifacts, with `releaseReady: false` and `htmlRendererIsolation: not-implemented`.

The broader runs exposed a synthetic legacy-v1 migration fixture that still contained v5 tables; its downgrade now removes those tables before exercising the real upgrade. They also exposed contention among 16 source-export writers sharing a three-second interprocess lock retry budget. A bounded set of process-local gates now queues sibling export/catalog/attachment operations before the file-lock deadline starts, with cancellation and trailing-separator path normalization. The interprocess lock and its three-second retry bound remain in place. A queued-cancellation regression proves no receipt is published and subsequent writers proceed. A composer interaction fixture now serializes autosave callbacks through a queued UI dispatcher, matching the native host instead of running background refreshes concurrently with edits. Final focused snapshot/source-catalog/composer regression: **56 passed**.

The native protocol fixture additionally captures through the production TLS/STARTTLS adapter and verifies checkpoint reopen. Published application smoke uses synthetic headers and a receiver whose ordinary receive/body methods throw. It verifies capture, retained offline reading, disabled inspection, separate-profile restore and recovery-backed account cleanup. These fixtures do not certify live providers, native assistive technology, continuous synchronization or other architectures.

## Authorized live read-only follow-up

The NativeAOT provider diagnostic's new explicit `--read-only-header-snapshot` mode passed against the configured IONOS account: **11 INBOX headers** with matching before/after membership. [Sanitized result](provider-results.json), [profile metadata invariance](profile-invariance.json). Existing profile top-level file names, sizes and modification times were unchanged; this is a metadata comparison, not a byte-for-byte content audit.

The tool only loaded the existing selected account and read its IMAP OS credential. It did not open/adopt/migrate the profile, apply a cache checkpoint, fetch bodies/source/attachments, use SMTP, change flags or send mail. It recorded no addresses, message headers/bodies, UIDs, UID-set hashes or protocol transcripts. Reports are CreateNew without overwriting. The prior one-shot self-message authorization was not reused. Live results certify only this bounded read-only observation on one provider/account, not mutation/fault recovery or general provider compatibility.

Ignored logs: `artifacts/m1-snapshot-shared.log`, `m1-snapshot-windows.log`, `m1-snapshot-final-targeted.log`, `m1-snapshot-profile-smoke.log`, `m1-snapshot-probes.log`, `m1-snapshot-aot.log`, `m1-snapshot-framework.log`, `m1-snapshot-provider-publish.log`, `m1-snapshot-provider-live.log`; detailed NativeAOT probe publish logs are under `artifacts/m0/win-x64/`.
