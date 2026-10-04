# Decision 0006: Non-sending SMTP sign-in test

Status: implemented. Date: 2026-10-04. Extends decisions 0002 and 0004.

Account setup can test the SMTP sign-in without sending anything. The test is a
separate Core contract, `IOutgoingConnectionTester`, implemented by
`SmtpConnectionTester` in Infrastructure. It is not a member of `IMailSender`: code
that tests a sign-in cannot reach submission at the type level, and the existing
sender implementations do not change.

## What the test does

The test runs only when the user selects **Test SMTP sign-in**. It never runs after a
save or at startup, and it never retries, so repeated sign-ins cannot trip provider
lockouts on their own.

1. Before any network use: the profile is validated; a missing outgoing server or a
   disabled account is refused (`Setup`); OAuth is refused (`UnsupportedSignIn`); and
   a canceled request stays canceled.
2. The password is read only from the SMTP slot bound to the saved outgoing server
   (`CredentialKey.For(account, MailProtocol.Smtp)`), never from the IMAP slot. A
   password saved for other server details is not released (`MissingPassword`).
3. It connects with implicit TLS (`SslOnConnect`) or required STARTTLS, with
   platform certificate validation. Without STARTTLS, nothing secret is sent.
4. A server that offers no AUTH (`AuthenticationUnavailable`) or only OAuth
   mechanisms (`UnsupportedSignIn`) is named for what it is, instead of reporting a
   wrong password.
5. It authenticates, then sends a best-effort QUIT within its own budget of at most
   2 seconds (and within the overall deadline). A QUIT that fails or gets no answer
   does not turn a pass into a failure.

The only commands sent are EHLO (or MailKit's HELO fallback), STARTTLS, AUTH and
QUIT. MAIL, RCPT, DATA, BDAT, VRFY, EXPN, ETRN, RSET and NOOP are never sent, and no
message is built. The deadline is 20 seconds, with MailKit's own timeout one second
behind it as a backstop, as for IMAP. Cancellation by the user at any point, even
after the server accepted the sign-in, is reported as canceled, never as a pass.

## Results and wording

`MailConnectionException` gained a `Failure` kind (`MailConnectionFailure`). It
defaults to `Unspecified`, so existing callers are unchanged; the IMAP receiver does
not classify its failures yet. Messages stay fixed app text: server replies, which
can echo what was sent, and secrets never reach them. A TLS failure names the
server name, the certificate, the port and the connection security, because TLS
against a STARTTLS port fails the same way as an untrusted certificate.

| Kind | Meaning |
| --- | --- |
| `Setup` | No saved outgoing server, or the account is disabled |
| `UnsupportedSignIn` | OAuth profile, or the server offers no password mechanism |
| `MissingPassword` | No SMTP password bound to these server details |
| `CredentialStore` | The protected store could not be read |
| `Unreachable`, `Timeout` | No connection, or no answer before the deadline |
| `TlsVerification`, `TlsUnavailable` | Unverified TLS, or required STARTTLS missing |
| `AuthenticationUnavailable`, `AuthenticationRejected` | No AUTH offered, or sign-in rejected |
| `ServerRefused`, `Interrupted` | The server refused a step, or the connection broke |

A pass proves an encrypted connection and an accepted sign-in only. It does not
prove that the server will accept the sender or the recipients, or that mail will be
delivered. The texts therefore say "sign-in tested; no message was sent", never that
sending works.

The result is kept in memory only and is never persisted. It stands on the
checklist's outgoing line. The receiving checklist (`NextStep`) never depends on it,
and the next-step button never offers it: sending stays optional. Each protocol's
result is reset only by its own changes. Saving new outgoing server details, or
saving or forgetting the SMTP password, resets the SMTP result. Saving new incoming
server details resets the IMAP result. Changing the account's enabled state resets
both. Adding outgoing mail no longer undoes a passed receiving test. A test refused
before it starts because of unsaved edits is not a result, so it leaves the saved
profile's result in place.

## Interface

The button sits in the SMTP section beside the SMTP password buttons, inside the
SMTP fields. It is therefore no Tab stop while outgoing mail is off, and the bottom
bar's order is unchanged. It is enabled only when a saved outgoing server exists, the
SMTP password is not known to be missing, the SMTP password box is empty (typed text
is never silently replaced by the saved secret), and nothing else is running. Only
one test runs at a time. **Cancel test** and Escape stop either test, and focus then
returns to the button of the test that ran. Progress ("Signing in to the SMTP
server… No message is sent.") and the result are each announced once; checklist
changes are silent.

Demo mode uses a synthetic tester that never opens a connection or reads a
credential. The `smtp-test-failed` fixture produces a sign-in rejection through the
ordinary command, and `smtp-test-passed` produces a pass. Every other demo scenario
refuses the test, as it refuses the IMAP test.

## Verification and open work

The loopback SMTP fixture gained a connection-test mode that fails the test on any
command besides EHLO, HELO, STARTTLS, AUTH and QUIT. It also gained servers without
AUTH, OAuth-only servers, a refused greeting, and stalled or dropped AUTH and QUIT.
Tests cover TLS and STARTTLS passes with QUIT; every failure kind; the deadline; user
cancellation before connecting, during AUTH, and during QUIT; credential binding and
protocol isolation; and the account form from saving the SMTP password to a pass
without any submission.

A live check against a real provider remains user-owned
([checklist](../version-2-smtp-checklist.md)). A screen-reader speech check of the
announcements belongs to the open H-01 pass. Classifying IMAP failures with the same
kinds is later work.
