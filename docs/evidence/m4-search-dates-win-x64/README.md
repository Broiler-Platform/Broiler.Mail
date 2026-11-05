# M4 saved-search received-date filtering: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Packages are unsigned
local validation artifacts, not releases or provider certification.

- Shared Release suite: 957 passed, including four new date-filter cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Both published profile fixtures exercise the actual inbox search model with the
  7-day filter and open a saved synthetic body on a disabled account without network.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in either publish log.

Coverage includes inclusive start/exclusive end down to a tick, equivalent UTC
instants with different offsets, unknown dates, one-sided bounds, invalid intervals,
rolling windows, explicit resubmission and account reset. Existing 640x480 search
view cases now select the dated query at both 100% and 200% text.

Ignored logs: artifacts/m4-search-dates-shared.log, m4-search-dates-windows.log,
m4-search-dates-aot.log and m4-search-dates-framework.log. Both package manifests
are retained alongside this document. Protocol behavior did not change; its
standalone probe was not rerun for this increment.

See [search scope](../../m4-search.md). Full-text/body indexing, custom date inputs,
large-mailbox latency, broader native accessibility, other-platform certification
and durable mutation recovery remain open.
