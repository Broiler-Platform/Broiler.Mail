# Five M4 saved-original reuse items: Windows x64 evidence

Local validation on 2026-11-04 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned validation artifacts, not releases/provider certification.

- Shared Release suite: **1,420 passed**, including **28 new saved-original cases**.
- Windows reader/profile/keyboard/theme/remote-image/viewport suite: **87 passed**.
- NativeAOT TLS/STARTTLS protocol fixtures: **both passed**, including catalog recreation.
- Focused saved-source/original-source/attachment suite: **80 passed** before the final post-commit cancellation case; the full suite includes that case.
- NativeAOT/self-contained and framework-dependent application publish/smoke: **passed**; retained [NativeAOT manifest](self-contained-build-manifest.json) and [framework-dependent manifest](framework-dependent-build-manifest.json).
- Dependency gate: **72 runtime libraries**; renderer compatibility audit matched.
- No IL2xxx/IL3xxx warnings in protocol or either application publish log.
- `git diff --check` passed apart from the preexisting LF/CRLF notice for `docs/roadmap.md`.

Coverage includes full account/folder/epoch/UID partitions, service recreation, relative-path separate copies, old-file isolation, corrupt/future/path/identity/duplicate/size/hash catalog validation, missing/modified originals, concurrent writes and 64-entry/file quota reuse, cache-clear retention/profile-backup exclusion, explicit disabled-download local activation, 100%/200% focus and no automatic preview/attachment inventory, stale/canceled/reset/disposed/queued listing and file verification, mismatched catalog rows and cancellation after file/receipt commit.

Published smoke recreates the source service and reader, lists and verifies a saved receipt, explicitly previews source and extracts exact attachment bytes without another receiver call, then checks catalog retention and backup exclusion. NativeAOT protocol fixtures separately check catalog recreation after production TLS/STARTTLS original-source export. All mail and servers are synthetic.

Ignored logs: `artifacts/m4-saved-originals-build.log`, `m4-saved-originals-targeted.log`, `m4-saved-originals-shared.log`, `m4-saved-originals-windows.log`, `m4-saved-originals-protocol.log`, `m4-saved-originals-aot.log`, `m4-saved-originals-framework.log`.

[Scope and remaining work](../../m4-saved-originals.md). Only cataloged exports for the current reader's exact message can be reused. Arbitrary open/import, global file browsing, removal/repair, printing, larger mail/streaming, sending attachments, broader provider/target/accessibility acceptance and renderer containment remain open. M0–M4 are not declared complete.
