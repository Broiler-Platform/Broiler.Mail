# M4 Windows x64 persistent stars validation

Validated locally on 2026-11-02 against the dirty working tree. These unsigned
validation packages are not releases or real-provider certification.

- Shared Release suite: 870 passed (20 additional protocol/migration/model/UI cases).
- Focused Windows reader/profile/keyboard/theme/remote-image checks: 79 passed.
- NativeAOT protocol probe passed implicit TLS and STARTTLS: existing read-only
  receive/body behavior; explicit read/unread/star/unstar; preservation of Seen
  when changing Flagged; and dropped star acknowledgement reported Unknown with
  one STORE and no automatic retry. No CLOSE/EXPUNGE is sent.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
  The fresh synthetic profile fixture recreates a v1 cache layout, upgrades it on
  reopen and drives both flag changes through the real application/SQLite path.
- Dependency policy passed with 72 runtime libraries; renderer package audit matched.
  No IL2xxx/IL3xxx warnings were observed in the final application publish logs.

Migration coverage includes a verified retained pre-upgrade v1 database backup,
read-only validation of old snapshots without modifying bytes, transactional upgrade
and migration failure without version advancement, preservation of bodies/read
state/receive times, new cache instances reopening stars and scoped row updates.
Shared mutation cases cover Confirmed/NotApplied/Unknown, cancellation/stale folder
results, cache-save failure, and reader actions at 640x480 with 100% and 200% text.

One concurrent build hit an intermediate PDB file lock; the final Windows tests
and framework package were run sequentially after NativeAOT completion and passed.

Logs remain in ignored artifacts/m4-stars-shared.log, m4-stars-windows.log,
m4-stars-protocol-aot.log, m4-stars-aot.log and m4-stars-framework.log.
See [M4 progress and recovery limits](../../m4-folders.md). Movement/deletion,
provider/native accessibility acceptance, durable operation history and other-platform
certification remain open. Recovery snapshots are sensitive database-only files,
retained beside the cache and outside its configured quota.
