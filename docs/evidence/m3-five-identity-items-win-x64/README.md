# Five M3 sender-identity items: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Packages are unsigned
local validation artifacts, not releases or real-provider certification.

- Shared Release suite: 1,134 passed, including 27 added identity/configuration/MIME/UI/lifecycle cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT TLS/STARTTLS protocol probe passed alias SMTP envelope and pinned quoted
  display-name checks after account presentation changed, private Bcc and removed-
  alias refusal, alongside existing IMAP discovery/read/flags/verified MOVE/reverse/
  delete/uncertainty invariants.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published profile fixture exercises source-generated account and draft persistence,
  per-alias name/signature defaults, reopening after settings change, pinned MIME,
  receiving-alias empty-reply-signature suppression, verified backup/separate-profile
  restore and sender-identity editor construction.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in application or protocol publish logs.

Added shared cases cover account partitioning/reopen, invalid/foreign/duplicate/null
settings, header-control/length/Unicode refusal, quoted and Unicode names as one
mailbox, empty/legacy display names, bounded folded Unicode Q words, signature
inheritance/suppression and account fallback, forward/reply/reply-all sender choice,
unchanged parked/restored drafts, editor save/reset and verified profile backup.
The new punctuation cases found a decode-before-address-split bug; valid mailbox
syntax now parses before display-name decoding, with the existing malformed-header
fallback retained. No real mail is sent; profile/transport fixtures are synthetic.

Ignored logs: `artifacts/m3-five-identity-items-shared.log`,
`m3-five-identity-items-windows.log`, `m3-five-identity-items-protocol.log`,
`m3-five-identity-items-aot.log`, and `m3-five-identity-items-framework.log`.
Both application build manifests are retained alongside this document.

See [five completed scopes, compatibility and limits](../../m3-sender-identity-defaults.md).
Broader account orchestration, unified views, provider alias authorization, OAuth,
other-platform certification and native accessibility acceptance remain open.
M0–M3 are not declared complete; this increment closes only the five listed items.
