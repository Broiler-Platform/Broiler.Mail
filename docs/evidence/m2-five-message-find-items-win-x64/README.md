# Five M2 selected-message find items: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Packages are unsigned
local validation artifacts, not releases or real-provider certification.

- Shared Release suite: 1,186 passed, including 14 added matcher/find/reveal/copy cases.
- Focused Windows reader/profile/keyboard/theme/remote-image/viewport suite: 87 passed,
  including one new real-HTML shell keyboard integration case.
- NativeAOT TLS/STARTTLS protocol probe passed existing discovery/read, flags,
  verified UID MOVE/reverse/delete/uncertainty, alias/pinned-name SMTP and private-Bcc fixtures.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published message-find smoke selects/reveals a late match in a long text preview,
  navigates/wraps, copies only the original-case match into a private synthetic
  clipboard, verifies zero clipboard reads, handles a busy clipboard, rejects stale
  context and enforces the 1,000-hit bound. It never touches the user's clipboard.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in application or protocol publish logs.

Added shared cases cover literal/Unicode/non-overlapping matching, metacharacters,
query/text/Unicode validation, cap/read-only results, next/previous wrapping,
selection/reveal and original-case copy at 100%/200% text, no clipboard reads,
clipboard failure/current-body guards, CRLF/generated-label offsets, list/reader/
AltGr scope, F3/Shift+F3/Escape, focus return, old control disposal and default HTML
on the next body, and query edits invalidating result navigation/copy. The Windows
case routes Ctrl+F, Enter, F3 and Escape through the shell with the actual HTML
adapter and verifies text selection without opening a link. Existing responsive/
viewport/resource-policy/profile checks remain passing. Fixtures are synthetic;
no real mail is sent.

Ignored logs: `artifacts/m2-five-message-find-items-shared.log`,
`m2-five-message-find-items-targeted.log`, `m2-five-message-find-items-windows.log`,
`m2-five-message-find-items-windows-targeted.log`, `m2-five-message-find-items-protocol.log`,
`m2-five-message-find-items-aot.log`, and `m2-five-message-find-items-framework.log`.
Both application build manifests are retained alongside this document.

See [five completed scopes and limits](../../m2-message-find.md).
This searches one decoded text preview, not rendered HTML, original MIME or an
entire mailbox. Direct HTML selection/copy, full host refactoring, renderer process
containment/native crash/hang recovery, broader accessibility and other-target
certification remain open. M0–M2 are not declared complete.
