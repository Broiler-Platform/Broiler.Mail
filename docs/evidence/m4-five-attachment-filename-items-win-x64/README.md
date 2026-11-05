# Five M4 attachment-filename items: Windows x64 evidence

Local validation on 2026-11-04 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned validation artifacts, not releases/provider certification.

- Shared Release suite: **1,344 passed**, including **39 new filename cases**.
- Windows reader/profile/keyboard/theme/remote-image/viewport suite: **87 passed**.
- Published NativeAOT TLS/STARTTLS protocol fixture: **both passed**, including Latin-1 extended-name extraction and exact saved bytes.

- NativeAOT/self-contained and framework-dependent application publish/smoke: **passed**; [NativeAOT manifest](self-contained-build-manifest.json) and [framework-dependent manifest](framework-dependent-build-manifest.json) retained.
- Dependency gate: **72 runtime libraries**, renderer compatibility audit matched.
- No IL2xxx/IL3xxx warnings in protocol or either application publish log.
- `git diff --check` passed apart from the preexisting LF/CRLF notice for `docs/roadmap.md`.

Regression coverage includes UTF-8/Latin-1/ASCII names, literal plus signs, extended-over-basic precedence, mixed and reordered continuation segments, split multibyte sequences, quoted delimiters and escapes, legacy RFC2047 compatibility without double decoding, Content-Type-only named text, malformed named-text isolation, invalid/ambiguous parameters, unavailable charsets, byte/segment bounds, path/control/format sanitization and exact local save bytes.

Published application smoke uses the actual reader controls to decode a continued UTF-8 filename and save unchanged binary bytes without another receiver request. The NativeAOT protocol fixture receives a Latin-1 extended filename over both TLS and STARTTLS and verifies its inventory name, saved bytes and no extra IMAP commands. Fixtures are synthetic; no real mail, credentials or file opening is involved.

Ignored logs: `artifacts/m4-attachment-names-targeted.log`, `m4-attachment-names-shared.log`, `m4-attachment-names-windows.log`, `m4-attachment-names-protocol.log`, `m4-attachment-names-aot.log`, `m4-attachment-names-framework.log`.

[Scope and remaining work](../../m4-attachment-filenames.md). Originals remain bounded to 2 MiB/32 attachments. Compose/send attachments, streaming/larger mail, destination selection, import/export browsing, broader provider/accessibility/other-target acceptance and renderer containment remain open. M0–M4 are not declared complete.
