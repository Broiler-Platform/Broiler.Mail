# M3 account removal guards — Windows x64 evidence

Local validation on 2026-11-04 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned validation artifacts, not releases.

- Final account-removal/collection/sender-override subset: **57 passed**, including **12 new removal cases**.
- Updated Windows profile smoke subset: **2 passed**.
- Final complete shared Release suite: **1,530 passed**.
- Final complete Windows suite: **346 passed**.
- Final NativeAOT/self-contained and framework-dependent publish/smoke: **both passed**. Retained [NativeAOT manifest](self-contained-build-manifest.json) and [framework-dependent manifest](framework-dependent-build-manifest.json).
- Dependency gate: **72 runtime libraries**; renderer compatibility audit matched; no IL2xxx/IL3xxx publish warnings.
- `git diff --check` passed with only the preexisting LF/CRLF notice for `docs/roadmap.md`.

Coverage includes owned active/uncertain/confirmed/unattributed changes, Windows case-varied paths, another account's attributable pending work, pre/post-backup configuration changes with both password choices, changes appearing after secret deletion, corrupt journals, unchanged recreated sender-override collections and changed overrides. Refusal preserves configuration and secrets before cleanup; late refusal keeps configuration and reports already-forgotten passwords.

Published profile smoke injects a synthetic unresolved intent for the selected temporary account and asserts refusal before backup. It clears only the fixture's intent, then exercises ordinary backed-up removal with persisted sender overrides. The existing OS-vault assertions check both synthetic target credential slots are removed, another account's synthetic password remains, cached bodies remain and the recovery backup restores the account. No live provider or user account is used.

Ignored logs: `artifacts/m3-removal-guard-targeted.log`, `m3-removal-guard-final-targeted.log`, `m3-removal-guard-smoke.log`, `m3-removal-guard-shared.log`, `m3-removal-guard-windows.log`, `m3-removal-guard-aot.log`, `m3-removal-guard-framework.log`.

[Implemented scope](../../m3-account-removal-guards.md). These checks do not claim an atomic vault/configuration transaction, full orphan-data management or complete provider/power-loss/other-target acceptance. [Remaining M0–M4 gates](../../m0-m4-completion-status.md) stay open.
