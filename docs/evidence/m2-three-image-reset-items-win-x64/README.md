# Three M2 remote-image reset items: Windows x64 evidence

Local validation on 2026-11-04 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned validation artifacts, not releases or provider certification.

- Shared Release suite: **1,468 passed**, including three new control cases.
- Final focused control check after wording adjustment: **6 passed**.
- Windows reader/profile/keyboard/theme/remote-image/viewport suite: **90 passed**, including three new reset cases.
- NativeAOT TLS/STARTTLS protocol fixtures: **both passed**.
- NativeAOT/self-contained and framework-dependent application publish/smoke: **passed**. Retained [NativeAOT manifest](self-contained-build-manifest.json) and [framework-dependent manifest](framework-dependent-build-manifest.json).
- Dependency gate: **72 runtime libraries**; renderer compatibility audit matched.
- No IL2xxx/IL3xxx warnings in the protocol or either application publish log.
- `git diff --check` passed with only the preexisting LF/CRLF notice for `docs/roadmap.md`.

The reset regressions cover loaded images and pending requests, queued completion rejection, a newer successful load before an old canceled request returns, fresh consent/refetching, original CID image retention, independent surfaces, current palette pixels, zoom and approximate reading position. Shared controls cover focus, HTML-mode guards and optional host support.

Published smoke exercises the actual broker and inline surface with a synthetic PNG: explicit load, block with no request and no remaining remote image, then a fresh explicit load with a new request. No public server, real mailbox, credentials or server mutation is used.

Ignored logs: `artifacts/m2-image-reset-build.log`, `m2-image-reset-shared.log`, `m2-image-reset-controls.log`, `m2-image-reset-windows.log`, `m2-image-reset-protocol.log`, `m2-image-reset-aot.log`, `m2-image-reset-framework.log`.

[Implemented scope](../../m2-remote-image-reset.md). Permissions are per open surface and are not persisted. Previously sent requests cannot be undone, and secure memory/OS-cache erasure is not claimed. Broader production DNS/TLS, other targets, accessibility/desktop acceptance and renderer process containment remain open. M0–M4 are not declared complete.
