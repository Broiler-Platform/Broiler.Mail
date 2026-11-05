# Local-original management — Windows x64

Local validation on 2026-11-04 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. This work postdates the earlier local CI snapshot. Nothing was pushed, released or sent through a mail account.

Implemented [decoded reading, exact export and recovery-backed removal](../../m4-local-originals.md) in Saved originals. Decoding is an explicit bounded text operation; standalone HTML is not executed or rendered. Import export rechecks the exact receipt and promotes flushed bytes without overwriting. Confirmed removal verifies that a completed whole-profile backup contains the exact target before atomically deleting only that import.

Sixteen new regression cases cover MIME encoded headers/base64/Unicode, exclusion of attachment text, HTML text alternatives, truncation, exact byte export, no overwrite/temporary leftovers, foreign receipt rejection, verified recovery restore, failed/missing-target/racing/canceled backups, quota-slot reuse, disabled accounts, 100%/200% text scale, Keep-first confirmation and ignored cancellation/late results. Source/text switching keeps a single preview control and preserves the importable external snapshot.

The published application fixture uses a synthetic original and disabled account. It opens/decodes/imports/reopens, verifies cache-clear/backup retention, exports exact bytes, keeps then confirms removal and restores the removed receipt/bytes into a separate profile. Receiver, sender and credential-mutation methods throw if invoked. This is not provider or native-window accessibility certification.

Validation:

- Full shared Release suite: **1,546 passed**.
- Full Windows Release suite: **346 passed**.
- Final local-original subset after the single-preview fix: **36 passed**.
- NativeAOT storage, TLS/STARTTLS protocol and Broiler.Net session probes: **all passed**.
- NativeAOT/self-contained and framework-dependent application publish/smoke: **both passed**. Retained [NativeAOT manifest](self-contained-build-manifest.json) and [framework-dependent manifest](framework-dependent-build-manifest.json).
- Application dependency gate: **72 runtime libraries**; renderer audit matched. No IL2xxx/IL3xxx warnings in application/probe publish logs.
- `git diff --check`: passed, apart from the existing LF/CRLF notice for `docs/roadmap.md`.

The full suites preceded the final single-preview disposal fix; the focused 36 cases cover that fix. Final published smoke also exercises repeated source switching. Local packages are unsigned validation artifacts, not releases. Both manifests retain `htmlRendererIsolation: not-implemented` and `releaseReady: false`.

Ignored logs: `artifacts/m4-local-management-full-tests.log`, `m4-local-management-windows-tests.log`, `m4-local-management-final-targeted.log`, `m4-local-management-probes.log`, `m4-local-management-aot.log`, `m4-local-management-framework.log`; detailed NativeAOT probe publish logs are under `artifacts/m0/win-x64/`.

Larger/streaming originals, rendered standalone HTML, printing, orphan metadata management and the other [M0–M4 gates](../../m0-m4-completion-status.md) remain open.
