# Three M4 search controls: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Unsigned local
validation packages are not releases or real-provider certification.

- Shared Release suite: 1,012 passed, including twelve added cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT/self-contained and framework-dependent publish/smoke passed.
- Published profile smoke verifies read/unstarred/downloaded matching on a disabled
  synthetic account, then excludes the same message with missing-body filtering.
  Unexpected network requests fail the synthetic receiver.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in either publish log.

Coverage includes all nine read/star combinations, downloaded and missing bodies
on reopened SQLite, keyword-free searches, edited text retaining criteria, submitted
coverage notices, account reset and no network. Existing next/previous/failure retry
cases now retain body availability despite edited controls. At 640x480 and 100%/200%
text, combined filter choices preserve usable focus and result-list space.

Ignored logs: artifacts/m4-search-three-filters-shared.log,
m4-search-three-filters-windows.log, m4-search-three-filters-aot.log and
m4-search-three-filters-framework.log. Both package manifests are retained alongside
this document. Protocol behavior did not change; its standalone probe was not rerun.

See [exact completed scopes](../../m4-search-state-coverage.md). Indexed full-text
search, large-cache responsiveness, broader native accessibility, other-platform
certification and durable mutation recovery remain open.
