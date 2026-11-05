# M4 Windows x64 UID MOVE foundation validation

Validated locally on 2026-11-02 against the dirty working tree. These unsigned
validation artifacts are not releases or real-provider certification.

- Shared Release suite: 889 passed (19 added move protocol/service cases).
- Focused Windows reader/profile/keyboard/theme/remote-image checks: 79 passed.
- NativeAOT protocol probe passed implicit TLS and STARTTLS. New checks perform a
  Unicode-target UID MOVE, verify original UID removal and mapped destination
  epoch/UID, and drop a MOVE acknowledgement after the fixture moved the message.
  That outcome must be Unknown with one attempt. Existing flags/read-only-body
  and SMTP acceptance checks also pass.
- Self-contained NativeAOT and framework-dependent application packages passed
  published smoke, 72-library dependency policy and renderer package audit.
  No IL2xxx/IL3xxx warnings were observed in the final publish logs.

This slice is the optional IMailMover/protocol foundation. It adds no move controls
or cache writes. Application smoke remains a regression gate; the standalone
NativeAOT protocol probe directly roots and exercises the new move code.

Loopback cases cover unsupported MOVE, absent/nonselectable destination, stale
source epoch, read-only source, missing UID, Unicode quoting/encoding, preserved
message content and flags, tagged/untagged singleton COPYUID ranges, missing/bad
receipts, lost/rejected or unverifiable operations, same-folder refusal and unsafe
input. No COPY/STORE Deleted/EXPUNGE fallback, CLOSE or folder creation is sent.

Logs remain in ignored artifacts/m4-moves-shared.log, m4-moves-windows.log,
m4-moves-protocol-aot.log, m4-moves-aot.log and m4-moves-framework.log.
See [move contract and remaining work](../../m4-moves.md). Destination-picker and
Archive/Trash UI, transactional cache reconciliation, undo and permanent deletion
remain pending, along with provider/native accessibility and other-platform acceptance.
