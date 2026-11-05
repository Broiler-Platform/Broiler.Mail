# Retained-body indexed search — Windows x64

Local validation on 2026-11-04 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. This work postdates the earlier local CI snapshot and original-management validation. No branch, PR, tag or release was published and no real mail account was used.

Implemented [complete retained decoded-body search](../../m4-retained-body-search.md), separate preview/full-text modes and an independent sender-header filter. Cache format v4 protects the new body representation with a verified v3 recovery copy; derived index v2 adds a distinct retained-text column and rejects old index formats with rebuild guidance.

Twenty-four new shared cases cover:

- Plain/HTML-only tail phrases beyond 32,000 preview characters, exact retained-text restart and preserved preview semantics.
- Inclusive 1,048,576-character retention bounds, over-limit/incomplete MIME exclusion, Unicode/base64, empty body and exclusion of attachment/script text.
- Account/folder/epoch isolation, replacement/MOVE/delete, quota rollback and coverage counts.
- Unsupported derived-index refusal without deletion, cancellation preserving the old index, verified v3 source migration and future-format refusal.
- Separate-profile backup/restore of retained source text followed by an offline index rebuild.
- Sender/body/date/read/scope combinations, case-insensitive literal matching without wildcards, sender-only queries, invalid input, submitted-filter paging and account reset.
- Accessible named search controls and notices at 100%/200% text scale with bounded search controls.

The shared full Release suite passed **1,570 tests**. The full Windows Release suite passed **346 tests**. The focused search/migration regression passed **107 tests**.

Final native validation:

- NativeAOT storage, TLS/STARTTLS protocol and Broiler.Net session probes: **all passed**. The protocol probe additionally checks real MIME decoding beyond the preview, persisted source-generated body reopening, retained-tail phrase search, sender filtering and complete-body coverage.
- NativeAOT/self-contained and framework-dependent application publish/smoke: **both passed**. Retained [NativeAOT manifest](self-contained-build-manifest.json) and [framework-dependent manifest](framework-dependent-build-manifest.json).
- Both application variants passed the **72 runtime library** dependency policy and renderer compatibility audit. No IL2xxx/IL3xxx warnings appeared in application or M0 probe publish logs.
- `git diff --check` passed, apart from the existing LF/CRLF notice for `docs/roadmap.md`.

Published fixtures use synthetic mail and a receiver that throws on network access. They check retained-only tail matches, sender exclusion, preview-only semantics, coverage notices and cached opening without a fetch. The protocol fixture uses loopback TLS/STARTTLS only; no real provider credentials/account/mail were used. Packages are unsigned local validation artifacts with `releaseReady: false` and `htmlRendererIsolation: not-implemented`.

Ignored logs: `artifacts/m4-retained-search-shared.log`, `m4-retained-search-windows.log`, `m4-retained-search-regression.log`, `m4-retained-search-probes.log`, `m4-retained-search-aot.log`, `m4-retained-search-framework.log`; detailed native probe publish logs are under `artifacts/m0/win-x64/`.

Broader native assistive-technology, other-target execution, large real-mailbox latency and power-loss certification remain wider acceptance work. The [M0–M4 audit](../../m0-m4-completion-status.md) retains the unfinished parent gates.
