# Five M3 removed-account mail items: Windows x64 evidence

Local validation on 2026-11-04 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned validation artifacts, not releases/provider certification.

- Shared Release suite: **1,392 passed**, including **17 new orphan-mail cases**.
- Windows reader/profile/keyboard/theme/remote-image/viewport suite: **87 passed**.
- Published NativeAOT TLS/STARTTLS protocol fixture: **both passed**.
- Focused account cleanup/selection/backend cases: **43 passed**; four added 100%/200% UI cases passed separately, with final collapse behavior included in the full suite.
- NativeAOT/self-contained and framework-dependent application publish/smoke: **passed**; retained [NativeAOT manifest](self-contained-build-manifest.json) and [framework-dependent manifest](framework-dependent-build-manifest.json).
- Dependency gate: **72 runtime libraries**; renderer compatibility audit matched.
- No IL2xxx/IL3xxx warnings in either application publish or protocol publish log.
- `git diff --check` passed apart from the preexisting LF/CRLF notice for `docs/roadmap.md`.

Coverage includes bounded sorted orphan discovery, retained old epochs/empty heads, authoritative and legacy account exclusion without import, corrupt/future accounts, re-added target refusal before backup, scoped indexed deletion/restore, canceled/pending/failed snapshots, manual selection/Keep-first confirmation, no-account management, draft retention and maintenance release. Existing shared account cleanup tests continue to exercise transaction-failure rollback and target-only progress/index maintenance through the reused cleanup engine.

Published smoke uses synthetic accounts/mail through the actual account view model: it checks the collapsed section, explicit discovery and selection, preserved active mail/draft/credential, valid remaining index and recovery restore. No real server or account is used.

Ignored logs: `artifacts/m3-orphan-mail-build.log`, `m3-orphan-mail-targeted.log`, `m3-orphan-mail-view.log`, `m3-orphan-mail-shared.log`, `m3-orphan-mail-windows.log`, `m3-orphan-mail-protocol.log`, `m3-orphan-mail-aot.log`, `m3-orphan-mail-framework.log`.

[Scope and remaining work](../../m3-orphan-downloaded-mail.md). Other orphan configuration/credentials and broader account-removal, crash/power-loss/provider/target/accessibility acceptance plus renderer containment remain open. M0–M4 are not declared complete.
