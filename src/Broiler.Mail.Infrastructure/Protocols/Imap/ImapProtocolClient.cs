// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Protocols.Smtp;
using Broiler.Mail.Infrastructure.Protocols.Transport;

namespace Broiler.Mail.Infrastructure.Protocols.Imap;

internal sealed record ImapMoveReceipt(bool IsOk, uint? DestinationValidity, uint? DestinationUid);

internal enum ImapMutableFlag { Seen, Flagged, Deleted }

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

    public Task<ImapMailboxInfo> ExamineInboxAsync(CancellationToken cancellationToken) =>
        ExamineMailboxAsync("INBOX", cancellationToken);

    public async Task<ImapMailboxInfo> ExamineMailboxAsync(string mailboxId, CancellationToken cancellationToken, bool boundedSnapshot = false)
    {
        string command = $"EXAMINE \"{EscapeString(ImapMailboxName.Encode(mailboxId))}\"";
        var result = boundedSnapshot ? await SendSnapshotCommandAsync(command, 66, cancellationToken, allowValidity: true).ConfigureAwait(false)
            : await SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
        if (!result.IsOk)
        {
            throw new ImapCommandException(result.Tag, result.StatusText);
        }

        int count = 0;
        uint validity = 0;
        uint? nextUid = null;
        bool hasCount = false, hasValidity = false, hasNext = false;

        foreach (string line in result.UntaggedLines)
        {
            var existsMatch = Regex.Match(line, @"^\*\s+(\d+)\s+EXISTS", RegexOptions.IgnoreCase);
            if (existsMatch.Success)
            {
                if (boundedSnapshot && hasCount || !int.TryParse(existsMatch.Groups[1].Value, out count)) throw new ImapProtocolException("Invalid mailbox count.");
                hasCount = true;
                continue;
            }

            var validityMatch = Regex.Match(line, @"UIDVALIDITY\s+(\d+)", RegexOptions.IgnoreCase);
            if (validityMatch.Success)
            {
                if (boundedSnapshot && hasValidity || !uint.TryParse(validityMatch.Groups[1].Value, out validity)) throw new ImapProtocolException("Invalid mailbox validity.");
                hasValidity = true;
                continue;
            }

            var nextMatch = Regex.Match(line, @"UIDNEXT\s+(\d+)", RegexOptions.IgnoreCase);
            if (nextMatch.Success)
            {
                if (boundedSnapshot && hasNext || !uint.TryParse(nextMatch.Groups[1].Value, out uint parsedNext)) throw new ImapProtocolException("Invalid next UID.");
                nextUid = parsedNext; hasNext = true;
                continue;
            }
        }

        if (boundedSnapshot && (!hasCount || !hasValidity || validity == 0)) throw new ImapProtocolException("Incomplete mailbox identity.");
        return new ImapMailboxInfo(count, validity, nextUid);
    }

    private static string FlagToken(ImapMutableFlag flag) => flag switch
    { ImapMutableFlag.Seen => "\\Seen", ImapMutableFlag.Flagged => "\\Flagged", ImapMutableFlag.Deleted => "\\Deleted", _ => throw new ArgumentOutOfRangeException(nameof(flag)) };

    internal Task<(uint UidValidity, bool AllowsFlag)> SelectForFlagChangeAsync(string mailboxId, ImapMutableFlag flag, CancellationToken token) =>
        SelectForMutationAsync(mailboxId, flag, token);

    internal Task<(uint UidValidity, bool AllowsFlag)> SelectForMoveAsync(string mailboxId, CancellationToken token) =>
        SelectForMutationAsync(mailboxId, null, token);

    private async Task<(uint UidValidity, bool AllowsFlag)> SelectForMutationAsync(string mailboxId, ImapMutableFlag? flag, CancellationToken token)
    {
        var result = await SendFlagCommandAsync($"SELECT \"{EscapeString(ImapMailboxName.Encode(mailboxId))}\"", token).ConfigureAwait(false);
        if (!result.IsOk) throw new ImapCommandException(result.Tag, result.StatusText);
        uint validity = 0;
        bool allowed = result.StatusText.StartsWith("[READ-WRITE]", StringComparison.OrdinalIgnoreCase);
        foreach (string line in result.UntaggedLines)
        {
            var epoch = Regex.Match(line, @"\[UIDVALIDITY ([0-9]+)\]", RegexOptions.IgnoreCase);
            if (epoch.Success && (!uint.TryParse(epoch.Groups[1].Value, out validity) || validity == 0))
                throw new ImapProtocolException("Invalid UIDVALIDITY.");
            var permanent = Regex.Match(line, @"\[PERMANENTFLAGS \(([^)]*)\)\]", RegexOptions.IgnoreCase);
            if (flag is { } requiredFlag && permanent.Success && !permanent.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(FlagToken(requiredFlag), StringComparer.OrdinalIgnoreCase)) allowed = false;
            if (line.Contains("[READ-ONLY]", StringComparison.OrdinalIgnoreCase)) allowed = false;
        }
        if (validity == 0) throw new ImapProtocolException("Missing UIDVALIDITY.");
        return (validity, allowed);
    }

    internal async Task<bool?> GetFlagStateAsync(uint uid, ImapMutableFlag flag, CancellationToken token)
    {
        if (uid == 0) throw new ArgumentOutOfRangeException(nameof(uid));
        var result = await SendFlagCommandAsync($"UID FETCH {uid} (UID FLAGS)", token).ConfigureAwait(false);
        if (!result.IsOk) throw new ImapCommandException(result.Tag, result.StatusText);
        bool? state = null;
        foreach (string line in result.UntaggedLines)
        {
            var summary = ImapEnvelopeParser.ParseFetchLine(line);
            if (summary?.Uid != uid) continue;
            if (!Regex.IsMatch(line, @"\bFLAGS\s+\([^)]*\)", RegexOptions.IgnoreCase) || state is not null)
                throw new ImapProtocolException("Invalid flag response.");
            state = flag switch { ImapMutableFlag.Seen => summary.IsSeen, ImapMutableFlag.Flagged => summary.IsFlagged, ImapMutableFlag.Deleted => summary.IsDeleted, _ => throw new ArgumentOutOfRangeException(nameof(flag)) };
        }
        return state;
    }

    internal async Task StoreFlagStateAsync(uint uid, ImapMutableFlag flag, bool isSet, CancellationToken token)
    {
        if (uid == 0) throw new ArgumentOutOfRangeException(nameof(uid));
        var result = await SendFlagCommandAsync($"UID STORE {uid} {(isSet ? "+" : "-")}FLAGS.SILENT ({FlagToken(flag)})", token).ConfigureAwait(false);
        if (!result.IsOk) throw new ImapCommandException(result.Tag, result.StatusText);
    }

    internal async Task RefreshMutationCapabilitiesAsync(CancellationToken token)
    {
        var result = await SendFlagCommandAsync("CAPABILITY", token).ConfigureAwait(false);
        if (!result.IsOk) throw new ImapCommandException(result.Tag, result.StatusText);
        _capabilities.Clear();
        foreach (string line in result.UntaggedLines)
            if (line.StartsWith("* CAPABILITY ", StringComparison.OrdinalIgnoreCase))
                _capabilities.AddRange(line[13..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }

    internal async Task ExpungeUidAsync(uint uid, CancellationToken token)
    {
        if (uid == 0) throw new ArgumentOutOfRangeException(nameof(uid));
        var result = await SendFlagCommandAsync($"UID EXPUNGE {uid}", token).ConfigureAwait(false);
        if (!result.IsOk) throw new ImapCommandException(result.Tag, result.StatusText);
    }

    internal async Task<ImapMoveReceipt> MoveUidAsync(uint uid, string destinationMailboxId, CancellationToken token)
    {
        if (uid == 0) throw new ArgumentOutOfRangeException(nameof(uid));
        var result = await SendFlagCommandAsync($"UID MOVE {uid} \"{EscapeString(ImapMailboxName.Encode(destinationMailboxId))}\"", token).ConfigureAwait(false);
        uint? validity = null, destination = null;
        foreach (string text in result.UntaggedLines.Append(result.StatusText))
        {
            if (!text.Contains("[COPYUID", StringComparison.OrdinalIgnoreCase)) continue;
            var receipt = Regex.Match(text, @"\[COPYUID ([0-9]+) ([0-9]+(?::[0-9]+)?) ([0-9]+(?::[0-9]+)?)\]", RegexOptions.IgnoreCase);
            if (!receipt.Success || !uint.TryParse(receipt.Groups[1].Value, out uint epoch) || epoch == 0 ||
                !TrySingleUid(receipt.Groups[2].Value, out uint original) || original != uid ||
                !TrySingleUid(receipt.Groups[3].Value, out uint moved) ||
                validity is not null && (validity != epoch || destination != moved))
                throw new ImapProtocolException("Invalid single-message move receipt.");
            validity = epoch; destination = moved;
        }
        return new(result.IsOk, validity, destination);
    }

    private static bool TrySingleUid(string value, out uint uid)
    {
        string[] parts = value.Split(':');
        uid = 0;
        return parts.Length is 1 or 2 && uint.TryParse(parts[0], out uid) && uid > 0 &&
            (parts.Length == 1 || uint.TryParse(parts[1], out uint last) && last == uid);
    }

    public async Task<List<ImapMessageSummaryInfo>> FetchSummariesAsync(int start, int end, CancellationToken cancellationToken, bool boundedSnapshot = false)
    {
        string command = $"FETCH {start}:{end} (UID ENVELOPE INTERNALDATE FLAGS)";
        var result = boundedSnapshot ? await SendSnapshotCommandAsync(command, end - start + 34, cancellationToken).ConfigureAwait(false)
            : await SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
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
                if (boundedSnapshot && !Regex.IsMatch(line, @"\bFLAGS\s*\(", RegexOptions.IgnoreCase)) throw new ImapProtocolException("Snapshot header omitted flags.");
                list.Add(summary);
            }
        }

        return list;
    }

    internal async Task<uint[]> GetSnapshotUidsAsync(int maximum, CancellationToken token)
    {
        var response = await SendSnapshotCommandAsync("UID SEARCH ALL", 34, token).ConfigureAwait(false);
        if (!response.IsOk) throw new ImapCommandException(response.Tag, response.StatusText);
        uint[]? uids = null;
        foreach (string line in response.UntaggedLines)
        {
            if (line.Equals("* SEARCH", StringComparison.OrdinalIgnoreCase) || line.StartsWith("* SEARCH ", StringComparison.OrdinalIgnoreCase))
            {
                if (uids is not null) throw new ImapProtocolException("Repeated UID census response.");
                string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length - 2 > maximum) throw new ImapProtocolException("UID census exceeds snapshot limit.");
                uids = new uint[fields.Length - 2];
                for (int i = 2; i < fields.Length; i++)
                    if (!uint.TryParse(fields[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out uids[i - 2]) || uids[i - 2] == 0)
                        throw new ImapProtocolException("Invalid UID census.");
                Array.Sort(uids);
                if (uids.Distinct().Count() != uids.Length) throw new ImapProtocolException("Repeated UID in census.");
            }
            if (line.StartsWith("* ESEARCH", StringComparison.OrdinalIgnoreCase)) throw new ImapProtocolException("Unsupported extended census response.");
        }
        return uids ?? throw new ImapProtocolException("Missing UID census response.");
    }

    private async Task<ImapCommandResult> SendSnapshotCommandAsync(string command, int maximumLines, CancellationToken token, bool allowValidity = false)
    {
        string tag = NextTag(); await _connection.WriteLineAsync(tag + " " + command, token).ConfigureAwait(false);
        var lines = new List<string>(); long characters = 0;
        for (int index = 0; index < maximumLines; index++)
        {
            string line = await _connection.ReadLineAsync(token, 128 * 1024).ConfigureAwait(false); characters += line.Length;
            if (characters > 2 * 1024 * 1024 || !allowValidity && line.Contains("[UIDVALIDITY", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(line, @"^\*\s+\d+\s+EXPUNGE\b", RegexOptions.IgnoreCase))
                throw new ImapProtocolException("Snapshot changed or exceeded response bounds.");
            if (line.StartsWith(tag + " ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', 3); string status = parts.Length > 1 ? parts[1].ToUpperInvariant() : "";
                return new(tag, status == "OK", status, parts.Length > 2 ? parts[2] : "", lines);
            }
            if (!line.StartsWith("* ", StringComparison.Ordinal)) throw new ImapProtocolException("Unexpected snapshot response.");
            lines.Add(line);
        }
        throw new ImapProtocolException("Snapshot response exceeded its line limit.");
    }

    public async Task<long> GetMessageSizeAsync(uint uid, CancellationToken token)
    {
        if (uid == 0) throw new ArgumentOutOfRangeException(nameof(uid));
        string tag = NextTag(); await _connection.WriteLineAsync($"{tag} UID FETCH {uid} (RFC822.SIZE)", token).ConfigureAwait(false);
        long? size = null;
        for (int lines = 0; lines < 1024; lines++)
        {
            string line = await _connection.ReadLineAsync(token).ConfigureAwait(false);
            if (line.StartsWith(tag + " ", StringComparison.Ordinal))
            {
                var fields = line.Split(' ', 3);
                if (fields.Length < 2 || !fields[1].Equals("OK", StringComparison.OrdinalIgnoreCase)) throw new ImapCommandException(tag, line);
                return size ?? throw new ImapMessageNotFoundException($"Message with UID {uid} was not found on the server.");
            }
            if (line.Contains("RFC822.SIZE", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(line, @"^\* [1-9][0-9]* FETCH \((?:UID ([1-9][0-9]*) RFC822\.SIZE ([0-9]+)|RFC822\.SIZE ([0-9]+) UID ([1-9][0-9]*))\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (size is not null || !match.Success || !uint.TryParse(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[4].Value, out uint returned) || returned != uid ||
                    !long.TryParse(match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value, out long value))
                    throw new ImapProtocolException("Unexpected message size response.");
                size = value;
            }
            else if (!line.StartsWith("* ", StringComparison.Ordinal) || line.StartsWith("* BYE", StringComparison.OrdinalIgnoreCase)) throw new ImapProtocolException("Unexpected message size response.");
        }
        throw new ImapProtocolException("Too many message size responses.");
    }

    public async Task<int> CopyBodyAsync(uint uid, int offset, int maximumBytes, Stream destination, CancellationToken cancellationToken,
        Action<long, long>? progress = null, int? expectedLength = null)
    {
        if (uid == 0 || offset < 0 || maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(uid));
        string tag = NextTag();
        await _connection.WriteLineAsync($"{tag} UID FETCH {uid} (BODY.PEEK[]<{offset}.{maximumBytes}>)", cancellationToken).ConfigureAwait(false);
        int? received = null;
        for (int lines = 0; lines < 1024; lines++)
        {
            string line = await _connection.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line.StartsWith(tag + " ", StringComparison.Ordinal))
            {
                string[] parts = line.Split(' ', 3);
                if (parts.Length < 2 || !parts[1].Equals("OK", StringComparison.OrdinalIgnoreCase)) throw new ImapCommandException(tag, line);
                return received ?? throw new ImapMessageNotFoundException($"Message with UID {uid} was not found on the server.");
            }
            if (line.Contains("BODY", StringComparison.OrdinalIgnoreCase) || line.EndsWith('}'))
            {
                var match = Regex.Match(line, @"^\* [1-9][0-9]* FETCH \((?:UID ([1-9][0-9]*) )?BODY\[\]<([0-9]+)> \{([0-9]+)\}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (received is not null || !match.Success || !int.TryParse(match.Groups[2].Value, out int returnedOffset) || returnedOffset != offset ||
                    !int.TryParse(match.Groups[3].Value, out int length) || length > maximumBytes || expectedLength is { } expected && length != expected)
                    throw new ImapProtocolException("Unexpected or oversized body response.");
                uint? responseUid = null;
                if (match.Groups[1].Success)
                {
                    if (!uint.TryParse(match.Groups[1].Value, out uint value) || value != uid) throw new ImapProtocolException("Body response UID mismatch.");
                    responseUid = value;
                }
                progress?.Invoke(0, length);
                await _connection.ReadExactBytesAsync(destination, length, cancellationToken,
                    progress is null ? null : transferred => progress(transferred, length)).ConfigureAwait(false);
                string closing = await _connection.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                var tail = Regex.Match(closing, @"^(?: UID ([1-9][0-9]*))?\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!tail.Success) throw new ImapProtocolException("Unexpected body response ending.");
                if (tail.Groups[1].Success)
                {
                    if (responseUid is not null || !uint.TryParse(tail.Groups[1].Value, out uint value) || value != uid) throw new ImapProtocolException("Body response UID mismatch.");
                    responseUid = value;
                }
                if (responseUid != uid) throw new ImapProtocolException("Body response did not identify the requested UID.");
                received = length;
            }
            else if (!line.StartsWith("* ", StringComparison.Ordinal) || line.StartsWith("* BYE", StringComparison.OrdinalIgnoreCase))
                throw new ImapProtocolException("Unexpected body response.");
        }
        throw new ImapProtocolException("Too many body response lines.");
    }

    public async Task<IReadOnlyList<MailFolder>> ListFoldersAsync(CancellationToken cancellationToken)
    {
        const int maximumFolders = 512;
        const int maximumBytes = 1024 * 1024;
        // Capabilities may change after authentication, especially for special-use metadata.
        await QueryCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        string tag = NextTag();
        string returns = _capabilities.Contains("SPECIAL-USE", StringComparer.OrdinalIgnoreCase) ? " RETURN (SPECIAL-USE)" : "";
        await _connection.WriteLineAsync($"{tag} LIST \"\" \"*\"{returns}", cancellationToken).ConfigureAwait(false);
        var folders = new List<MailFolder>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        int bytes = 0;
        for (int response = 0; response < 1024; response++)
        {
            string line = await _connection.ReadLineAsync(cancellationToken, 8192).ConfigureAwait(false);
            bytes += line.Length + 2;
            if (bytes > maximumBytes) throw new ImapProtocolException("Folder discovery exceeds the reading limit.");
            if (line.StartsWith(tag + " ", StringComparison.Ordinal))
            {
                string[] parts = line.Split(' ', 3);
                if (!string.Equals(parts[1], "OK", StringComparison.OrdinalIgnoreCase))
                    throw new ImapCommandException(tag, "Folder listing rejected.");
                return folders.AsReadOnly();
            }
            if (!line.StartsWith("* LIST ", StringComparison.OrdinalIgnoreCase))
            {
                if (!line.StartsWith("* ", StringComparison.Ordinal) || line.StartsWith("* BYE", StringComparison.OrdinalIgnoreCase))
                    throw new ImapProtocolException("Unexpected folder discovery response.");
                continue;
            }
            if (folders.Count >= maximumFolders) throw new ImapProtocolException("Too many server folders.");
            string? literal = null;
            var marker = Regex.Match(line, @" \{([0-9]+)\}$", RegexOptions.CultureInvariant);
            if (marker.Success)
            {
                if (!int.TryParse(marker.Groups[1].Value, out int length) || length > 4096 || bytes + length > maximumBytes)
                    throw new ImapProtocolException("Mailbox literal exceeds the reading limit.");
                using var data = new MemoryStream(length);
                await _connection.ReadExactBytesAsync(data, length, cancellationToken).ConfigureAwait(false);
                literal = Encoding.Latin1.GetString(data.GetBuffer(), 0, length);
                string tail = await _connection.ReadLineAsync(cancellationToken, 8192).ConfigureAwait(false);
                if (tail.Length != 0) throw new ImapProtocolException("Unexpected LIST extension.");
                bytes += length + 2;
            }
            var folder = ImapFolderParser.Parse(line, literal);
            if (!identities.Add(folder.MailboxId)) throw new ImapProtocolException("Duplicate mailbox identity.");
            folders.Add(folder);
        }
        throw new ImapProtocolException("Too many folder discovery responses.");
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
        IEnumerable<ReadOnlyMemory<byte>> messageChunks,
        int messageLength,
        bool seen,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(messageLength);
        string tag = NextTag();
        string flags = seen ? "(\\Seen) " : "";
        string command = $"APPEND \"{EscapeString(folderName)}\" {flags}{{{messageLength}}}";

        await _connection.WriteLineAsync($"{tag} {command}", cancellationToken).ConfigureAwait(false);

        // Expect continuation line starting with '+'
        string prompt = await _connection.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (!prompt.StartsWith('+'))
        {
            throw new ImapCommandException(tag, prompt);
        }

        // A literal's exact octet count must match the announced size. Never complete a mismatched command.
        int remaining = messageLength;
        foreach (var chunk in messageChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (chunk.Length > remaining) throw new ImapProtocolException("Sent copy exceeded its announced size.");
            if (!chunk.IsEmpty) await _connection.WriteBytesAsync(chunk, cancellationToken).ConfigureAwait(false);
            remaining -= chunk.Length;
        }
        if (remaining != 0) throw new ImapProtocolException("Sent copy ended before its announced size.");
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

    private async Task<ImapCommandResult> SendFlagCommandAsync(string command, CancellationToken token)
    {
        string tag = NextTag();
        await _connection.WriteLineAsync($"{tag} {command}", token).ConfigureAwait(false);
        var lines = new List<string>(); int bytes = 0;
        for (int response = 0; response < 128; response++)
        {
            string line = await _connection.ReadLineAsync(token, 8192).ConfigureAwait(false);
            bytes += line.Length + 2;
            if (bytes > 256 * 1024) throw new ImapProtocolException("Flag response exceeds the reading limit.");
            if (line.StartsWith(tag + " ", StringComparison.Ordinal))
            {
                string[] parts = line.Split(' ', 3);
                string status = parts[1].ToUpperInvariant();
                return new(tag, status == "OK", status, parts.Length > 2 ? parts[2] : "", lines);
            }
            if (!line.StartsWith("* ", StringComparison.Ordinal) || line.StartsWith("* BYE", StringComparison.OrdinalIgnoreCase))
                throw new ImapProtocolException("Unexpected flag response.");
            lines.Add(line);
        }
        throw new ImapProtocolException("Too many flag responses.");
    }

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
