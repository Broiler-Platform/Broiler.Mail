# M4 saved-search header fields and filter-only queries: Windows x64

Validated locally on 2026-11-03 against the dirty working tree. Unsigned local
validation packages are not releases or real-provider certification.

- Shared Release suite: 965 passed, including five additional field/filter cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published profile smoke checks sender-only matching, subject-only rejection of
  sender matches, and an empty-keyword unread/starred query on a disabled synthetic
  account. Its existing second-page workflow also changes the editable field while
  retaining the originally submitted subject scope. Unexpected network requests fail.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in either publish log.

Shared tests cover source-specific case-insensitive literal matching, wildcard-like
characters, blank text with required flags/dates, invalid enum/control inputs,
explicit submission and account reset. The failed-page retry case now changes the
field selector too. The 640x480 view cases at 100%/200% text exercise subject-only
selection and next-page focus without removing usable result-list space.

Ignored logs: artifacts/m4-search-fields-shared.log, m4-search-fields-windows.log,
m4-search-fields-aot.log and m4-search-fields-framework.log. Both package manifests
are retained alongside this document. Protocol behavior did not change; its
standalone probe was not rerun for this increment.

See [search behavior](../../m4-search.md). Full-text/body indexing, custom date
controls, large-mailbox performance, broader native accessibility, other-platform
certification and durable mutation recovery remain open.
