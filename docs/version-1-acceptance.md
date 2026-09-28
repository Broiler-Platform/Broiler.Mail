# Broiler.Mail 1.0 — Start here

Version 1 is a Windows mail reader for one manually configured IMAP account.
Settings, protected passwords, receiving, and plain-text reading are implemented.
The release build is ready for your provider test, which you chose to run later.
Public-provider compatibility has not been certified.

## Run

Extract the complete ZIP to a folder, then open `Broiler.Mail.Windows.exe`.
The self-contained Windows x64 build includes its .NET runtime; no SDK or installer
is required. Windows 10/11 and a working Direct2D graphics environment are required.
This is an unsigned portable build. Application data lives under
`%LOCALAPPDATA%\Broiler.Mail`, independently of the extracted program folder.

For a preview with synthetic mail and no files, saved credentials, or networking,
run `Broiler.Mail.Windows.exe --demo`. Select **Receive mail**, then select a row.
The 55 sample messages exercise paging, the text fallback label, and scrolling.
Demo settings/account edits are temporary. Password operations are disabled by the
demo service and do not touch Windows Credential Manager.

## Configure your test account

1. In Account, enter your display name, email address, IMAP hostname, port,
   username, and TLS mode. Select **Save account**.
2. Enter your password or app password and select **Save password**. It is stored
   in Windows Credential Manager; the input field is cleared.
3. Select **Test connection**, then open Inbox and select **Receive mail**.
4. Select a message. **Load older** adds another page. **Read message** retries a
   failed body request. **Cancel** stops pending work.

Only password/app-password authentication is supported. Use the connection details
provided by your provider. OAuth-only accounts require a later integration. TLS
certificate verification is required and cannot be bypassed.

## Keyboard

- Tab / Shift+Tab: move between enabled controls; off-screen fields scroll into view.
- Ctrl+1 / Ctrl+2 / Ctrl+3: Inbox / Account / Settings.
- Ctrl+Tab / Ctrl+Shift+Tab: switch tabs.
- Arrow keys: navigate the focused list, tab strip, or combo box.
- F5: receive mail. Escape: cancel receiving or the connection test.
- Enter / Space: activate a focused button; Enter on the inbox list retries reading.
- Page Up / Page Down / Home / End: scroll the focused reading pane.
- Ctrl+C / Ctrl+X / Ctrl+V: text-field clipboard operations. Password fields allow
  paste but cannot copy or cut their contents.

## Provider acceptance checklist (your follow-up)

- [ ] Save an account and theme/window settings. Close and reopen the app; verify
  the same account and preferences load and the saved password works.
- [ ] Connect, receive, and read a plain-text and an HTML-only message.
- [ ] Refresh twice; confirm no duplicate rows. Load older messages.
- [ ] Check in another client that reading in Broiler.Mail leaves read/unread flags unchanged.
- [ ] Try an incorrect app password, restore the correct one, and retry without restarting.
- [ ] Disconnect the network during receive, cancel, reconnect, and retry.
- [ ] Check empty/attachment-only mail, long text, and large messages; each should
  show an understandable state without hanging the UI.
- [ ] Check keyboard navigation, resizing, your display scaling, and any IME you use.

Record provider/server, TLS mode, password or app-password authentication, Windows
version, scaling, and outcomes. Keep credentials and private mail out of reports.

## Limits and recovery

The reader loads 50 headers per page, up to 500 per session. It reads raw messages
up to 2 MiB including attachments and shows at most 32,000 text characters, with
an explicit truncation notice. New deliveries or deletions may require a fresh
receive before loading older pages. Each network operation has a 20-second deadline.

Mail is held in memory and received again after restart. Sending, HTML rendering,
multiple accounts, attachments, background sync, and offline mail are later versions.
Full Windows screen-reader/UI Automation support remains a later milestone; version
1 includes visible labels, keyboard focus/navigation, and light/dark themes.

Changing connection details requires saving the password again. To remove a saved
password use **Forget saved password** before resetting the profile. Configuration
files are `accounts.json` and `settings.json` in the data directory. Corruption is
reported and saving is disabled for that form; repair/restore or intentionally move
the affected file to a backup location, then restart. The app never resets it silently.

## Verification performed

Automated checks cover persistence/restart, protected Windows credential storage,
TLS and required STARTTLS, cancellation/timeouts, read-only paging, MIME/text
extraction, mailbox identity changes, stale asynchronous results, and recovery.
Full-shell layout/keyboard checks cover 640×480 and 1100×720 in light/dark themes.
Native demo inspection verified readable forms, the corrected reading pane,
synthetic receiving, HTML-fallback labeling, scrolling layout, and keyboard tab
switching/focus. Live-provider, physical multi-monitor DPI, and IME language-matrix
testing remain user acceptance work.
