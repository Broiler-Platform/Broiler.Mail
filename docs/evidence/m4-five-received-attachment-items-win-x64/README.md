# Five M4 received-attachment items: Windows x64 evidence

Local validation on 2026-11-03 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned validation artifacts, not releases or provider certification.

- Shared Release suite: **1,305 passed**, including 22 new received-attachment/storage/reader cases.
- Focused attachment/MIME/original-source suite: **58 passed**.
- Windows reader/profile/keyboard/theme/remote-image/viewport suite: **87 passed**.
- NativeAOT TLS/STARTTLS protocol probe passed, including exact binary attachment extraction from the received/exported original without another IMAP command.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed; both build manifests are retained alongside this record.
- Dependency gate passed with 72 runtime libraries; renderer compatibility audit matched.
- No IL2xxx/IL3xxx warnings in either application publish or the protocol publish logs.
- `git diff --check` passed apart from the preexisting LF/CRLF notice for `docs/roadmap.md`.

Published application smoke exercises actual reader export/inventory/save, exact decoded binary bytes, no second receiver call, unchanged cached body, cache-clear retention and verified-backup exclusion. Fixtures use synthetic local mail and pinned loopback TLS; no real account or file opening is involved.

Ignored logs: `artifacts/m4-received-attachments-targeted.log`, `m4-received-attachments-shared.log`, `m4-received-attachments-windows.log`, `m4-received-attachments-protocol.log`, `m4-received-attachments-aot.log`, `m4-received-attachments-framework.log`.

[Scope and remaining work](../../m4-received-attachments.md). The current verified original is limited to 2 MiB and 32 attachment leaves. Exports require separate backups. Compose/send attachments, streaming/larger messages, destination selection, import/export browsing, broader provider/accessibility/other-target acceptance and renderer containment remain open. M0–M4 are not declared complete.
