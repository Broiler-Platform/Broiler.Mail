# Three M4 flag-recovery subitems: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Unsigned local
validation packages are not releases or real-provider certification.

- Shared Release suite: 1,058 passed, including ten added flag-recovery cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT TLS/STARTTLS protocol probes passed with durable flag intents and
  confirmed read/unread/star/unstar reconciliation in SQLite. Reopening a lost-star
  intent retained uncertainty without another STORE. Existing MOVE/reverse/delete,
  alias SMTP/private-Bcc and TLS invariants passed in the same executable.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published profile smoke repairs typed read/star receipts, skips uncertain star
  during repair and uses backed-up explicit review/dismissal while retaining mail.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in application or protocol publish logs.

Added cases exercise read/star x true/false x confirmed/lost, exact source scope,
reopened no-replay behavior, retained body/other flag/receive time, missing-header
receipt retention then repair, and malformed flag payload refusal without deletion.
All earlier uncertain-review confirmation/focus and backup/restore cases remain
in the shared/Windows suites.

Ignored logs: artifacts/m4-three-flag-recovery-items-shared.log,
m4-three-flag-recovery-items-windows.log, m4-three-flag-recovery-items-protocol.log,
m4-three-flag-recovery-items-aot.log and m4-three-flag-recovery-items-framework.log.
Both application manifests are retained alongside this document. All accounts,
credentials, loopback transports and profile data are synthetic.

See [exact completed scopes](../../m4-flag-recovery.md). Wider provider/process-crash/
power-loss acceptance, other-target certification, broader accessibility and the
remaining M0–M4 feature gaps remain open.
