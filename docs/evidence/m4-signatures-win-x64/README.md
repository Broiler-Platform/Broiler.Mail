# M3/M4 account signatures: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Unsigned local
validation packages are not releases or real-provider certification.

- Shared Release suite: 979 passed, including nine added signature cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT/self-contained and framework-dependent publish/smoke passed.
- Published profile smoke saves/reopens a synthetic account signature, creates and
  flushes a signed draft, and reopens it against changed configuration without
  reapplying the new signature or altering the saved body.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in either publish log.

Tests cover account isolation, draft restart, retained text on account/signature
changes, reply/reply-all/forward quote preservation, empty signatures and validation.
Editor tests capture multiline text and save it to the account store at 100% and
200% text. Broader native accessibility acceptance remains open.

Ignored logs: artifacts/m4-signatures-shared.log, m4-signatures-windows.log,
m4-signatures-aot.log and m4-signatures-framework.log. Both package manifests are
retained alongside this document. Protocol behavior did not change; its standalone
probe was not rerun for this increment.

See [signature behavior and M0–M4 gaps](../../m4-signatures.md). Other architectures,
renderer containment, broader storage/provider acceptance, aliases/orchestration,
durable mutations, general attachments, indexed full-text search, multiple drafts
and import/export remain open.
