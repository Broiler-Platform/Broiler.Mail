# Decision 0003: Read-only receiving and plain-text reading

Status: implemented. Date: 2026-09-28. Extends decision 0002.

Implement the version 1 manual receive and plain-text reading entries together,
reusing Broiler.UI's ListView, Button, Panel, Label, and ScrollView controls. Keep
protocol/MIME details in Infrastructure and paging/selection state in Application.
No additional package dependency is required beyond the pinned MailKit/MimeKit.

Use a fresh authenticated connection per operation, with the existing TLS policy
and 20-second deadline. Open INBOX using read-only EXAMINE. Fetch envelopes, UIDs,
internal dates, and flags in pages of at most 50; show descending IMAP arrival order.
Continuation captures UIDVALIDITY, UIDNEXT, count, and the next sequence index. A
membership change invalidates continuation and asks for refresh. This intentionally
simple approach avoids an unbounded SEARCH or retaining connections in version 1.
Sequences are used only for the bounded page request; message identity and body
requests always use account/folder/UIDVALIDITY/UID. The UI stops at 500 headers.

Fetch bodies on selection using bounded BODY.PEEK, rechecking UIDVALIDITY first.
Limit the raw MIME download to 2 MiB, including attachments, with an extra byte
to detect overflow and a transfer-progress guard. MimeKit parses at depth 32 and
decodes MIME alternatives, transfer encodings, and charsets. Prefer plain text.
For HTML-only mail, extract text with MimeKit's tokenizer; no browser, CSS, script,
resource-loading, or navigation service participates. This is lossy text conversion,
not HTML sanitization suitable for the version 2 renderer. Mark the fallback and
any 32,000-character display truncation explicitly.

Run receiving/decoding off the UI thread. The UI dispatcher publishes results only
if the operation generation is current. Cancel, changed account details, selection
changes, and window close invalidate pending results. Refresh replaces the list;
failure keeps previously loaded mail visible. Only the selected body is retained.
Opening a message never marks it read, and server flags are a refresh-time snapshot.

Verification uses synthetic loopback IMAP/TLS and MIME fixtures, including bounded
page requests, PEEK, UIDVALIDITY changes, deleted messages, stale paging, oversized
messages, timeouts, cancellation, and malformed/disconnected responses. Workflow
tests cover UI dispatch, stale completion suppression, retry, empty inbox, and
scrolling/wrapping. Native-window usability and a real-provider acceptance run are
still required for release readiness.
