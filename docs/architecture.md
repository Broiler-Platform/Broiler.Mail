# Architecture and implementation notes

Version 1 includes account/settings configuration, protected password storage,
IMAP connection testing, manual receiving, and plain-text reading. Version 1
implementation is complete; live-provider acceptance was deferred by the user.
Version 2 includes optional SMTP account configuration, a plain-text composer,
durable recovery of the active draft, secure SMTP submission, configurable
Sent-copy handling, and HTML preview. The current renderer runs in the mail process;
the [process-isolation specification](html-renderer-security.md) remains to implement.

## Project boundaries

```text
Broiler.Mail.Windows
  +-- Broiler.Mail.Application
  |     +-- Broiler.Mail.Core
  |     +-- Broiler.UI.*.Standard
  +-- Broiler.Mail.Infrastructure
  |     +-- Broiler.Mail.Core
  |     +-- MailKit
  +-- Broiler.Graphics.Windows
```

- **Core** owns data and interfaces. It references only the .NET base libraries.
- **Application** owns presentation and depends on Core and platform-neutral
  Broiler controls. It must not reference Infrastructure or a native backend.
- **Infrastructure** implements persistence and mail protocols behind
  Core interfaces. It must not depend on the UI or Windows.
- **Windows** is the composition root and platform host. It supplies native
  graphics/input integration and the protected credential adapter.

The three shared projects target `net10.0`; Windows targets `net10.0-windows`.
Use package references rather than absolute paths to neighboring repositories.
The central package versions match an available Broiler.UI preview and its
Broiler.Graphics dependency; upgrades should be deliberate and validated together.

## Starting points

| Area | Files/classes | Current behavior / next step |
| --- | --- | --- |
| Startup | `Program`, `CompositionRoot`, `MailApplication` | Loads accounts/settings/drafts independently, shows read errors, and opens the shell without invoking mail adapters. |
| Native hosting | `WindowsMailWindow`, `WindowsUiHost` | Broiler Direct2D, native input, UI dispatch through Broiler.UI's queued dispatcher, clipboard, default IME placement, minimum size, and DPI-aware resizing. |
| Shell | `MailShellView`, `MailShellViewModel`, `InboxView`, `ComposerView` | Inbox, Account, Settings, and Compose tabs; receiving and recoverable composition. |
| Account setup | `AccountProfileView`, `AccountProfileViewModel`, `AccountProfile` | Validated IMAP and optional SMTP settings; separate password controls reuse account/protocol bindings. Separate explicit tests: the IMAP connection, and a non-sending SMTP sign-in whose result never gates receiving. |
| Settings | `SettingsView`, `SettingsViewModel`, `ApplicationSettings` | Theme and initial window size save/reload; preferences apply at startup. |
| Persistence | `JsonAccountStore`, `JsonSettingsStore`, `JsonDraftStore`, `JsonConfigurationFile` | Versioned JSON, explicit paths, write locks, bounded reads, same-directory replacement, and visible errors. |
| Credentials | `CredentialKey`, `ICredentialStore`, `WindowsCredentialStore` | Windows generic credentials scoped to account/protocol and bound to connection identity. |
| Receiving | `IMailReceiver`, `ImapMailReceiver`, `InboxViewModel` | Read-only IMAP, bounded header pages, on-demand bodies, cancellation, and safe errors. |
| Reading | `MessageTextDecoder`, `ScrollableMessageText`, `PlainTextMessagePreview` | MIME/charset decoding, HTML text extraction, and a bounded wrapping/scrolling reader. |
| Sending | `MailDraft`, `IMailSender`, `SmtpMailSender`, `SendResult` | MailKit SMTP with required TLS, protected SMTP credentials, plain-text MIME, and durable submission outcomes. |
| SMTP sign-in test | `IOutgoingConnectionTester`, `SmtpConnectionTester`, `MailConnectionFailure` | Connect, required TLS, AUTH, best-effort QUIT; no submission command and no retry ([decision 0006](decisions/0006-non-sending-smtp-test.md)). |
| Sent copies | `ISentCopyWriter`, `ImapSentCopyWriter`, `OutgoingMessageFactory` | Explicit provider-managed mode or one append to an existing IMAP folder after durable SMTP acceptance. |
| Composition | `MailCompositionSource`, `MailComposition`, `ComposerViewModel`, `ComposerView` | New/reply/reply-all/forward, independent recipient fields, pinned sender, plain-text body, and reply-thread metadata. |
| HTML | `HtmlMessagePreview`, `WindowsHtmlPreviewHost`, `HtmlPreviewWindow` | Native Broiler.HTML (`HtmlContainer` / `BBitmap`) host in `Direct2DWindow` with passive HTML reduction, bounded cid images, plain-text toggle, remote image blocking, and external link handling. |

Sent-folder copying reports its outcome independently of SMTP acceptance. HTML preview
applies passive markup reduction and resource policy, but these do not establish OS
renderer isolation. See the [Phase 0 contract decisions](phase-0-foundation.md).

## Data and threading decisions

- Profiles contain no secrets. Credential slots are keyed by account and protocol;
  each stored value is bound to the host, port, username, TLS mode, and authentication
  method. A changed binding returns no password, preventing reuse for another server.
- Accounts have stable `AccountId` values. IMAP message identity includes account,
  mailbox, UIDVALIDITY, and UID, preparing the model for version 3.
- `MailMessageBody.HtmlText` is untrusted data. The plain-text adapter uses only
  `PlainText`; the placeholder HTML adapter cannot accidentally render it.
- Service contracts accept cancellation tokens. `MailInboxPage` carries bounded
  headers and a session-only continuation with account, UIDVALIDITY, UIDNEXT,
  mailbox count, and the next sequence index. Membership changes require refresh.
- `InboxViewModel` runs network/parsing work through the thread pool, posts results
  on the UI dispatcher, and invalidates old completions on cancellation, a changed
  selection/profile, or disposal. A failed refresh preserves the loaded list.
- Configuration view models publish busy/error/success changes and commit saved
  state only after a successful write. Forms disable editing while a save is pending.
- The native host uses Broiler.UI's `StandardQueuedUiDispatcher`. Results posted
  from any thread, the UI thread included, wait in its queue until the window drains
  it on the UI thread: on a message posted to the Broiler native window, and before
  each frame. A closing window stops draining, so late results are dropped. Only
  headless checks use the immediate dispatcher. Initialization completes before the
  window is created, preserving the entry point's STA thread. Controls are never
  updated by storage continuations.
- The Windows host implements `IUiClipboardHost` using bounded Unicode clipboard
  access and `IUiTextInputHost` to place the default IME composition window at the
  caret, in physical pixels, and to turn the IME off while a password field has the
  caret, as a native password box does. Hosting's `WindowsInputBridge` subclasses the
  render window. It handles characters, surrogate pairs, IME composition, and both
  wheel axes, and tracks dead keys, whose composed character Windows delivers; the
  Graphics character and wheel callbacks are not reached while it is attached.
  Broiler.UI draws the composition inline, but the default IME window can still show
  its own copy, because Hosting passes `WM_IME_SETCONTEXT` on unchanged and forwards
  the composition messages to `DefWindowProc` (UI-10). A per-monitor-v2 manifest and
  native resize handling preserve logical sizes, with a 640×480 minimum client area.
- `MailKeyboardNavigation` adds enabled-control traversal, automatic scrolling to
  focused fields, tab shortcuts, receive, and cancellation. First run opens Account.
  Full OS screen-reader/UI Automation integration remains the version 7 work item.
  The legacy Graphics input adapter is isolated in the Windows project. For the main
  window's native input it carries only pointer movement, buttons, and key presses
  (the measurement harness also synthesizes events through it); the HTML preview
  window takes all of its input through it. Broiler.Input 0.1.0-preview.5 has
  neutral Windows message translators (`WindowsMouseInputDevice` and
  `WindowsKeyboardInputDevice`) and a host seam (`IWindowsInputHost`), but Mail
  references neither package and no consumed host implements the seam: Graphics
  turns pointer and key messages into the legacy callbacks, and Hosting's bridge
  passes them on. The adapter can go once Hosting feeds those translators from its
  subclass and stops the callbacks.

## Verification

Build the solution in Release with warnings treated as errors. The `--smoke-test`
option composes the same application and renders each tab through a headless
Broiler UI host. It exercises dependency loading, control construction, layout,
and render-list creation without network access, accounts, or native windows.

The headless check is not a native-window or provider-compatibility test.
`Broiler.Mail.Tests` covers persistence/configuration, credential workflows, and a
loopback IMAP fixture for TLS, STARTTLS, authentication rejection, missing credentials,
certificate rejection, cancellation, timeouts, bounded read-only paging, deleted
messages, UIDVALIDITY changes, MIME/HTML text, transfer limits, and failed fetches.
Inbox workflow tests drain the host's queued dispatcher on its own thread and cover
worker-thread publication, stale completions, and the reader scrollbar at two sizes.
`Version1AcceptanceTests` exercises profile/settings restart, authentication,
receive/read/refresh, failure recovery, full-shell measurement, focus traversal,
and password clipboard behavior. Native demo inspection separately verified the
reading/form layouts, text fallback, and keyboard focus/tab switching.
Successful fixture connections
pin only their temporary test certificate through an internal test seam; the
production constructor always uses platform certificate validation.

`Broiler.Mail.Windows.Tests` exercises actual Credential Manager read/write/update/
delete operations using random test account IDs and synthetic passwords, with cleanup
in `finally`. Tests never enumerate credentials or use real account data.

## Persistence format and recovery

The Windows composition root uses `%LOCALAPPDATA%\Broiler.Mail`; tests inject file
paths. Files have a `schemaVersion: 1` envelope with `data` containing an account
array (at most one entry for version 1), application settings, or revisioned draft state. Unknown fields,
unknown enum values, invalid data, and unsupported versions are rejected.

Updates acquire an exclusive `.lock` file, re-read/validate the existing document,
write and flush a temporary file beside it, and replace the target. The lock file
may remain on disk; an open handle, not file presence, determines lock ownership.
The default local Windows filesystem is the intended storage target; network
filesystem durability guarantees have not been validated.

Unreadable configuration blocks saving on the affected form. The other form can
still be used. Repair/restore the file and restart, or intentionally move the file
to a backup location to start fresh. Missing files alone trigger defaults. Authentication
secrets are never part of the configuration models or JSON. Draft JSON does contain
readable message content and recipients, including Bcc. The masked password field is
cleared after submission and is never populated from stored credentials.

## Passwords and connection testing

The Windows adapter uses the documented [CredWrite/CredRead API](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew)
with generic credentials and current-user persistence on the local machine. Each
account/protocol has one slot; the connection fingerprint is metadata, and the
UTF-16 password is the protected blob. Native buffers are cleared before release.
Secrets necessarily exist temporarily as managed strings while being entered and
used. There is no protocol logger or plaintext fallback.

[MailKit 4.18.1](https://www.nuget.org/packages/MailKit/4.18.1) supplies IMAP connection
and authentication. The adapter explicitly selects `SslOnConnect` or required
`StartTls`, uses default certificate validation, enforces a 20-second deadline, and
propagates cancellation. A connection test authenticates and disposes the connection
without opening a mailbox. Receiving opens the inbox read-only and fetching bodies
uses a bounded `BODY.PEEK` request. Server-provided diagnostics are replaced with fixed user-facing messages;
exception chains do not carry authentication responses into UI or logs.

Profile saving, password saving, and testing are explicit separate actions. Password
operations require a saved, unchanged profile. The UI serializes operations, clears
password input, and offers cancellation during connection tests. Closing the shell
cancels an active test. OAuth is rejected before networking until sign-in is implemented.

The SMTP sign-in test follows the same rules with its own contract,
`IOutgoingConnectionTester`, kept apart from `IMailSender`. It reads only the SMTP
slot, connects with implicit TLS or required STARTTLS, authenticates, and sends a
best-effort QUIT. It never sends MAIL, RCPT, DATA or any other submission command. Its
failures carry a `MailConnectionFailure` kind with fixed text. Its result is kept in
memory beside the outgoing checklist step and never changes receiving readiness. A
test that cannot start, for example because of unsaved edits, is refused with what to
do first rather than reported as failed, and leaves both results in place. See
[decision 0006](decisions/0006-non-sending-smtp-test.md).

## Receiving and text boundaries

Header requests fetch at most 50 envelopes/flags/dates, ordered by descending UID;
the UI holds at most 500 headers. Opening a message rechecks UIDVALIDITY and requests
at most 2 MiB plus one overflow-detection byte by UID. A transfer-progress check
also aborts oversized downloads. MIME parsing has a depth limit of 32. Attachments
may occur in the raw download but are not presented, opened, or saved.

MimeKit decodes multipart content, charsets, and transfer encodings. Prefer the
plain-text alternative; otherwise use its HTML tokenizer to extract data tokens,
skip head/script/style/embedded content, and retain simple paragraph boundaries.
No HTML is rendered and no resource URLs are followed. The decoder returns a labeled
fallback and a truncation flag, with at most 32,000 text characters. The version 1
receiver leaves `HtmlText` null. Header display fields are limited to 512 characters.

The reader and the account and settings forms scroll in standard scroll views that
constrain content to the viewport width (`UiScrollConstraint.ConstrainWidth`), so long
text wraps and scrolls vertically. The standard tab view measures each tab's content
at the size it allocates, but it arranges every hidden tab at an empty rectangle, which
lays a hidden form out at no width and lost the composer status area's scroll position.
`TabContent` wraps each tab and skips that arrange. Because Broiler.UI has no minimum or
maximum size, Mail also keeps `BoundedScrollArea`, which caps the message header and the
inbox notice at a share of the height; `FillLastStack`, which gives the composer body
the remaining height above a minimum; and `ReadingColumn`, which bounds the reading line
length. `AdaptiveInboxLayout` is Mail's own responsive policy for the inbox panes. The
[component reuse review](component-reuse-review.md#4-october-2026-current-disposition)
records what would retire each one. Labels that show addresses, subjects, server text,
or the footer status set `UseMnemonic = false`, so an `&` is shown as written, not read
as an access key.

## Distribution and acceptance

`scripts/Publish-Windows.ps1` publishes a self-contained, untrimmed Windows package,
includes package/runtime license notices, executes its smoke check, and creates a
ZIP and SHA-256 file. The x64 build is validated on this machine. The script also
accepts arm64 for future testing; arm64 is not advertised as validated.

`--demo` uses only an in-memory account and synthetic mail. It does not construct
the Windows credential adapter or read the default configuration directory.
Its `send-rejected` fixture offers Send to a synthetic sender that refuses the recipient;
no demo sender contacts a server or reports acceptance. The `smtp-test-failed` and
`smtp-test-passed` fixtures answer the SMTP sign-in test synthetically and show a saved
SMTP password by presence only (`ICredentialStore.ContainsAsync`); no demo credential
lookup returns a secret.
The `new-mail` fixture keeps a small in-memory server: each later receive adds messages and
marks the open one read. Two acceptance-only options build on the demo: `--server-change`
makes the next new-mail receive delete the open message, push it below the newest page, or
renumber the inbox (used by `scripts/Accept-Refresh.ps1`), and `--scale` renders the main window
at a simulated display scale named in the window title, at the requested DIP size even beyond the
screen. HTML previews keep Windows' scale, except the preview a `long-html` or `preview-zoom`
measurement opens, which takes the simulated scale at its own 900×700 DIPs and names it in its
title. Neither option changes a Windows setting. The UI-12 measurement harness (`--measure`,
`scripts/Measure-UI.ps1`) is described in [the performance baseline](ui-performance-baseline-2026-10-02.md);
while it measures the preview, the preview's tile cache counts hits, misses, evictions, and raster
time, and otherwise counts nothing.
`--data-directory` supports isolated real configuration tests. The user will run
live-provider acceptance later using the [included checklist](version-1-acceptance.md).
Physical multi-monitor and IME language coverage are also recorded as user checks.

## Next implementation slice

The first version 2 entry is complete: `AccountProfileViewModel` now edits the
existing optional `OutgoingServer` value. Configuration reuses `MailServerSettings`,
`ConfigurationValidator`, and `JsonAccountStore`, so no new storage schema or package
is needed. Absent/null outgoing settings still load as IMAP-only. Configured SMTP
defaults to required STARTTLS on port 587, has an independent username and explicit
port, and accepts no plaintext transport. Disabling setup and saving clears only
the outgoing settings. Existing unsupported authentication modes are preserved
when editing other fields; this does not implement OAuth authentication.

Tests cover TLS/STARTTLS persistence, old profiles without `outgoingServer`, invalid
edits preserving disk contents, form capture, removal of outgoing settings, IMAP
credential isolation, and keyboard/scroll layout with SMTP expanded at both
640×480 and 1100×720. Separate SMTP password save/forget controls now reuse the
existing credential workflow. A separate, non-sending SMTP sign-in test now
completes outgoing setup ([decision 0006](decisions/0006-non-sending-smtp-test.md)).

The composer entry is also complete. `MessageTextDecoder` now extracts bounded
composition headers from the fetched MIME message, separately from abbreviated
inbox labels. Oversized/invalid metadata leaves the body readable but disables
reply/forward preparation. There is no received Bcc field in `MailCompositionSource`.
`MailComposition` prefers Reply-To, excludes the account's address, deduplicates
reply-all recipients, and quotes only the bounded plain-text preview.

Reply threading follows [RFC 5322 section 3.6.4](https://www.rfc-editor.org/rfc/rfc5322#section-3.6.4):
the parent Message-ID becomes In-Reply-To; References contains the parent's chain
(or its sole In-Reply-To when no chain exists) followed by its Message-ID. Missing
IDs are not invented. A forward starts a new conversation. The
[MimeKit ReplyTo contract](https://mimekit.net/docs/html/P_MimeKit_MimeMessage_ReplyTo.htm)
defines the Reply-To/From fallback. The SMTP sender serializes these draft fields
into MIME reply headers.

`ComposerViewModel` retains raw edits even when validation fails, allows only one
active draft, and preserves its ID, sender, and thread metadata across inbox and
profile changes. Sender identity changes block a successful draft check without
discarding text. Draft checks allow 1–100 recipients across To/Cc/Bcc, 998 subject
characters, and 100,000 body characters. Imported fields are not silently shortened
to fit editors. A composition source is limited to 100 addresses per header group,
100 reference IDs, and 998 characters per ID; an over-limit reply chain is rejected
instead of silently dropping ancestors.

The body editor uses the matching `Broiler.UI.RichEdit.Standard` package and only
its plain-text input/output surface. No document codecs or HTML renderer are added.
The Windows host routes application shortcuts before editor input so Tab navigation
cannot also insert text; subsequent key events still update UI focus visibility.
Compose is fourth in the tab order, preserving the original Ctrl+1/2/3 shortcuts.
Tests cover source MIME/IMAP integration, threading, recipient privacy, invalid
draft retention, sender changes, action enablement, and keyboard/layout behavior.

The draft recovery entry is complete. `IDraftStore` and `JsonDraftStore` reuse
`JsonConfigurationFile` for locking, schema validation, bounded IO, and file
replacement. The draft file allows up to 4 MiB; account/settings limits remain
1 MiB. Draft storage accepts incomplete or invalid recipient text independently
of send validation: raw To/Cc/Bcc fields are authoritative, while `MailDraft`
retains the original sender, ID, and reply metadata. Storage bounds exceed send
bounds (400,000 body characters, 16,000 subject characters, and 32,000 characters
per raw recipient field) so validation errors do not discard recoverable edits.

`DraftJournal` serializes and coalesces snapshots off the UI thread. Every write
checks the expected persisted revision under the file lock. Discard writes a null
draft with a new revision, preventing an older instance from resurrecting it.
Conflicts retain local edits and require resolving the saved file before restart;
they never silently choose a winner. Broken/unsupported draft files disable
composition without blocking account/settings loading. `MemoryDraftStore` keeps
demo and default headless composition free of disk writes.

The native host uses Broiler.Graphics' delegated close mode and a message loop
through `Broiler.Native.Windows`, allowing normal close to wait for the journal.
Editing is frozen during that flush; a failed save keeps the window and draft open.
The flush itself never waits on the UI dispatcher. Forced termination can recover
only the latest completed save. The native close test verifies that a blocked
write keeps the window alive until saving finishes.

Submission persists `Sending` before invoking `IMailSender`. Accepted, rejected,
and uncertain results become `Accepted`, `Failed`, and `Unknown`; transport
exceptions conservatively become unknown. A recovered `Sending` becomes unknown,
and accepted/unknown drafts cannot be resent. Failed persistence before submission
prevents transport invocation; failed persistence afterward retains the result
in memory and the earlier sending record on disk. Tests cover these transitions,
restart recovery, malformed files, conflicts, coalescing, and failed save/discard.
`SmtpMailSender.IsAvailable` now enables Send for configured accounts. Demo mode
injects an unavailable sender that never accesses the network.

SMTP submission uses the existing MailKit dependency and the existing
`CredentialKey.For(account, MailProtocol.Smtp)`/`ICredentialStore` path. No new
credential format or platform interop is needed. Shared `MailComposition.ValidateDraft`
checks sender/account identity and bounded content both in the composer and again
at the transport boundary. Configuration and OAuth checks happen before connecting.
An operation deadline covers credential lookup, connection, authentication, and send.
Production uses platform certificate validation with explicit `SslOnConnect` or
required `StartTls`; only internal test construction can pin a fixture certificate.

The [MailKit explicit-envelope SendAsync overload](https://mimekit.net/docs/html/M_MailKit_Net_Smtp_SmtpClient_SendAsync.htm)
separates SMTP recipients from MIME headers. Broiler.Mail never constructs a Bcc
header for SMTP; the deduplicated envelope includes To/Cc/Bcc. MIME contains Unicode plain
text, a GUID-based message ID retained across retries, and reply threading headers.
Tests inspect actual DATA and envelope commands, including Bcc-only mail and dot
stuffing. A rejection of one recipient stops the transaction before DATA even if
an earlier recipient was accepted. Explicit sender/recipient/message rejection is
safe to retry; other failures after entering SendAsync are conservatively unknown.
Pre-submission failures are rejected. Successful SendAsync fixes acceptance even
if subsequent disposal fails; no QUIT round trip can reverse it. All user-facing
transport diagnostics are fixed messages, without server responses or secrets.

The loopback SMTP fixture verifies TLS/STARTTLS, certificate rejection, no plaintext
authentication fallback, authentication/recipient/message rejection, lost or malformed
acknowledgements, cancellation, timeouts, restart state, and retained draft content.
SMTP credential tests verify protocol isolation, changed bindings, masked/cleared
fields, and retry after storage failure. Provider validation remains with the user;
see the [SMTP checklist](version-2-smtp-checklist.md).

Sent-copy policy is explicit per account: `NotConfigured` (the compatible default),
`ProviderManaged`, or `AppendToFolder` with a bounded exact IMAP path. Changing only
this policy does not invalidate credential bindings. The provider-managed status
does not assert that the provider's copy was verified. Demo mode has no copy writer.

`ComposerViewModel` captures the account and submitted draft, including its timestamp,
before SMTP begins. After acceptance it persists `Accepted` plus `Pending` copy
intent before invoking `ISentCopyWriter` once. Failure to persist that boundary
prevents the append. The copy result is then persisted separately. Restoring a
pending copy produces `Unknown`, while keeping SMTP accepted. A failed or lost
copy acknowledgement never enables resend, and there is no copy retry on restart,
Save draft, or repeated Send. A post-copy persistence failure leaves the earlier
pending record to recover conservatively. The local composition remains available.

`ImapSentCopyWriter` reuses MailKit and the existing protected IMAP credential slot,
with platform certificate validation, required TLS/STARTTLS, and its own 20-second
deadline. It resolves the exact existing folder and calls
[MailKit AppendAsync](https://mimekit.net/docs/html/Overload_MailKit_IMailFolderExtensions_AppendAsync.htm)
once with the Seen flag and submission date; it does not SELECT, CREATE, or alter
other messages. A tagged OK confirms saving even without APPENDUID. A command
rejection is failed; an ambiguous interruption after entering APPEND is unknown.
No raw server diagnostics are shown. SMTP and IMAP use `OutgoingMessageFactory`
for the same message ID, date, thread headers, and text; only the private Sent copy
includes Bcc. Received-mail access remains read-only.

Tests inspect actual SMTP DATA and IMAP APPEND, compare content, verify credential
and folder failures, and simulate interruption and storage failure on either side
of the acceptance/copy boundary. Legacy accounts and accepted drafts load without
assuming a Sent copy exists. Provider testing remains pending.

Isolated HTML preview is implemented behind `IHtmlPreviewHost` and `HtmlMessagePreview`.
Untrusted markup is sanitized to passive typography and tables by `HtmlPreviewPolicy`,
injecting a strict `default-src 'none'` Content-Security-Policy. Embedded `cid:` images
are resolved from the MIME message's `multipart/related` parts by `MessageTextDecoder`
under bounded constraints (1 MiB per image, 16 images maximum, raster types only)
and converted into data URIs; missing or non-message CIDs are omitted. Remote images
are blocked by default and can be loaded via a controlled HTTP fetcher. The preview
window provides a seamless toggle between HTML and plain-text views. All interactive
elements (scripts, forms, frames, plugins, devtools, web messages) are disabled, and
selected HTTP/HTTPS links open externally.

Record the user's provider test results when available using the [SMTP checklist](version-2-smtp-checklist.md).
The [initial decision](decisions/0001-version-1-foundation.md) records the foundation.
The [connection decision](decisions/0002-credentials-and-imap-test.md) records credentials/testing.
The [receiving decision](decisions/0003-read-only-receiving.md) records the current reader.
The [sending and sent-copy decision](decisions/0004-smtp-and-sent-copies.md) records SMTP submission and Sent copies.
The [HTML preview decision](decisions/0005-isolated-html-preview.md) records content policy;
its former isolation claim is superseded by the [renderer security gate](html-renderer-security.md).
Version 2 renderer containment remains open before an HTML-enabled release can be approved.
