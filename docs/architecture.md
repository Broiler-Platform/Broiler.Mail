# Architecture and implementation notes

Version 1 includes account/settings configuration, protected password storage,
IMAP connection testing, manual receiving, and plain-text reading. Version 1
implementation is complete; live-provider acceptance was deferred by the user.

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
| Startup | `Program`, `CompositionRoot`, `MailApplication` | Loads account/settings independently, shows read errors, and opens the shell without invoking mail adapters. |
| Native hosting | `WindowsMailWindow`, `WindowsUiHost` | Broiler Direct2D, native input, UI dispatch through Broiler.UI's queued dispatcher, clipboard, default IME placement, minimum size, and DPI-aware resizing. |
| Shell | `MailShellView`, `MailShellViewModel`, `InboxView` | Inbox, Account, and Settings tabs; receive/load older/read/cancel actions and visible operation status. |
| Account setup | `AccountProfileView`, `AccountProfileViewModel`, `AccountProfile` | Validated profile, explicit password save/removal, cancellable connection test, and visible outcomes. |
| Settings | `SettingsView`, `SettingsViewModel`, `ApplicationSettings` | Theme and initial window size save/reload; preferences apply at startup. |
| Persistence | `JsonAccountStore`, `JsonSettingsStore`, `JsonConfigurationFile` | Versioned JSON, explicit paths, write locks, bounded reads, same-directory replacement, and visible errors. |
| Credentials | `CredentialKey`, `ICredentialStore`, `WindowsCredentialStore` | Windows generic credentials scoped to account/protocol and bound to connection identity. |
| Receiving | `IMailReceiver`, `ImapMailReceiver`, `InboxViewModel` | Read-only IMAP, bounded header pages, on-demand bodies, cancellation, and safe errors. |
| Reading | `MessageTextDecoder`, `ScrollableMessageText`, `PlainTextMessagePreview` | MIME/charset decoding, HTML text extraction, and a bounded wrapping/scrolling reader. |
| Sending | `MailDraft`, `IMailSender`, `SmtpMailSender`, `SendResult` | Version 2 contracts only; account validation and uncertain outcomes need implementation. |
| HTML | `HtmlMessagePreview` | Throws until the version 2 isolation/resource-policy gate is met. |

Sending and isolated HTML rendering are the remaining skeleton integration points.
Unimplemented operations throw explicitly instead of returning successful empty
results or pretending data has been saved.

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
  access and `IUiTextInputHost` to place the default IME composition window. Committed
  text flows through the Graphics WM_CHAR bridge. A per-monitor-v2 manifest and
  native resize handling preserve logical sizes, with a 640×480 minimum client area.
- `MailKeyboardNavigation` adds enabled-control traversal, automatic scrolling to
  focused fields, tab shortcuts, receive, and cancellation. First run opens Account.
  Full OS screen-reader/UI Automation integration remains the version 7 work item.
  The legacy Graphics input bridge is isolated in the Windows project.

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
array (at most one entry for version 1) or application settings. Unknown fields,
unknown enum values, invalid data, and unsupported versions are rejected.

Updates acquire an exclusive `.lock` file, re-read/validate the existing document,
write and flush a temporary file beside it, and replace the target. The lock file
may remain on disk; an open handle, not file presence, determines lock ownership.
The default local Windows filesystem is the intended storage target; network
filesystem durability guarantees have not been validated.

Unreadable configuration blocks saving on the affected form. The other form can
still be used. Repair/restore the file and restart, or intentionally move the file
to a backup location to start fresh. Missing files alone trigger defaults. Secrets
are never part of the configuration models or JSON. The masked password field is
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

The Broiler.UI scroll view measures content without width constraints. A small
application-owned content wrapper supplies the reading pane width to the standard
label, so long text wraps and scrolls. Account/settings forms use the same viewport
constraint. `TabContent` remeasures content during arrange because the pinned
Broiler tab control initially measures using a 320×220 preferred size, even when
arranged into a larger window. This avoids narrow-column text in the real shell.
Literal ampersands are escaped at the label boundary to avoid Broiler.UI access-key
interpretation.

## Distribution and acceptance

`scripts/Publish-Windows.ps1` publishes a self-contained, untrimmed Windows package,
includes package/runtime license notices, executes its smoke check, and creates a
ZIP and SHA-256 file. The x64 build is validated on this machine. The script also
accepts arm64 for future testing; arm64 is not advertised as validated.

`--demo` uses only an in-memory account and synthetic mail. It does not construct
the Windows credential adapter or read the default configuration directory.
`--data-directory` supports isolated real configuration tests. The user will run
live-provider acceptance later using the [included checklist](version-1-acceptance.md).
Physical multi-monitor and IME language coverage are also recorded as user checks.

## Next implementation slice

Continue with version 2 SMTP/composition and the isolated HTML preview boundary.
Record the user's provider test results when available.
The [initial decision](decisions/0001-version-1-foundation.md) records the foundation.
The [connection decision](decisions/0002-credentials-and-imap-test.md) records credentials/testing.
The [receiving decision](decisions/0003-read-only-receiving.md) records the current reader.
Sending and HTML preview remain separate version 2 work.
