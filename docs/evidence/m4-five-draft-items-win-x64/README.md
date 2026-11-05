# Five M4 multiple-draft items: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Packages are unsigned
local validation artifacts, not releases or real-provider certification.

- Shared Release suite: 1,107 passed, including 17 added library/store/UI/lifecycle cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT TLS/STARTTLS protocol probe passed existing discovery/read, flags,
  verified UID MOVE/reverse/delete/uncertainty and SMTP/private-Bcc fixtures.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published profile smoke exercises source-generated multi-draft storage, save and
  close, atomic swap with raw invalid recipients retained, picker construction,
  reopening and verified backup/separate-profile restore.
- Published confirmed library removal preserves the active draft; activation of an
  interrupted Sending record becomes persisted Unknown with Send unavailable.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in application or protocol publish logs.

Added shared cases cover exact-byte verified pre-library backup, preservation by
ordinary active save/discard, stale/cancelled mutations, invalid/null/duplicate
library entries, capacity and swap at capacity, cross-account raw edits/identity,
accepted/unknown/interrupted-send/Sent-copy states, separately confirmed removal,
failed active flush, backup/restore/cache clear, retained-owner removal refusal,
and disclosure/confirmation with a usable body at 640×480 and 1100×720. An existing
attachment-reminder fixture now queues UI callbacks to remove an autosave/test-thread
race observed during verification. All profile and transport data are synthetic.
No real mail is sent.

Ignored logs: `artifacts/m4-five-draft-items-shared.log`,
`m4-five-draft-items-windows.log`, `m4-five-draft-items-protocol.log`,
`m4-five-draft-items-aot.log`, and `m4-five-draft-items-framework.log`.
Both application build manifests are retained alongside this document.

See [five completed scopes, compatibility and limits](../../m4-multiple-drafts.md).
The visible outbox, server Drafts synchronization, attachments, full-body search,
broader accessibility/provider/crash/power-loss acceptance and other-platform
certification remain outside this increment. Other M0–M4 gaps remain open.
