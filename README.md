# Broiler.Mail

A mail application built on .NET 10, Broiler.UI, and Broiler.Graphics. Version 1
is implemented as a small, single-account reader and is ready for your provider
acceptance test. Version 2 development is underway.

Version 2 development includes secure SMTP sending, separate SMTP credentials, and a plain-text
composer with Reply, Reply all, and Forward. The active draft is saved locally and
recovered after restart. Sent copies can be provider-managed or appended via IMAP.
HTML preview is implemented with Broiler.HTML. Renderer process isolation remains
a release gate; current generated packages are unsigned validation builds.

See the [roadmap](docs/roadmap.md) for release scope, Broiler component reuse,
acceptance criteria, and the first implementation steps.

The [component reuse review](docs/component-reuse-review.md) identifies code that
can move upstream and the recommended order for removing application workarounds.

The [experience roadmap](docs/experience-roadmap.md) reviews visual polish,
reading/composing flows, accessibility, and smoothness, with
[native screenshot evidence](docs/ux-review-2026-09-30/README.md) and a
[shared Broiler component roadmap](docs/broiler-experience-components-roadmap.md).

See the [consolidated roadmap audit, 4 October](docs/roadmap-status-2026-10-04.md)
for the current status of every roadmap, remaining work, and validation evidence.
The [detailed UI implementation roadmap](docs/ui-implementation-roadmap.md) splits
the remaining interface work into tracked packages, delivery slices, and native acceptance checks.

The first three milestones are:

1. **Read mail:** settings, one account profile, and receiving messages.
2. **Send and preview:** sending messages and an HTML message preview.
3. **Multiple accounts:** independent mail accounts and clear sender selection.

## Build and run

The portable Windows x64 build is generated under
`artifacts/Broiler.Mail-2.0.0-win-x64/`. Open `Broiler.Mail.Windows.exe` from that
folder, or extract the matching ZIP. The NativeAOT executable needs no installed .NET runtime.
See [Start here / provider checklist](docs/version-2-acceptance.md).

Install the .NET 10 SDK. The native application currently targets Windows.
Dependencies restore from NuGet.org, including Broiler.UI **0.1.0-preview.17** and
its shared forms package, plus Broiler.Hosting **0.1.0-preview.5** for Windows,
Linux, and the Android API probe. Sibling Broiler checkouts and a local package feed are
not required. See the [C-04 implementation notes](docs/c04-forms/README.md)
for validation evidence and remaining accessibility checks.

```powershell
dotnet restore Broiler.Mail.slnx
dotnet build Broiler.Mail.slnx --no-restore -c Release
dotnet run --project src/Broiler.Mail.Windows --no-build -c Release
```

Preview the complete UI with synthetic messages, without saved accounts or network:

```powershell
dotnet run --project src/Broiler.Mail.Windows --no-build -c Release -- --demo
```

For reproducible UI review, name a prepared scenario, for example
`--demo send-unknown --theme dark --size 640x480`. `--help` lists the scenarios.

Create a NativeAOT ZIP and SHA-256 checksum with PowerShell 7 and the Visual Studio
C++ desktop build tools (including the Windows SDK), on the matching Windows architecture:

```powershell
./scripts/Publish-Windows.ps1
```

The publisher includes dependency notices and smoke-tests the resulting executable.
See [hosting package integration](docs/hosting-packages.md) for package scope and validation.
`--data-directory <path>` selects an explicit configuration directory when running
the app for a separate test profile. Credentials still use their account-specific
Windows Credential Manager slots. `--help` lists startup options.

To check application composition and render all four tabs without opening a window:

```powershell
dotnet run --project src/Broiler.Mail.Windows --no-build -c Release -- --smoke-test
```

The native shell has **Inbox**, **Account**, **Settings**, and **Compose** tabs. Save one account
profile and its password, test the IMAP connection, receive mail, and read messages.
Settings include a theme, Comfortable/Compact inbox row spacing, and initial window
size. Saving row spacing updates the inbox immediately without changing text size.
The executable retains console
output for startup diagnostics.

Tab / Shift+Tab traverses enabled controls and reveals fields below the fold.
Ctrl+1/2/3/4 selects Inbox/Account/Settings/Compose; Ctrl+Tab cycles tabs. F5 receives mail,
Ctrl+N starts a message, Ctrl+R / Ctrl+Shift+R / Ctrl+F reply, reply to all, and forward, Escape
goes back from a narrow-window reader or cancels, Alt+Left goes back, and Enter on a selected inbox
message opens it. Shortcuts need their exact modifiers, so AltGr characters type normally.
Settings lists every shortcut. Text fields
support the Windows clipboard; password fields cannot copy or cut their contents.
The Windows host declares per-monitor DPI awareness and positions the default IME
composition window at the caret. Minimum client size is 640×480 logical pixels.

## Set up and test an account

1. Enter the email address, IMAP hostname, port, username, and TLS mode in **Account**.
   Choose the settings supplied by your mail provider, then select **Save account**.
2. Enter a password or provider-issued app password and select **Save password**.
   The field clears; the saved secret is kept in Windows Credential Manager.
3. Select **Test connection**. It checks encryption and authentication without
   selecting a mailbox or fetching messages. Use **Cancel test** to stop; a test
   also has a 20-second deadline.
4. Open **Inbox** and select **Receive mail**, then select a message to read it.
   Use **Load older** for another page and **Cancel** to stop a mail operation.
5. Use **Forget saved password** in Account to remove the saved IMAP credential.

Save profile edits before password operations or testing. Changing host, port,
username, TLS mode, or authentication invalidates the saved password binding;
save the password again for the new details. Display-name/email-only edits preserve
the binding. Entering a new password disables testing until it is explicitly saved
or the field is cleared. On restart the password field is blank, but testing can
use the existing saved credential.

Only password/app-password authentication is available. OAuth-only providers are
not supported yet. Certificate errors cannot be bypassed, and required STARTTLS
never falls back to plaintext authentication. Compatibility is verified against
the controlled local IMAP fixture; no public provider is advertised as validated.

## Configure outgoing mail (version 2 development)

In **Account → Outgoing mail setup**, choose **Configure SMTP**, enter the
provider's SMTP hostname, port, and username, then select **Save account**.
Choose **Required STARTTLS** (typically port 587) or **TLS** (typically port 465)
as directed by the provider; changing the mode does not automatically change the
port. Password/app-password is the available authentication choice. OAuth sign-in
is not implemented. These non-secret settings survive restart.

SMTP setup is optional. Existing profiles open with **Not configured**; selecting
that option and saving removes the outgoing configuration while retaining IMAP.
In the outgoing section, enter **SMTP password / app password** and choose **Save
SMTP password** after saving the account. It uses a separate, server-bound Windows
Credential Manager slot, even when your provider uses the same password for IMAP
and SMTP. The field clears after saving. The **Test connection** action remains
IMAP-only; SMTP authentication occurs when you send.

Use **Forget SMTP password** before removing SMTP setup or resetting the profile.
Changing the outgoing host, port, username, TLS mode, or authentication requires
saving its password again. IMAP credentials remain independent.

## Compose, reply, and forward (version 2 development)

Open **Compose** and choose **New message** after saving an account. Enter To, Cc,
Bcc, subject, and a multiline body. Separate recipients with commas; display names
such as `"Last, First" <person@example.test>` are accepted. **Check draft** validates
the fields without sending. A Bcc-only draft is allowed. The body uses
Broiler.UI.RichEdit for editing, scrolling, and undo, while the draft retains only
plain text. Enter adds a line; Tab moves to the next control.

For **Reply**, **Reply all**, or **Forward**, first select a message in Inbox and
wait for its body to load, then open Compose. Replies prefer Reply-To over From;
reply-all includes the original To/Cc recipients, removes duplicates and the
current account's address, and never copies received Bcc recipients. Reply thread
identifiers are included in outgoing MIME headers. Forward starts with empty recipient
fields and no reply-thread identifiers. Quotes identify truncated or HTML-derived
text; attachments are not forwarded.

Only one draft can be open. Changing tabs or inbox selection keeps it intact.
Use **Discard draft** before starting another composition. The displayed sender is
fixed when the draft starts; changing the saved email address does not silently
change that draft's sender. Edits are automatically saved, including incomplete
recipient fields, and Compose opens with the recovered draft after restart.
**Save draft** retries a failed save. The storage status confirms when the latest
edits are saved; normal window closing waits for saving and keeps the window open
if it fails. Forced termination can lose edits that have not finished saving.

The active draft is stored in `drafts.json` in the selected data directory. This
file contains readable message text and recipients, including Bcc; it is not an
encrypted credential store. A corrupt file or a conflicting write from another
instance produces an error instead of overwriting saved work. Copy unsaved edits
before resolving a conflict and restarting. **Discard draft** removes the saved
composition only after a successful write. Demo mode keeps drafts in memory only.

Submission status and storage status are shown separately. The lifecycle supports
sending, accepted, failed, and unknown outcomes; an interrupted sending record
recovers as unknown and is never automatically retried. **Send** validates the draft,
saves submission intent, and connects to the configured SMTP server. The operation
has a 20-second deadline and requires TLS or STARTTLS with valid certificates.
Bcc addresses are included in the SMTP envelope and omitted from message headers.
Accepted means the server accepted the message, not that it reached the recipient.

Accepted and unknown drafts remain available to read/copy but cannot be resent.
An unknown result requires checking with the recipient/provider before composing
another copy. A definite failure retains an editable draft for an explicit retry.
Choose **Sent-copy handling** in Account after checking your provider's behavior:

- **Not configured** (the default): no app copy is attempted.
- **Provider saves a copy automatically**: Broiler.Mail does not append a duplicate
  or claim it has verified the provider's copy.
- **Broiler.Mail appends one copy via IMAP**: enter the exact path of an existing
  folder and save the account. This uses the saved IMAP password and a separate
  20-second deadline after SMTP acceptance. No folder is created automatically.

The separate Sent-copy status shows saving, saved, failed, or unknown. The private
copy retains Bcc recipients; delivered mail still omits the Bcc header. A failed
copy does not change SMTP acceptance or enable resending. An interrupted or uncertain
copy is never automatically retried, including after restart. Check the folder and
use the retained local composition for manual recovery. Disabling SMTP setup also
clears its saved Sent-copy policy. See the [SMTP provider checklist](docs/version-2-smtp-checklist.md).
Demo mode never enables sending or contacts a server.

## Receive and read mail

Receiving uses the saved account profile. The inbox loads the newest 50 messages
by IMAP arrival order, with sender, subject, and read/unread rows. Selecting a row
loads its body and shows the received date and full bounded header text. Reading
uses read-only access and leaves the server's read/unread flags unchanged. **Read
message** retries a failed or canceled body request. **Receive mail** replaces the
loaded list; a failed refresh keeps the previous list visible.

Plain-text MIME content is preferred. HTML-only messages get a labeled text
extraction with simplified formatting. Images, scripts, styles, external resources,
and clickable links are not rendered. Attachment-only mail has an explicit empty
text state. Reading and network work run away from the UI thread.

Current limits: 50 headers per page, 500 loaded headers per session, 2 MiB per raw
message including attachments, and a 32,000-character text preview. Larger messages
show an error; long text shows a truncation notice. Bodies are fetched only on
selection, and only the selected decoded body is retained. Attachments can be part
of the bounded MIME download but cannot be opened or saved. Mail is held in memory
and must be received again after restart.

Each network operation has a 20-second deadline. If messages arrive or disappear
between pages, **Load older** asks you to receive again. A changed mailbox identity
or deleted message cannot silently open a different message. Saving a changed
account profile clears the loaded mail and cancels pending requests.

Run the automated suite on Windows:

```powershell
dotnet test Broiler.Mail.slnx --no-restore -c Release
```

The IMAP and SMTP tests use loopback-only TLS fixtures. The Windows tests create/update/read/
delete synthetic credentials under random account IDs and clean up in `finally`
blocks; they never enumerate or access existing user credentials. The headless
`--smoke-test` does not access credentials or make network connections.

## Projects

| Project | Responsibility |
| --- | --- |
| `Broiler.Mail.Core` | Account/settings/message models and service interfaces; no UI or platform dependencies. |
| `Broiler.Mail.Application` | Broiler.UI views, configuration workflows, application composition, and preview boundary. |
| `Broiler.Mail.Infrastructure` | Versioned JSON persistence, IMAP reading and Sent-copy append, SMTP sending, MIME handling, and text extraction. |
| `Broiler.Mail.Windows` | Entry point, dependency wiring, Direct2D host, and Windows Credential Manager adapter. |
| `Broiler.Mail.Linux` | X11/EGL project foundation and prerequisite diagnostics; interactive hosting is pending. See [Linux build notes](docs/linux-host.md). |
| `Broiler.Mail.Tests` | Persistence, configuration/credential/inbox workflows, MIME fixtures, and controlled IMAP/TLS tests. |
| `Broiler.Mail.Windows.Tests` | Native credential storage and window-close draft-save tests. |

Package versions are pinned centrally in `Directory.Packages.props`. Shared compiler
settings live in `Directory.Build.props` and SDK selection lives in `global.json`.

## Current implementation status

Account setup saves display name, email address, IMAP host/port, username, and TLS
mode. It validates input and preserves account identity when edited. Settings save
System/Light/Dark theme and initial window dimensions; these apply on restart.
The Windows host forwards input and posts asynchronous save results to its UI thread.

Configuration is stored in `%LOCALAPPDATA%\Broiler.Mail\accounts.json` and
`settings.json`; the active composition uses `drafts.json`. All use a versioned
format and same-directory file replacement.
Writes are serialized with a lock file. Missing files use defaults; corrupt,
unsupported, or unreadable files produce visible errors and disable saving for
that form. Repair/restore the affected file and restart. To reset it intentionally,
move that file to a backup location before restarting; the app does not reset it
automatically. The startup console prints the data directory.

Passwords are stored as user-protected Windows generic credentials under
`Broiler.Mail/<account-id>/Imap` and `Broiler.Mail/<account-id>/Smtp`. A fingerprint binds each credential to its server
configuration. Re-saving replaces that account/protocol slot; no old password
history is kept. Profile JSON and application diagnostics contain no passwords.
Removing/resetting profile JSON does not remove Windows credentials: use **Forget
saved password** and **Forget SMTP password** before resetting, or remove the corresponding Broiler.Mail entries
in Windows Credential Manager afterward.

Version 2 adds SMTP outgoing mail configuration, the plain-text composer with
reply and forward threading, durable draft recovery, secure SMTP submission with
configurable Sent-copy handling, and HTML preview with a plain-text toggle
and bounded inline images. OAuth enum values describe future configuration only.
The current HTML preview runs inside the mail process; OS process isolation remains
an open [security gate](docs/html-renderer-security.md). Passing preview fixtures
does not establish containment of a compromised renderer. Live-provider testing
remains with the user using the [SMTP checklist](docs/version-2-smtp-checklist.md).
Full screen-reader/UI Automation integration remains a later milestone.

See [architecture and implementation notes](docs/architecture.md) for dependency
boundaries, security policies, and roadmap progress.

The [cross-platform roadmap](docs/cross-platform-roadmap.md) starts with the
[Phase 0 foundation](docs/phase-0-foundation.md): pinned package/API verification,
shared Linux/Windows CI, Windows host tests, and unsigned validation packages.
Hosted Linux and ARM64 results remain pending until that workflow runs.

Licensed under [Apache License 2.0](LICENSE).
