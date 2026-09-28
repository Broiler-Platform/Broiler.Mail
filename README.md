# Broiler.Mail

A mail application built on .NET 10, Broiler.UI, and Broiler.Graphics. Version 1
is implemented as a small, single-account reader and is ready for your provider
acceptance test. Sending and multiple accounts are the next milestones.

See the [roadmap](docs/roadmap.md) for release scope, Broiler component reuse,
acceptance criteria, and the first implementation steps.

The [component reuse review](docs/component-reuse-review.md) identifies code that
can move upstream and the recommended order for removing application workarounds.

The first three milestones are:

1. **Read mail:** settings, one account profile, and receiving messages.
2. **Send and preview:** sending messages and an HTML message preview.
3. **Multiple accounts:** independent mail accounts and clear sender selection.

## Build and run

The portable Windows x64 build is generated under
`artifacts/Broiler.Mail-1.0.0-win-x64/`. Open `Broiler.Mail.Windows.exe` from that
folder, or extract the matching ZIP. It includes its .NET runtime.
See [Start here / provider checklist](docs/version-1-acceptance.md).

Install the .NET 10 SDK. The native application currently targets Windows.
Dependencies restore from NuGet.org; sibling Broiler repositories are not required.

```powershell
dotnet restore Broiler.Mail.slnx
dotnet build Broiler.Mail.slnx --no-restore -c Release
dotnet run --project src/Broiler.Mail.Windows --no-build -c Release
```

Preview the complete UI with synthetic messages, without saved accounts or network:

```powershell
dotnet run --project src/Broiler.Mail.Windows --no-build -c Release -- --demo
```

Create a self-contained ZIP and SHA-256 checksum with PowerShell 7:

```powershell
./scripts/Publish-Windows.ps1
```

The publisher includes dependency notices and smoke-tests the resulting executable.
`--data-directory <path>` selects an explicit configuration directory when running
the app for a separate test profile. Credentials still use their account-specific
Windows Credential Manager slots. `--help` lists startup options.

To check application composition and render all three tabs without opening a window:

```powershell
dotnet run --project src/Broiler.Mail.Windows --no-build -c Release -- --smoke-test
```

The native shell has **Inbox**, **Account**, and **Settings** tabs. Save one account
profile and its password, test the IMAP connection, receive mail, and read messages.
Settings include a theme and initial window size. The executable retains console
output for startup diagnostics.

Tab / Shift+Tab traverses enabled controls and reveals fields below the fold.
Ctrl+1/2/3 selects Inbox/Account/Settings; Ctrl+Tab cycles tabs. F5 receives mail,
Escape cancels, and Enter on a selected inbox message retries reading. Text fields
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

The IMAP tests use a loopback-only TLS fixture. The Windows tests create/update/read/
delete synthetic credentials under random account IDs and clean up in `finally`
blocks; they never enumerate or access existing user credentials. The headless
`--smoke-test` does not access credentials or make network connections.

## Projects

| Project | Responsibility |
| --- | --- |
| `Broiler.Mail.Core` | Account/settings/message models and service interfaces; no UI or platform dependencies. |
| `Broiler.Mail.Application` | Broiler.UI views, configuration workflows, application composition, and preview boundary. |
| `Broiler.Mail.Infrastructure` | Versioned JSON persistence, read-only MailKit IMAP, MIME decoding, and text extraction. |
| `Broiler.Mail.Windows` | Entry point, dependency wiring, Direct2D host, and Windows Credential Manager adapter. |
| `Broiler.Mail.Tests` | Persistence, configuration/credential/inbox workflows, MIME fixtures, and controlled IMAP/TLS tests. |
| `Broiler.Mail.Windows.Tests` | Native credential storage, isolation, update, and cleanup tests. |

Package versions are pinned centrally in `Directory.Packages.props`. Shared compiler
settings live in `Directory.Build.props` and SDK selection lives in `global.json`.

## Current implementation status

Account setup saves display name, email address, IMAP host/port, username, and TLS
mode. It validates input and preserves account identity when edited. Settings save
System/Light/Dark theme and initial window dimensions; these apply on restart.
The Windows host forwards input and posts asynchronous save results to its UI thread.

Configuration is stored in `%LOCALAPPDATA%\Broiler.Mail\accounts.json` and
`settings.json`, with a versioned format and same-directory file replacement.
Writes are serialized with a lock file. Missing files use defaults; corrupt,
unsupported, or unreadable files produce visible errors and disable saving for
that form. Repair/restore the affected file and restart. To reset it intentionally,
move that file to a backup location before restarting; the app does not reset it
automatically. The startup console prints the data directory.

Passwords are stored as user-protected Windows generic credentials under
`Broiler.Mail/<account-id>/Imap`. A fingerprint binds each credential to its server
configuration. Re-saving replaces that account/protocol slot; no old password
history is kept. Profile JSON and application diagnostics contain no passwords.
Removing/resetting profile JSON does not remove Windows credentials: use **Forget
saved password** before resetting, or remove the corresponding Broiler.Mail entry
in Windows Credential Manager afterward.

SMTP and isolated HTML rendering remain **skeletons** that throw
`NotImplementedException` if called. OAuth enum values describe future configuration
only. Automated version 1 acceptance and native demo inspection are complete.
You chose to run live-provider testing later; the acceptance checklist records this
separately. Full screen-reader/UI Automation integration remains a later milestone.

See [architecture and implementation notes](docs/architecture.md) for dependency
boundaries, known host limitations, and where to continue with version 2.

Licensed under [Apache License 2.0](LICENSE).
