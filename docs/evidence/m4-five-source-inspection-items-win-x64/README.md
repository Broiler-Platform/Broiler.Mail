# Five M4 inline source-inspection items: Windows x64 evidence

Local validation on 2026-11-03 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned local validation artifacts, not releases/provider certification.

- Shared Release suite: **1,267 passed**, including 14 added formatter/local-read/inspection/reader-focus cases.
- Focused original-source suite: **31 passed**.
- Windows reader/profile/keyboard/theme/remote-image/viewport suite: **87 passed**.
- NativeAOT TLS/STARTTLS protocol probe passed, including verified local export re-read.
- NativeAOT/self-contained and framework-dependent application publish/smoke checks passed; both manifests are retained alongside this record.
- Dependency policy passed with 72 runtime libraries; renderer compatibility audit matched.
- No IL2xxx/IL3xxx warnings observed in application/protocol publish logs.
- `git diff --check` passed apart from the preexisting LF/CRLF notice for `docs/roadmap.md`.

Added cases cover ASCII byte escaping (including raw markup, encoded words, controls/non-ASCII/backslashes and CRLF), input/prefix/output bounds, exact local reads, foreign path/invalid receipt rejection, changed length/checksum and missing/pre-canceled file handling, 100%/200% text Show/Hide and frame bounds/focus/read-only behavior, explicit clipboard writes with zero reads and busy-clipboard feedback, cancellation/Hide/receipt replacement/stale/disposal and queued-success races, wrong-message read rejection, modified-file re-read clearing, and focus restoration on account switch.

Native protocol probe verifies original export re-read identity/octet fidelity in the TLS and STARTTLS fixture flow. Published application smoke explicitly shows/clears the saved-source preview, sees escaped binary bytes, enforces truncation/output bounds, preserves the ordinary reader and confirms no second original-source request. Existing export checksum/retention/backup exclusion and no-script/resource-policy checks remain in the lanes.

Ignored logs: `artifacts/m4-source-inspection-build.log`, `m4-source-inspection-targeted.log`, `m4-source-inspection-shared.log`, `m4-source-inspection-windows.log`, `m4-source-inspection-protocol.log`, `m4-source-inspection-aot.log`, `m4-source-inspection-framework.log`.

[Scope and remaining work](../../m4-original-source-inspection.md). Inspection is a normalized/escaped prefix of the current export; export browsing after restart, arbitrary `.eml` open/import, complete attachment handling, printing, other-target/provider/accessibility acceptance and renderer containment remain open. M0–M4 are not declared complete.
