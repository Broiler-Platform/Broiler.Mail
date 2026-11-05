# Five M4 indexed-search items: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Packages are unsigned
local validation artifacts, not releases or real-provider certification.

- Shared Release suite: 1,065 passed, including seven added indexing/lifecycle cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT protocol probe passed existing TLS and STARTTLS discovery/read, flags,
  verified UID MOVE/reverse/delete/uncertainty and SMTP/private-Bcc fixtures.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published profile smoke actually executes bundled FTS5: rebuild 501 documents,
  indexed phrase first/next pages, downloaded-preview matching, transactional body
  replacement, and disable while retaining saved body content. Existing profile
  migration/backup/restore/recovery and JavaScript-free reader checks also pass.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in application or protocol publish logs.

Added shared cases exercise quoted phrase/field semantics, literal substring
compatibility, account/epoch isolation, reopening, insert/update/body/flags/MOVE/
delete maintenance, stale and corrupt index recovery, injected index-write failure
rolling back source changes, disabling before retry, verified v2 migration backup,
pre-cancelled rebuild preservation, and backup exclusion/restore rebuild/cache clear.
These synthetic checks do not certify full-body coverage, production mailbox latency,
process-kill/power-loss recovery, other platforms or broader accessibility.

Ignored logs: `artifacts/m4-five-index-items-shared.log`,
`m4-five-index-items-windows.log`, `m4-five-index-items-protocol.log`,
`m4-five-index-items-aot.log`, and `m4-five-index-items-framework.log`.
Both application manifests are retained alongside this document. All accounts,
credentials, loopback transports and profile data are synthetic.

See [five completed scopes and remaining limits](../../m4-indexed-search.md).
The parent complete-body full-text search item and other M0–M4 gaps remain open.
