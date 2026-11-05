# M4 saved-search paging: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Unsigned local
validation packages are not releases or real-provider certification.

- Shared Release suite: 960 passed, including three new paging cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published profile smoke traverses 501 synthetic saved headers on a disabled
  account, verifies 500 first-page rows then one second-page row, retains the
  submitted filters despite edited inputs, and opens the second-page saved body.
  Unexpected network requests fail the fixture.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in either publish log.

Storage cases cover nonoverlapping exclusive-UID continuation after reopening the
cache, rejected foreign/unscoped cursors, and changed mailbox epochs. Model coverage
checks the fixed text/flags/date interval across a failed-page retry even when the
editable query is blank. The 640x480 search UI cases at 100% and 200% text now drive
Next saved results, transfer focus to Cancel and retain usable completion focus.

Ignored logs: artifacts/m4-search-pages-shared.log, m4-search-pages-windows.log,
m4-search-pages-aot.log and m4-search-pages-framework.log. Both package manifests
are retained alongside this document. Protocol behavior did not change; the
standalone protocol probe was not rerun for this increment.

See [paging scope](../../m4-search.md). Continuation is session-only and current-folder
only; pages are not a frozen snapshot. Full-text/body indexing, custom date controls,
large-mailbox performance, broader native accessibility, other-platform certification
and durable mutation recovery remain open.
