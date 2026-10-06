// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Infrastructure.Protocols.Smtp;
using Broiler.Mail.Infrastructure.Protocols.Transport;

namespace Broiler.Mail.Infrastructure.Protocols.Imap;

public sealed record ImapMailboxInfo(int Count, uint UidValidity, uint? UidNext);

/// <summary>
/// 100% native .NET C# client for the IMAP protocol (RFC 3501, RFC 9051).
/// Tagged command pipeline, bounded streaming, trimmer-safe with zero external dependencies.
/// </summary>
public sealed class ImapProtocolClient : IAsyncDisposable, IDisposable
{
    private readonly MailTransportConnection _connection = new();
    private readonly RemoteCertificateValidationCallback? _certValidator;
    private int _tagCounter = 0;
    private readonly List<string> _capabilities = [];

    public IReadOnlyList<string> Capabilities => _capabilities;
    public bool SupportsStartTls => _capabilities.Contains("STARTTLS", StringComparer.OrdinalIgnoreCase);

    public ImapProtocolClient(RemoteCertificateValidationCallback? certValidator = null)
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

        // Read * OK greeting
        string greeting = await _connection.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (!greeting.StartsWith("* OK", StringComparison.OrdinalIgnoreCase))
        {
            throw new ImapProtocolException($"Invalid IMAP greeting: {greeting}");
        }

        // Query initial capabilities
        await QueryCapabilitiesAsync(cancellationToken).ConfigureAwait(false);

        // Perform STARTTLS if requested
        if (security == TransportSecurity.StartTls)
        {
            if (!SupportsStartTls)
            {
                throw new NotSupportedException("STARTTLS is required by account configuration but not offered by the IMAP server.");
            }

            var startTlsResult = await SendCommandAsync("STARTTLS", cancellationToken).ConfigureAwait(false);
            if (!startTlsResult.IsOk)
            {
                throw new ImapCommandException(startTlsResult.Tag, startTlsResult.StatusText);
            }

            try
            {
                await _connection.UpgradeToTlsAsync(host, certValidator, cancellationToken).ConfigureAwait(false);
            }
            catch (AuthenticationException ex)
            {
                throw new TlsHandshakeException("TLS handshake or certificate verification failed during STARTTLS upgrade.", ex);
            }

            // Re-query capabilities after TLS upgrade
            await QueryCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task QueryCapabilitiesAsync(CancellationToken cancellationToken)
    {
        _capabilities.Clear();
        var result = await SendCommandAsync("CAPABILITY", cancellationToken).ConfigureAwait(false);
        foreach (string line in result.UntaggedLines)
        {
            if (line.StartsWith("* CAPABILITY ", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = line[13..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                _capabilities.AddRange(parts);
            }
        }
    }

    public async Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        // Check SASL PLAIN / SASL-IR
        bool hasPlain = _capabilities.Any(c => c.Equals("AUTH=PLAIN", StringComparison.OrdinalIgnoreCase));
        bool hasSaslIr = _capabilities.Any(c => c.Equals("SASL-IR", StringComparison.OrdinalIgnoreCase));

        if (hasPlain && hasSaslIr)
        {
            byte[] authPayload = Encoding.UTF8.GetBytes($"\0{username}\0{password}");
            string b64 = Convert.ToBase64String(authPayload);
            var result = await SendCommandAsync($"AUTHENTICATE PLAIN {b64}", cancellationToken).ConfigureAwait(false);
            if (result.IsOk) return;
            throw new ImapAuthenticationException(result.StatusText);
        }

        if (hasPlain)
        {
            string tag = NextTag();
            await _connection.WriteLineAsync($"{tag} AUTHENTICATE PLAIN", cancellationToken).ConfigureAwait(false);
            string prompt = await _connection.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (prompt.StartsWith('+'))
            {
                byte[] authPayload = Encoding.UTF8.GetBytes($"\0{username}\0{password}");
                string b64 = Convert.ToBase64String(authPayload);
                await _connection.WriteLineAsync(b64, cancellationToken).ConfigureAwait(false);
                var result = await ReadTaggedResponseAsync(tag, cancellationToken).ConfigureAwait(false);
                if (result.IsOk) return;
                throw new ImapAuthenticationException(result.StatusText);
            }
        }

        // Standard LOGIN
        string escapedUser = EscapeString(username);
        string escapedPass = EscapeString(password);
        var loginResult = await SendCommandAsync($"LOGIN \"{escapedUser}\" \"{escapedPass}\"", cancellationToken).ConfigureAwait(false);
        if (loginResult.IsOk) return;
        throw new ImapAuthenticationException(loginResult.StatusText);
    }

    public async Task<ImapMailboxInfo> ExamineInboxAsync(CancellationToken cancellationToken)
    {
        var result = await SendCommandAsync("EXAMINE \"INBOX\"", cancellationToken).ConfigureAwait(false);
        if (!result.IsOk)
        {
            throw new ImapCommandException(result.Tag, result.StatusText);
        }

        int count = 0;
        uint validity = 0;
        uint? nextUid = null;

        foreach (string line in result.UntaggedLines)
        {
            var existsMatch = Regex.Match(line, @"^\*\s+(\d+)\s+EXISTS", RegexOptions.IgnoreCase);
            if (existsMatch.Success)
            {
                count = int.Parse(existsMatch.Groups[1].Value);
                continue;
            }

            var validityMatch = Regex.Match(line, @"UIDVALIDITY\s+(\d+)", RegexOptions.IgnoreCase);
            if (validityMatch.Success)
            {
                validity = uint.Parse(validityMatch.Groups[1].Value);
                continue;
            }

            var nextMatch = Regex.Match(line, @"UIDNEXT\s+(\d+)", RegexOptions.IgnoreCase);
            if (nextMatch.Success)
            {
                nextUid = uint.Parse(nextMatch.Groups[1].Value);
                continue;
            }
        }

        return new ImapMailboxInfo(count, validity, nextUid);
    }

    public async Task<List<ImapMessageSummaryInfo>> FetchSummariesAsync(int start, int end, CancellationToken cancellationToken)
    {
        var result = await SendCommandAsync($"FETCH {start}:{end} (UID ENVELOPE INTERNALDATE FLAGS)", cancellationToken).ConfigureAwait(false);
        if (!result.IsOk)
        {
            throw new ImapCommandException(result.Tag, result.StatusText);
        }

        var list = new List<ImapMessageSummaryInfo>();
        foreach (string line in result.UntaggedLines)
        {
            var summary = ImapEnvelopeParser.ParseFetchLine(line);
            if (summary is not null)
            {
                list.Add(summary);
            }
        }

        return list;
    }

    public async Task<Stream> GetBodyStreamAsync(
        uint uid,
        int offset,
        int maxBytesPlusOne,
        CancellationToken cancellationToken,
        Action<long, long>? progress = null)
    {
        string tag = NextTag();
        string command = $"UID FETCH {uid} (BODY.PEEK[]<{offset}.{maxBytesPlusOne}>)";
        await _connection.WriteLineAsync($"{tag} {command}", cancellationToken).ConfigureAwait(false);

        var destination = new MemoryStream();
        bool messageFound = false;

        while (true)
        {
            string line = await _connection.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line.StartsWith(tag, StringComparison.Ordinal))
            {
                // Tagged completion
                string[] parts = line.Split(' ', 3);
                if (parts.Length >= 2 && parts[1].Equals("OK", StringComparison.OrdinalIgnoreCase))
                {
                    if (!messageFound)
                    {
                        throw new ImapMessageNotFoundException($"Message with UID {uid} was not found on the server.");
                    }
                    destination.Position = 0;
                    return destination;
                }
                throw new ImapCommandException(tag, line);
            }

            if (line.StartsWith("* "))
            {
                // Check if this line introduces a literal payload: ... {length}
                var literalMatch = Regex.Match(line, @"\{(\d+)\}$");
                if (literalMatch.Success && line.Contains("BODY", StringComparison.OrdinalIgnoreCase))
                {
                    messageFound = true;
                    int literalLength = int.Parse(literalMatch.Groups[1].Value);
                    progress?.Invoke(0, literalLength);

                    // Read literal bytes
                    await _connection.ReadExactBytesAsync(destination, literalLength, cancellationToken).ConfigureAwait(false);
                    progress?.Invoke(literalLength, literalLength);

                    // Read the closing line containing ')'
                    string closingLine = await _connection.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    // Continue to tagged OK
                }
            }
        }
    }

    public async Task<(bool Exists, bool Selectable)> CheckFolderAsync(string folderName, CancellationToken cancellationToken)
    {
        var result = await SendCommandAsync($"LIST \"\" \"{EscapeString(folderName)}\"", cancellationToken).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return (false, false);
        }

        foreach (string line in result.UntaggedLines)
        {
            if (line.StartsWith("* LIST", StringComparison.OrdinalIgnoreCase))
            {
                bool noSelect = line.Contains("\\Noselect", StringComparison.OrdinalIgnoreCase);
                return (true, !noSelect);
            }
        }

        return (false, false);
    }

    public async Task AppendAsync(
        string folderName,
        byte[] messageBytes,
        bool seen,
        DateTimeOffset? date,
        CancellationToken cancellationToken)
    {
        string tag = NextTag();
        string flags = seen ? "(\\Seen) " : "";
        string command = $"APPEND \"{EscapeString(folderName)}\" {flags}{{{messageBytes.Length}}}";

        await _connection.WriteLineAsync($"{tag} {command}", cancellationToken).ConfigureAwait(false);

        // Expect continuation line starting with '+'
        string prompt = await _connection.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (!prompt.StartsWith('+'))
        {
            throw new ImapCommandException(tag, prompt);
        }

        // Send raw message bytes
        await _connection.WriteBytesAsync(messageBytes, cancellationToken).ConfigureAwait(false);
        await _connection.WriteLineAsync("", cancellationToken).ConfigureAwait(false); // terminating CRLF

        var result = await ReadTaggedResponseAsync(tag, cancellationToken).ConfigureAwait(false);
        if (!result.IsOk)
        {
            throw new ImapCommandException(tag, result.StatusText);
        }
    }

    public async Task DisconnectAsync(bool sendLogout, CancellationToken cancellationToken)
    {
        if (sendLogout && _connection.IsConnected)
        {
            try
            {
                await SendCommandAsync("LOGOUT", cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Disconnect errors during LOGOUT do not fail the session
            }
        }
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private string NextTag() => $"A{Interlocked.Increment(ref _tagCounter):D4}";

    private async Task<ImapCommandResult> SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        string tag = NextTag();
        await _connection.WriteLineAsync($"{tag} {command}", cancellationToken).ConfigureAwait(false);
        return await ReadTaggedResponseAsync(tag, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImapCommandResult> ReadTaggedResponseAsync(string tag, CancellationToken cancellationToken)
    {
        var untagged = new List<string>();
        while (true)
        {
            string line = await _connection.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line.StartsWith(tag, StringComparison.Ordinal))
            {
                string[] parts = line.Split(' ', 3);
                string status = parts.Length > 1 ? parts[1].ToUpperInvariant() : "";
                string statusText = parts.Length > 2 ? parts[2] : "";
                bool isOk = status == "OK";
                return new ImapCommandResult(tag, isOk, status, statusText, untagged);
            }

            untagged.Add(line);
        }
    }

    private static string EscapeString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}

public sealed record ImapCommandResult(string Tag, bool IsOk, string Status, string StatusText, List<string> UntaggedLines);
