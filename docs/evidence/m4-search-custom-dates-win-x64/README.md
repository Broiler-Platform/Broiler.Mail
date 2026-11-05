# M4 custom UTC search intervals: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Unsigned local
validation packages are not releases or real-provider certification.

- Shared Release suite: 988 passed, including nine added date-range cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published profile smoke submits a custom UTC interval, checks its matches, then
  verifies that an invalid edited interval disables Find without altering results.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in either publish log.

Model cases include invalid calendar/control input, equal/reversed bounds, UTC
midnight boundaries, open endpoints, leap day, filter-only queries and account reset.
The failed-page retry case now captures custom dates and retains them despite edited
controls. Search UI cases at 640x480 and 100%/200% text select custom mode, populate
the range editor and traverse a next page while preserving usable focus/list space.

Ignored logs: artifacts/m4-search-custom-dates-shared.log,
m4-search-custom-dates-windows.log, m4-search-custom-dates-aot.log and
m4-search-custom-dates-framework.log. Both manifests are retained alongside this
file. Protocol behavior did not change; its standalone probe was not rerun.

See [search scope and remaining work](../../m4-search.md). Indexed full-body search,
large-cache performance, broader accessibility/target certification and durable
mutation recovery remain open. M0–M4 are not collectively certified complete.
