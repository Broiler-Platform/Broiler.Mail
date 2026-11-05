# M4 reader saved-search validation: Windows x64

Validated locally on 2026-11-03 against the dirty working tree. Unsigned local
validation packages are not releases or real-provider certification.

- Shared Release suite: 953 passed, including six new search model/view cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT/self-contained and framework-dependent publish/smoke both passed.
- Published profile smoke drives the real inbox model through current-folder search,
  opening a cached body and closing search on a disabled synthetic account. The
  synthetic receiver rejects unexpected network requests.
- Native dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in either publish log.

Model coverage includes saved and missing bodies, disabled accounts, unread/star
filters, empty results, account switching and late search completion after cancel
or account change. View checks at 640x480 and 100%/200% text verify focused input,
Cancel during work, usable completion/close focus, and remaining list space.
The expanded search toolbar replaces normal mail actions to preserve that space.

Ignored logs: artifacts/m4-search-ui-shared.log, m4-search-ui-windows.log,
m4-search-ui-aot.log and m4-search-ui-framework.log. Both package manifests are
retained alongside this document. Protocol behavior did not change; the standalone
protocol probe was not rerun for this increment.

See [search behavior and remaining work](../../m4-search.md). Search is limited to
saved sender/subject headers in the current folder. Full-text indexing, date/body
coverage, broader native accessibility, other-platform certification and durable
mutation recovery remain open.
