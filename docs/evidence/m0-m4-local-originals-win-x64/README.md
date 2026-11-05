# M0–M4 continuation: local-original Windows x64 evidence

Local validation on 2026-11-04 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Unsigned packages are validation artifacts, not releases or provider certification.

- Final shared Release suite: **1,488 passed**, including all 20 new cases.
- Final local-original/profile-storage/saved-library subset: **59 passed**.
- Corrected native DPI/input/new-mail fixture subset: **35 passed**.
- Final complete Windows suite: **346 passed**.
- NativeAOT storage, TLS/STARTTLS protocol and Broiler.Net session probes: **all passed**.
- NativeAOT/self-contained and framework-dependent publish/smoke: **both passed**. Retained [NativeAOT manifest](self-contained-build-manifest.json) and [framework-dependent manifest](framework-dependent-build-manifest.json).
- Application dependency gate: **72 runtime libraries**; renderer compatibility audit matched. No IL2xxx/IL3xxx warnings in either application publish or M0 probe logs.
- `git diff --check`: passed, with only the preexisting LF/CRLF notice for `docs/roadmap.md`.

Storage coverage includes read-only local opening, exact byte snapshots, independent local/account identities, restart, metadata/checksum validation, corrupt/future-state refusal, per-file/count/aggregate quotas without eviction, cancellation, concurrent writers and verified profile backup/restore/cache-clear retention.

UI coverage includes explicit open/import/list/read separation, disabled accounts, 100%/200% text scale, read-only source text, optional hosts, late/canceled/queued/account/close/dispose/path/selection guards, usable focus and a new context proceeding during an ignored old cancellation. Preview controls are created only after explicit opening. File I/O/parsing of the bounded store runs outside the UI thread.

The complete Windows suite exposed stale native fixture assumptions from prior full-key row IDs and the addition of multiple readers (including Outbox). DPI/new-mail assertions now verify full message keys; input/new-mail helpers scope their lookup to Inbox instead of the entire shell. Anchor, text-selection, scroll, draft and input assertions remain in place.

Published smoke exercises the actual Saved originals library in a disabled account without inbox-cache support. It opens a synthetic local .eml source, imports exact bytes, explicitly lists/reopens it, clears downloaded mail, restores a verified backup and checks the same imported receipt/bytes. Closing clears local previews. Receiver/sender/credential mutation methods throw if invoked; no public mail account is used.

Ignored logs: `artifacts/m0-m4-completion-build.log`, `m0-m4-completion-shared.log`, `m0-m4-completion-targeted.log`, `m0-m4-completion-windows.log`, `m0-m4-completion-native-fixtures.log`, `m0-m4-completion-probes.log`, `m0-m4-completion-aot.log`, `m0-m4-completion-framework.log`.

[Implemented scope](../../m4-local-originals.md), [remaining M0–M4 gates](../../m0-m4-completion-status.md). Larger/streaming originals, decoded standalone reading, import management/export, sending attachments, complete synchronization/search, renderer containment and platform/provider acceptance remain open. M0–M4 are not complete.
