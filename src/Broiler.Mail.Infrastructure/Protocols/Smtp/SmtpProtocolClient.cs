// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Infrastructure.Protocols.Transport;

namespace Broiler.Mail.Infrastructure.Protocols.Smtp;

/// <summary>
/// 100% native .NET C# client for the SMTP protocol (RFC 5321, RFC 4954).
/// Trimmer-safe, zero external dependencies.
/// </summary>
public sealed class SmtpProtocolClient : IAsyncDisposable, IDisposable
{
    private readonly MailTransportConnection _connection = new();
    private readonly List<string> _authMechanisms = [];
    private readonly RemoteCertificateValidationCallback? _certValidator;

    public IReadOnlyList<string> AuthenticationMechanisms => _authMechanisms;
    public bool SupportsAuthentication => _authMechanisms.Count > 0;
    public bool SupportsStartTls { get; private set; }

    public SmtpProtocolClient(RemoteCertificateValidationCallback? certValidator = null)
    {
        _certValidator = certValidator;
    }

    public Task ConnectAsync(string host, int port, TransportSecurity security, CancellationToken cancellationToken) =>
        ConnectAsync(host, port, security, _certValidator, cancellationToken);

    public async Task ConnectAsync(
        string host,
        int port,
        TransportSecurity security,
        RemoteCertificateValidationCallback? certValidator,
        CancellationToken cancellationToken)
    {
        try
        {
            await _connection.ConnectAsync(host, port, security, certValidator, cancellationToken).ConfigureAwait(false);
        }
        catch (AuthenticationException ex)
        {
            throw new TlsHandshakeException("TLS handshake or certificate verification failed.", ex);
        }

        // Read 220 greeting
        var greeting = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        if (greeting.StatusCode != 220)
        {
            throw new SmtpCommandException(greeting.StatusCode, greeting.LastLine, SmtpErrorCode.ServiceUnavailable);
        }

        // Send EHLO
        await HandshakeAsync(host, cancellationToken).ConfigureAwait(false);

        // Perform STARTTLS upgrade if requested
        if (security == TransportSecurity.StartTls)
        {
            if (!SupportsStartTls)
            {
                throw new NotSupportedException("STARTTLS is required by account configuration but not offered by the SMTP server.");
            }

            await _connection.WriteLineAsync("STARTTLS", cancellationToken).ConfigureAwait(false);
            var startTlsResponse = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
            if (startTlsResponse.StatusCode != 220)
            {
                throw new SmtpCommandException(startTlsResponse.StatusCode, startTlsResponse.LastLine);
            }

            try
            {
                await _connection.UpgradeToTlsAsync(host, certValidator, cancellationToken).ConfigureAwait(false);
            }
            catch (AuthenticationException ex)
            {
                throw new TlsHandshakeException("TLS handshake or certificate verification failed during STARTTLS upgrade.", ex);
            }

            // Re-issue EHLO after TLS upgrade per RFC 3207
            await HandshakeAsync(host, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandshakeAsync(string host, CancellationToken cancellationToken)
    {
        _authMechanisms.Clear();
        SupportsStartTls = false;

        await _connection.WriteLineAsync("EHLO [127.0.0.1]", cancellationToken).ConfigureAwait(false);
        var response = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != 250)
        {
            // Fallback to HELO
            await _connection.WriteLineAsync("HELO [127.0.0.1]", cancellationToken).ConfigureAwait(false);
            response = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != 250)
            {
                throw new SmtpCommandException(response.StatusCode, response.LastLine);
            }
            return;
        }

        foreach (string line in response.Lines)
        {
            string clean = line.Length > 4 ? line[4..].Trim() : "";
            if (clean.StartsWith("STARTTLS", StringComparison.OrdinalIgnoreCase))
            {
                SupportsStartTls = true;
            }
            else if (clean.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase))
            {
                int sep = clean.IndexOfAny([' ', '=']);
                if (sep > 0)
                {
                    string mechsPart = clean[(sep + 1)..].Trim();
                    foreach (string mech in mechsPart.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!_authMechanisms.Contains(mech, StringComparer.OrdinalIgnoreCase))
                        {
                            _authMechanisms.Add(mech);
                        }
                    }
                }
            }
        }
    }

    public async Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        if (_authMechanisms.Contains("PLAIN", StringComparer.OrdinalIgnoreCase))
        {
            // SASL PLAIN: \0username\0password
            byte[] authPayload = Encoding.UTF8.GetBytes($"\0{username}\0{password}");
            string base64 = Convert.ToBase64String(authPayload);
            await _connection.WriteLineAsync($"AUTH PLAIN {base64}", cancellationToken).ConfigureAwait(false);
            var response = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != 235)
            {
                throw new SmtpAuthenticationException(response.LastLine);
            }
            return;
        }

        if (_authMechanisms.Contains("LOGIN", StringComparer.OrdinalIgnoreCase))
        {
            await _connection.WriteLineAsync("AUTH LOGIN", cancellationToken).ConfigureAwait(false);
            var challenge = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
            if (challenge.StatusCode != 334)
            {
                throw new SmtpAuthenticationException(challenge.LastLine);
            }

            string userB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(username));
            await _connection.WriteLineAsync(userB64, cancellationToken).ConfigureAwait(false);
            var passChallenge = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
            if (passChallenge.StatusCode != 334)
            {
                throw new SmtpAuthenticationException(passChallenge.LastLine);
            }

            string passB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
            await _connection.WriteLineAsync(passB64, cancellationToken).ConfigureAwait(false);
            var response = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != 235)
            {
                throw new SmtpAuthenticationException(response.LastLine);
            }
            return;
        }

        throw new NotSupportedException("No supported password authentication mechanism available on this server.");
    }

    public async Task SendMailAsync(
        string sender,
        IReadOnlyList<string> recipients,
        byte[] messageBytes,
        CancellationToken cancellationToken)
    {
        // Format sender address
        string cleanSender = sender.Trim().Trim('<', '>');
        await _connection.WriteLineAsync($"MAIL FROM:<{cleanSender}>", cancellationToken).ConfigureAwait(false);
        var fromResp = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        if (fromResp.StatusCode != 250)
        {
            throw new SmtpCommandException(fromResp.StatusCode, fromResp.LastLine, SmtpErrorCode.SenderNotAccepted);
        }

        // Format recipient addresses
        foreach (string recipient in recipients)
        {
            string cleanRcpt = recipient.Trim().Trim('<', '>');
            await _connection.WriteLineAsync($"RCPT TO:<{cleanRcpt}>", cancellationToken).ConfigureAwait(false);
            var rcptResp = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
            if (rcptResp.StatusCode is not (250 or 251))
            {
                throw new SmtpCommandException(rcptResp.StatusCode, rcptResp.LastLine, SmtpErrorCode.RecipientNotAccepted);
            }
        }

        // DATA command
        await _connection.WriteLineAsync("DATA", cancellationToken).ConfigureAwait(false);
        var dataResp = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        if (dataResp.StatusCode != 354)
        {
            throw new SmtpCommandException(dataResp.StatusCode, dataResp.LastLine, SmtpErrorCode.MessageNotAccepted);
        }

        // Stream dot-stuffed body
        await StreamDotStuffedBodyAsync(messageBytes, cancellationToken).ConfigureAwait(false);

        // Read final submission acceptance response
        var submitResp = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        if (submitResp.StatusCode != 250)
        {
            throw new SmtpCommandException(submitResp.StatusCode, submitResp.LastLine, SmtpErrorCode.MessageNotAccepted);
        }
    }

    private async Task StreamDotStuffedBodyAsync(byte[] messageBytes, CancellationToken cancellationToken)
    {
        string text = Encoding.UTF8.GetString(messageBytes);
        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var sb = new StringBuilder();

        foreach (string line in lines)
        {
            if (line.StartsWith('.'))
            {
                sb.Append('.'); // Dot-stuffing
            }
            sb.Append(line).Append("\r\n");
        }

        sb.Append(".\r\n"); // End of DATA
        await _connection.WriteBytesAsync(Encoding.UTF8.GetBytes(sb.ToString()), cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(bool sendQuit, CancellationToken cancellationToken)
    {
        if (sendQuit && _connection.IsConnected)
        {
            try
            {
                await _connection.WriteLineAsync("QUIT", cancellationToken).ConfigureAwait(false);
                await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Disconnect errors during QUIT do not fail the session
            }
        }
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<(int StatusCode, string LastLine, List<string> Lines)> ReadResponseAsync(CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        while (true)
        {
            string line = await _connection.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            lines.Add(line);

            if (line.Length < 3 || !int.TryParse(line.AsSpan(0, 3), out int code))
            {
                throw new SmtpProtocolException($"Invalid SMTP response line: {line}");
            }

            // If line is 3 chars or 4th char is not '-', it's the final line
            if (line.Length == 3 || line[3] != '-')
            {
                return (code, line, lines);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
