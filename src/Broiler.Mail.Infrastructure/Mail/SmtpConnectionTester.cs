using System.Net.Sockets;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace Broiler.Mail.Infrastructure.Mail;

/// <summary>
/// Signs in to the saved SMTP server and leaves again: connect with the required TLS mode, authenticate, QUIT.
/// Nothing else is sent, so no message, sender or recipient ever reaches the server.
/// </summary>
public sealed class SmtpConnectionTester : IOutgoingConnectionTester
{
    private static readonly TimeSpan DefaultQuitBudget = TimeSpan.FromSeconds(2);
    private readonly ICredentialStore _credentials;
    private readonly Func<SmtpClient> _createClient;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _quitBudget;

    public SmtpConnectionTester(ICredentialStore credentials) : this(credentials, () => new SmtpClient(), TimeSpan.FromSeconds(20)) { }

    // Test-only seam for a fixture certificate and short deadlines; production uses platform certificate validation.
    internal SmtpConnectionTester(ICredentialStore credentials, Func<SmtpClient> createClient, TimeSpan timeout, TimeSpan? quitBudget = null)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _createClient = createClient;
        _timeout = timeout;
        _quitBudget = quitBudget ?? DefaultQuitBudget;
    }

    public async Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default)
    {
        // Everything that can be decided without the network is decided first; no client exists yet.
        ConfigurationValidator.Validate(account);
        cancellationToken.ThrowIfCancellationRequested();
        var server = account.OutgoingServer ?? throw new MailConnectionException("Set up and save the SMTP server first.", MailConnectionFailure.Setup);
        if (!account.IsEnabled) throw new MailConnectionException("Enable the account before testing the SMTP sign-in.", MailConnectionFailure.Setup);
        if (server.Authentication != AuthenticationMethod.Password)
            throw new MailConnectionException("SMTP OAuth sign-in is not available yet. Use password or app-password authentication.", MailConnectionFailure.UnsupportedSignIn);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        string? secret;
        try
        {
            // Only the SMTP slot bound to these exact server details; never the IMAP password.
            secret = await _credentials.ReadAsync(CredentialKey.For(account, MailProtocol.Smtp), deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw Failure(MailConnectionFailure.Timeout); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Failure(MailConnectionFailure.CredentialStore);
        }
        if (string.IsNullOrEmpty(secret)) throw Failure(MailConnectionFailure.MissingPassword);

        bool connected = false;
        try
        {
            using var client = _createClient(); // No protocol logger and no certificate-validation bypass.
            // MailKit's own read timeout is only a backstop behind the deadline, as for IMAP.
            client.Timeout = checked((int)(_timeout + TimeSpan.FromSeconds(1)).TotalMilliseconds);
            var security = server.Security == TransportSecurity.Tls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(server.Host, server.Port, security, deadline.Token).ConfigureAwait(false);
            connected = true;

            if (!client.Capabilities.HasFlag(SmtpCapabilities.Authentication))
                throw Failure(MailConnectionFailure.AuthenticationUnavailable);
            // MailKit never uses OAuth mechanisms with a password. Without another usable mechanism it reports no
            // compatible mechanism, which would read as a server without sign-in on this port, so an OAuth-only
            // server is named for what it is.
            if (!client.AuthenticationMechanisms.Any(IsPasswordMechanism))
                throw Failure(MailConnectionFailure.UnsupportedSignIn);
            await client.AuthenticateAsync(server.UserName, secret, deadline.Token).ConfigureAwait(false);

            // Signed in. QUIT is a courtesy with its own short budget: no answer, or a broken connection,
            // does not turn the pass into a failure or into a long wait.
            using var quit = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            quit.CancelAfter(_quitBudget);
            try { await client.DisconnectAsync(true, quit.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException) { }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Whatever the error looked like, a canceled test is a cancellation, not a finding.
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception error) when (Classify(error, connected, deadline.IsCancellationRequested) is { } failure)
        {
            throw Failure(failure);
        }
        // Cancel pressed while QUIT was under way still means canceled, never a success.
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool IsPasswordMechanism(string mechanism) =>
        SaslMechanism.IsSupported(mechanism) && !mechanism.Equals("XOAUTH2", StringComparison.OrdinalIgnoreCase)
        && !mechanism.Equals("OAUTHBEARER", StringComparison.OrdinalIgnoreCase) && !mechanism.Equals("ANONYMOUS", StringComparison.OrdinalIgnoreCase);

    private static MailConnectionFailure? Classify(Exception error, bool connected, bool deadlineFired) => error switch
    {
        // TLS stream reads can surface deadline cancellation as an I/O error instead of OCE.
        IOException or SocketException when (deadlineFired || IsTimeout(error)) => MailConnectionFailure.Timeout,
        OperationCanceledException or TimeoutException => MailConnectionFailure.Timeout,
        // MailKit wraps a handshake cut off by the deadline as a handshake failure. A server that never answers
        // the handshake is a reachability problem; blaming its certificate would send the user the wrong way.
        SslHandshakeException when (deadlineFired || IsTimeout(error)) => MailConnectionFailure.Timeout,
        SslHandshakeException => MailConnectionFailure.TlsVerification,
        // Before the connection is up, only required STARTTLS is refused this way.
        NotSupportedException => connected ? MailConnectionFailure.AuthenticationUnavailable : MailConnectionFailure.TlsUnavailable,
        MailKit.Security.AuthenticationException => MailConnectionFailure.AuthenticationRejected,
        SmtpCommandException => MailConnectionFailure.ServerRefused,
        SmtpProtocolException or SaslException => MailConnectionFailure.Interrupted,
        SocketException => MailConnectionFailure.Unreachable,
        IOException => MailConnectionFailure.Interrupted,
        _ => null,
    };

    private static bool IsTimeout(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is TimeoutException or SocketException { SocketErrorCode: SocketError.TimedOut })
                return true;
        return false;
    }

    // Fixed app text only: never the server's reply, which can echo what was sent, and never the secret.
    private static MailConnectionException Failure(MailConnectionFailure failure) => new(failure switch
    {
        MailConnectionFailure.MissingPassword => "No SMTP password is saved for these server details. Save the SMTP password, then test again.",
        MailConnectionFailure.CredentialStore => "The saved SMTP password could not be read. Save it again, then test again.",
        MailConnectionFailure.Unreachable => "The SMTP server could not be reached. Check its address, port, and your network.",
        MailConnectionFailure.Timeout => "The SMTP server did not answer in time. Check the server address, port, and connection security, then test again.",
        MailConnectionFailure.TlsVerification => "The encrypted connection could not be verified. Check the SMTP server name and certificate, and that connection security matches the port (usually TLS on 465, STARTTLS on 587). Certificate errors cannot be bypassed.",
        MailConnectionFailure.TlsUnavailable => "The SMTP server does not offer STARTTLS, so the password was not sent. Check the port and connection security (usually TLS on 465, STARTTLS on 587).",
        MailConnectionFailure.AuthenticationUnavailable => "The SMTP server does not offer sign-in on this connection, so the password was not sent. Check the port; mail submission usually uses 465 or 587.",
        MailConnectionFailure.UnsupportedSignIn => "The SMTP server offers no password sign-in. It may require OAuth, which is not available yet.",
        MailConnectionFailure.AuthenticationRejected => "The SMTP server rejected the sign-in. Check the SMTP username and password. Some providers require an app password or SMTP sign-in to be turned on.",
        MailConnectionFailure.ServerRefused => "The SMTP server refused the connection or the sign-in. Check the server details, then test again later.",
        _ => "The connection to the SMTP server was interrupted or its reply could not be read. Test again.",
    }, failure);
}
