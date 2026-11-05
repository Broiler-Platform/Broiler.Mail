# Three M1/M4 confirmed-cache recovery subitems: Windows x64

Validated locally on 2026-11-03 against the dirty working tree. Unsigned local
validation packages are not releases or real-provider certification.

- Shared Release suite: 1,038 passed, including six new recovery cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published profile smoke seeds confirmed MOVE/delete receipts, reopens SQLite,
  repairs both locally while retaining the moved body, repeats an already-applied
  MOVE receipt, then backs up/restores and repairs a delete in a separate profile
  without altering the source profile's saved body.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in either publish log.

Shared cases cover failed-MOVE receipt retention and later successful repair,
idempotence before/after local commit, backup/restore isolation, cleanup refusal,
manual repair view/status/maintenance events, receipt deduplication, corrupt/future/
missing-destination refusal, cancellation before recording and pending-mutation
guards with continued offline reading/Receive. Pending-count cleanup also runs
without the cancelled token after repair exits.

Ignored logs: artifacts/m4-three-recovery-items-shared.log,
m4-three-recovery-items-windows.log, m4-three-recovery-items-aot.log and
m4-three-recovery-items-framework.log. Both package manifests are retained alongside
this file. Network protocol behavior did not change; its standalone NativeAOT probe
was not rerun for this increment. All recovery/profile data are synthetic.

See [exact receipt boundary and remaining work](../../m4-cache-recovery.md).
Server-phase crash/unknown tracking and receipt-publication failure are not covered
by this journal. No server mutation is automatically replayed. Broader M0–M4
implementation/platform/accessibility acceptance remains open.
