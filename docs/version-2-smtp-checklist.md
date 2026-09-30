# Version 2 SMTP provider checklist

SMTP submission is implemented and tested against a controlled loopback server.
Live-provider validation is still pending. The earlier packaged version 1 build
does not include these changes; build the current source using the [README steps](../README.md#build-and-run).

1. Use a dedicated test mailbox. In Account, configure SMTP according to the
   provider's instructions, save the account, and save its separate SMTP password
   or app password. OAuth-only sign-in is not supported yet.
2. Compose a small plain-text message to a mailbox you control. Close and reopen
   Broiler.Mail first to verify the draft is restored, then select Send.
3. Confirm the app reports server acceptance and the recipient receives the text,
   Unicode subject, and sender correctly. Acceptance alone does not prove delivery.
4. Send another test with To/Cc/Bcc mailboxes you control. Inspect received headers
   to confirm Bcc is absent. Check reply/reply-all threading and a Bcc-only message.
5. Initially leave **Sent-copy handling** at **Not configured**. Record whether the
   provider creates a Sent-folder copy automatically. If so, select **Provider saves
   a copy automatically** to avoid duplicates. Otherwise choose **Broiler.Mail
   appends one copy via IMAP**, enter an existing folder's exact IMAP path, save the
   account, and ensure the IMAP password is saved. Send a new test and verify exactly
   one copy, including Bcc in this private copy and the same Message-ID as received.
6. If sending fails, verify the draft remains. Correct configuration or credentials
   before explicitly retrying. If the result is unknown, check the recipient/provider
   before creating another copy; do not resend automatically.
7. Check that Sent-copy status is separate from SMTP acceptance. An invalid folder
   or missing permission must report a copy failure while retaining acceptance.
   If the copy outcome is unknown, inspect the server folder before recovering a
   copy manually. Restart must not automatically append another copy or resend mail.

Record provider, TLS mode, authentication type, date, observed results, and Sent-copy
behavior. Do not include passwords. The app retains the accepted/unknown composition
locally until Discard draft; it does not enable resending it. Draft files contain
readable message content and recipient addresses, including Bcc.
