# Three M2 reader viewport items: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Packages are unsigned
local validation artifacts, not releases or real-provider certification.

- Shared Release suite: 1,164 passed, including ten added viewport/reader handoff cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 86 passed,
  including seven added cases against the actual HTML viewport adapter.
- NativeAOT TLS/STARTTLS protocol probe passed existing discovery/read, flags,
  verified UID MOVE/reverse/delete/uncertainty, alias/pinned-name SMTP and private-Bcc fixtures.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published HTML raster smoke restores an actual long HTML viewport, transfers its
  relative position to selectable text, moves text to its end and resets a new
  HTML document to the top, alongside real raster/no-script/high-contrast checks.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in application or protocol publish logs.

Added shared cases cover clamped fractions, nonfinite refusal, selection retention,
short documents, hidden/zoom restore and explicit reset, HTML/text handoff, keyboard
End activation and Top, current managed-failure fallback and stale-failure/new-message
isolation. Actual HTML cases cover clamped/nonfinite fractions, hidden restore through
zoom/theme/reflow, explicit new-document reset and Top. Existing responsive/header,
reader keyboard/link, palette, remote-image cancellation/privacy and profile suites
remain passing. All data/transport fixtures are synthetic; no real mail is sent.

Ignored logs: `artifacts/m2-three-viewport-items-shared.log`,
`m2-three-viewport-items-windows.log`, `m2-three-viewport-items-protocol.log`,
`m2-three-viewport-items-aot.log`, and `m2-three-viewport-items-framework.log`.
Both application build manifests are retained alongside this document.

See [three completed scopes and limits](../../m2-reader-viewport.md).
Positions are relative scroll fractions, not exact semantic anchors or persisted
reading bookmarks. Direct HTML selection/copy, full host-lifecycle refactoring,
renderer process containment/native crash/hang recovery, broader native accessibility
and other-target certification remain open. M0–M2 are not declared complete.
