# Broiler.Mail 2.0 — Start here

Version 2 turns Broiler.Mail into a two-way mail client with sending and HTML preview.
It adds optional SMTP account configuration, a plain-text composer with reply and forward
threading, local draft recovery, secure SMTP submission, configurable Sent-copy handling,
and isolated HTML preview with a plain-text toggle and bounded inline images.

## Run

Build the current source or extract the published release package:
- Package: `artifacts/Broiler.Mail-1.0.0-win-x64.zip`
- Application executable: `Broiler.Mail.Windows.exe`
- Demo preview: `Broiler.Mail.Windows.exe --demo`

In demo mode:
1. Open the **Inbox** tab and select **Receive mail**.
2. Select message 54 ("HTML-only mail — text preview").
3. Select **Open HTML preview** to view the sandboxed HTML snapshot with the inline demo logo.
4. Toggle between **Show plain text** and **Show HTML** in the preview window.
5. Select **Compose** (Ctrl+4) to write a test message or reply to message 54.

## Compose, send, and Sent-copy workflow

1. **Account setup:** In the **Account** tab (Ctrl+2), enable outgoing server settings,
   enter your provider's SMTP hostname, port, username, and TLS mode (defaults to port 587
   with STARTTLS), and select **Save account**.
2. **SMTP password:** Enter your SMTP password or app password and select **Save SMTP password**.
   It is stored in Windows Credential Manager under `Broiler.Mail/<account-id>/Smtp`.
3. **Sent-copy handling:** Choose between:
   - **Provider saves a copy automatically** (use if your provider copies sent mail to Sent).
   - **Broiler.Mail appends one copy via IMAP** (enter an existing IMAP folder path, e.g., `Sent` or `INBOX.Sent`).
4. **Compose:** In the **Compose** tab (Ctrl+4), select **New message**, or select a message
   in the Inbox and choose **Reply**, **Reply all**, or **Forward**.
5. **Draft recovery:** Edits are saved automatically to `%LOCALAPPDATA%\Broiler.Mail\drafts.json`.
   Closing and reopening Broiler.Mail restores unfinished drafts.
6. **Send:** Select **Send**. The app reports server acceptance separately from Sent-copy status.
   If sending fails or is interrupted, the draft is preserved locally for review.

## HTML Preview features

- **Isolated preview:** Click **Open HTML preview** in the reading pane to render in a private
  sandboxed WebView2 window.
- **Embedded `cid:` images:** Inline images from MIME `multipart/related` messages are resolved
  exclusively within the current message and displayed inline with bounded sizing (max 1 MiB per image).
- **Remote images:** Remote images are blocked by default. Click **Load remote images** to load them
  through a controlled .NET HTTP channel enforcing non-SVG verification and 5 MB bounds.
- **Plain-text toggle:** Click **Show plain text** / **Show HTML** to switch views.
- **External links:** Clicking web links opens your default browser; internal navigation is blocked.
- **Resilience:** Renderer crashes fall back to the plain-text body without crashing the client.

## Keyboard shortcuts

- Ctrl+1: Inbox
- Ctrl+2: Account
- Ctrl+3: Settings
- Ctrl+4: Compose
- Ctrl+Tab / Ctrl+Shift+Tab: switch tabs
- F5: receive mail
- Escape: cancel active operation
- Tab / Shift+Tab: move between controls; off-screen fields scroll into view

See the [SMTP provider checklist](version-2-smtp-checklist.md) for live-provider testing.
