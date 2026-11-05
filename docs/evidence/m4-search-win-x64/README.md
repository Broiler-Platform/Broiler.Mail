# M4 offline header-search foundation: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Packages are unsigned
local validation artifacts, not releases or provider certification.

- Shared Release suite: 947 passed, including four new local-search tests.
- NativeAOT/self-contained publish and published application smoke passed.
- Framework-dependent publish and published application smoke passed.
- Both published profile fixtures execute offline header search on a reopened
  synthetic SQLite cache, preserving body availability and excluding another account.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in the publish logs.

Logs: ignored `artifacts/m4-search-shared.log`, `m4-search-aot.log` and
`m4-search-framework.log`. Manifests alongside this document identify both packages.
No UI, protocol or renderer behavior changed in this increment; historical focused
Windows/protocol checks are not claimed as newly rerun here.

See [search scope and pending work](../../m4-search.md). Search UI, indexing,
body/date coverage, large-mailbox performance and other-architecture certification
remain open. Durable mutation recovery is also still pending.
