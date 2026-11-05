# Three M3/M4 composition defaults: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Unsigned local
validation packages are not releases or real-provider alias certification.

- Shared Release suite: 1,032 passed, including ten added default/policy/signature cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT protocol probe passed TLS and STARTTLS, initializing an SMTP alias from
  the saved default and retaining alias envelope/header, removed-alias refusal,
  own-recipient exclusion, private-Bcc and existing IMAP fixture checks.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published profile smoke saves/reopens composition defaults, pins/restores a default
  alias, then selects a unique receiving alias for a reply with separate signature.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in application or protocol publish logs.

Cases cover persisted default and restored draft preservation, unique/ambiguous/
unknown/case-insensitive receiving addresses, explicit sender priority, opt-in policy,
reply/reply-all versus forward signatures, empty reply signature and invalid defaults.
Existing editor tests save default sender, reply policy and separate signature at
100%/200% text. Disclosure semantics include Composition defaults.

Ignored logs: artifacts/m3-three-composition-defaults-shared.log,
m3-three-composition-defaults-windows.log, m3-three-composition-defaults-protocol.log,
m3-three-composition-defaults-aot.log and m3-three-composition-defaults-framework.log.
Both application manifests are retained alongside this document. All transport,
accounts, credentials and profile fixtures are synthetic.

See [exact completed scopes](../../m3-composition-defaults.md). Provider permission,
alias-specific display names/signatures, broader account orchestration/accessibility,
other-platform certification and the remaining M0–M4 gaps remain open.
