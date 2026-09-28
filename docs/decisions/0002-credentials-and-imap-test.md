# Decision 0002: Protected credentials and IMAP connection testing

Status: implemented. Date: 2026-09-28. Extends decision 0001.

Use Windows Credential Manager for saved passwords. No suitable credential bindings
were found in the inspected Broiler.Native checkout, so keep the small Win32 binding
private to the Windows adapter. A generic credential is stored per account/protocol,
with a fingerprint of host, port, username, security mode, and authentication method.
Read only when that fingerprint matches; changing connection details requires saving
the password again. Re-saving replaces the slot; forgetting deletes it. No credentials
are serialized in account/settings files or automatically loaded into UI fields.

Use MailKit 4.18.1 behind `IMailReceiver`. The public constructor uses default TLS
certificate validation. Both implicit TLS and mandatory STARTTLS are supported;
opportunistic or plaintext authentication is not. The initial authentication mode is
password/app-password. OAuth configurations fail before networking. No provider is
claimed as supported solely because it speaks IMAP.

The user saves profile details and the password separately, then explicitly runs the
connection test. Test only the saved, unchanged account and authenticate without
selecting a mailbox. MailKit may query capabilities/folder metadata during setup;
no message bodies or flags are read or changed. Stop on cancellation or a 20-second
deadline. Report safe failure categories without echoing server responses.

Verification uses a controlled loopback IMAP fixture with a temporary certificate
and synthetic credentials. Tests cover TLS, STARTTLS, downgrade prevention, rejected
certificates, failed authentication, missing/bound credentials, cancellation, timeout,
and absence of message operations. Separate Windows tests verify native credential
storage and isolation, cleaning up only their own random credential targets. The
fixture does not constitute acceptance testing against a public mail provider.

Message retrieval, real-provider acceptance, and OAuth sign-in are later work.
