# Three M3 sender-identity subitems: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Unsigned local
validation packages are not releases or real-provider alias certification.

- Shared Release suite: 1,022 passed, including ten new alias cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT protocol probe passed TLS and STARTTLS, including alias envelope/header,
  removed-alias refusal before another SMTP DATA and reply-all own-address exclusion.
  Existing IMAP discovery/read/flag/move/reverse/delete/unknown and private-Bcc checks
  also passed in the same native executable.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published profile smoke saves/reloads an alias, pins it in a signed draft, restores
  that sender and verifies removal refusal and explicit reauthorization.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in application or protocol publish logs.

Shared coverage includes account/draft reopen, invalid/duplicate/excessive aliases,
blank-line safety, arbitrary sender refusal, alias removal/reauthorization, reply-all
recipient exclusion, UI picker pinning and SMTP submission/refusal over TLS/STARTTLS.
Existing editor tests now capture/save aliases at 100%/200% text, and disclosure
naming/control semantics include the new alias section.

Ignored logs: artifacts/m3-three-alias-items-shared.log,
m3-three-alias-items-windows.log, m3-three-alias-items-protocol.log,
m3-three-alias-items-aot.log and m3-three-alias-items-framework.log. Both application
package manifests are retained alongside this document. Fixtures use synthetic
accounts and pinned loopback certificates, not real accounts or provider aliases.

See [exact completed scopes](../../m3-sender-aliases.md). Provider authorization,
alias-specific display names/signatures, broader account orchestration/acceptance,
other-platform certification and remaining M0–M4 gaps remain open.
