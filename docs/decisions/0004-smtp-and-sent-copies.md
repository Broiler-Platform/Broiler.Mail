# Decision 0004: SMTP submission and Sent-copy handling

Status: implemented. Date: 2026-09-30. Extends decisions 0001–0003.

Implement version 2 outgoing mail and Sent-copy handling using MailKit behind
Core interfaces (`IMailSender`, `ISentCopyWriter`). Separate credential storage
reuses the existing Windows Credential Manager adapter. Outgoing configuration
is stored alongside incoming server settings in `AccountProfile`.

## Outgoing submission

SMTP requires TLS (`SslOnConnect` or `StartTls`) with system certificate validation
and a 20-second operation deadline. MailKit serializes plain-text MIME with stable
UUID-based `Message-ID`, `In-Reply-To`, and `References` headers. Bcc recipients are
included in the deduplicated SMTP envelope commands but are strictly omitted from
message headers. A recipient rejection aborts submission before DATA.

Interrupted submission or connection loss after sending DATA is treated conservatively
as `Unknown`. The active draft snapshot transitions to `Sending` before transport
invocation, and to `Accepted`, `Failed`, or `Unknown` upon completion. Draft recovery
never automatically resends an accepted or unknown draft on startup.

## Sent-copy policy

Sent copies are configured per account:
- `NotConfigured`: compatible default for existing accounts; no copy is saved.
- `ProviderManaged`: provider automatically saves outgoing messages; no append.
- `AppendToFolder`: appends one copy to the specified existing IMAP folder path.

Sent-copy execution occurs only after successful SMTP acceptance and is tracked
independently. An append failure reports copy error while preserving SMTP acceptance;
it never causes a resend. The Sent copy includes the sender's private Bcc header
for record-keeping, while retaining the exact same Message-ID and timestamp.
