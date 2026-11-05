# Five M4 Saved originals library items: Windows x64 evidence

Local validation on 2026-11-04 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned validation artifacts, not releases/provider certification.

- Shared Release suite: **1,465 passed**, including **20 new library cases**.
- Windows reader/profile/keyboard/theme/remote-image/viewport suite: **87 passed**.
- Focused catalog/library/keyboard/outbox suite: **73 passed** before the final unavailable-host case, which is included in the full suite.
- NativeAOT TLS/STARTTLS protocol fixtures: **both passed**.
- NativeAOT/self-contained and framework-dependent application publish/smoke: **passed**; retained [NativeAOT manifest](self-contained-build-manifest.json) and [framework-dependent manifest](framework-dependent-build-manifest.json).
- Dependency gate: **72 runtime libraries**; renderer compatibility audit matched.
- No IL2xxx/IL3xxx warnings in protocol or either application publish log.
- `git diff --check` passed apart from the preexisting LF/CRLF notice for `docs/roadmap.md`.

New coverage includes account-specific receipts across folders/epochs/UIDs without cache creation, explicit selection/verification, selectable read-only identity, disabled-account use, exact attachment saving and source preview at 100%/200%, selection invalidation, account/close/cancel/dispose/queued inventory and verification guards, new-account listing during an old lookup, wrong account/missing-file refusal, unavailable hosts, and Ctrl+6 close during held work.

Published smoke uses a disabled configured account, synthetic exported MIME and a foreign receipt, clears downloaded cache, then opens the actual library in an application without inbox cache support. It lists one account only, verifies and previews the saved source, saves exact attachment bytes, renders the dialog and clears details on close. Receiver/sender/credential-write methods throw if invoked. Ordinary shell smoke also renders Saved originals. No real mail/provider is used.

Ignored logs: `artifacts/m4-original-library-build.log`, `m4-original-library-targeted.log`, `m4-original-library-shared.log`, `m4-original-library-windows.log`, `m4-original-library-protocol.log`, `m4-original-library-aot.log`, `m4-original-library-framework.log`.

[Scope and remaining work](../../m4-original-library.md). Cataloged originals only; arbitrary import/open, removal/repair, decoded standalone HTML, printing, streaming/sending attachments, other-target/provider/accessibility acceptance and renderer containment remain open. M0–M4 are not declared complete.
