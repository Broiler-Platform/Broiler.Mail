# Five M4 original-source export items: Windows x64 evidence

Local validation on 2026-11-03 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned validation artifacts, not releases or real-provider certification.

- Shared Release suite: **1,253 passed**, including 24 added source/protocol/export/reader cases.
- Focused source/IMAP/export/reader suite: **123 passed**.
- Windows reader/profile/keyboard/theme/remote-image/viewport suite: **87 passed**.
- NativeAOT original-source retrieval/export protocol fixtures passed under loopback TLS and STARTTLS.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed; manifests retained alongside this record.
- Dependency policy passed with 72 runtime libraries; renderer compatibility audit matched.
- No IL2xxx/IL3xxx warnings observed in protocol/application publish logs.
- `git diff --check` passed apart from the preexisting LF/CRLF notice for `docs/roadmap.md`.

Coverage includes exact original binary octets and CRLF, UID response ordering/identity, partial offsets, declared literal bounds, missing/duplicate payload/framing, complete RFC822.SIZE comparison and pre-transfer oversize refusal; read-only/disabled/epoch/account guards; checksum/unique files, canceled/invalid source, concurrent file limits and immutable caller snapshot; copyable paths at 100%/200% text, wrong-message and storage-failure retry, cancellation/disposal/stale context, and maintenance/account-switch rejection of late uncancellable source. Profile cleanup keeps exports while verified backups omit them.

Native protocol probe retrieves and exports actual original bytes under pinned loopback TLS and STARTTLS without read-flag mutations. Published application smoke uses synthetic bytes including binary values, the ordinary cached reader and real export files; verifies no automatic source call, unchanged preview, checksum display, retained exports after cache cleanup and exclusion from recovery snapshots. No real mail/account/provider or automatically launched file is involved.

Ignored logs: `artifacts/m4-original-source-imap.log`, `m4-original-source-targeted.log`, `m4-original-source-shared.log`, `m4-original-source-windows.log`, `m4-original-source-protocol.log`, `m4-original-source-aot.log`, `m4-original-source-framework.log`.

[Scope and remaining work](../../m4-original-source-export.md). Exports must be backed up separately. Local .eml open/import/source viewing, general attachments/large-message support, target/provider certification and renderer containment remain open. M0–M4 are not declared complete.

Update 2026-11-03: this record predates the bounded current-export byte viewer;
see [source inspection](../../m4-original-source-inspection.md) and its separate evidence.
