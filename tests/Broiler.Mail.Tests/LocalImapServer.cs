using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Tests;

/// <summary>Minimal loopback IMAP fixture. Never records authentication payloads or uses real accounts.</summary>
internal sealed class LocalImapServer : IAsyncDisposable
{
    public const string Password = "fixture-only-password";
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(15));
    private readonly TransportSecurity _security;
    private readonly bool _rejectAuthentication;
    private readonly bool _advertiseStartTls;
    private readonly bool _silent;
    private readonly Task _session;

    public LocalImapServer(TransportSecurity security, bool rejectAuthentication = false, bool advertiseStartTls = true, bool silent = false)
    {
        _security = security;
        _rejectAuthentication = rejectAuthentication;
        _advertiseStartTls = advertiseStartTls;
        _silent = silent;
        Certificate = CreateCertificate();
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _session = RunAsync();
    }

    public X509Certificate2 Certificate { get; }
    public int Port { get; }
    public bool Authenticated { get; private set; }
    public bool AuthenticatedOverTls { get; private set; }
    public ConcurrentQueue<string> Commands { get; } = new();
    public ConcurrentQueue<string> MailCommands { get; } = new();
    public List<FixtureMessage> Messages { get; } = [];
    public uint UidValidity { get; set; } = 7;
    public int SnapshotCensusCount { get; private set; }
    public Func<int, string?>? SnapshotCensusResponse { get; set; }
    public Action<int>? BeforeSnapshotCensus { get; set; }
    public Action<int, int>? BeforeSnapshotHeaderFetch { get; set; }
    public bool OmitSnapshotFlags { get; set; }
    public string? HeaderResponseOverride { get; set; }
    public long? SourceReportedSize { get; set; }
    public byte[]? SourceOctets { get; set; }
    public uint? BodyResponseUid { get; set; }
    public int BodyResponseOffset { get; set; }
    public int? DeclaredBodyLiteralLength { get; set; }
    public bool BodyUidAfterLiteral { get; set; }
    public bool OmitBodyUid { get; set; }
    public bool DuplicateBodyLiteral { get; set; }
    public bool InvalidBodyEnding { get; set; }
    public bool DisconnectOnFetch { get; set; }
    public bool MalformedFetch { get; set; }
    public bool StallOnFetch { get; set; }
    public string? FolderListResponse { get; set; }
    public bool AdvertiseSpecialUse { get; set; }
    public bool RejectFolderList { get; set; }
    public bool MissingSentFolder { get; set; }
    private string? _movedMailboxWireName;
    public bool AdvertiseMove { get; set; }
    public string MoveDestinationWireName { get; set; } = "Archive";
    public uint MoveDestinationValidity { get; set; } = 9;
    public uint MoveDestinationUid { get; set; } = 900;
    public List<FixtureMessage> MovedMessages { get; } = [];
    public int MoveCount { get; private set; }
    public bool RejectMove { get; set; }
    public bool DropAfterMove { get; set; }
    public bool IgnoreMove { get; set; }
    public bool KeepSourceAfterMove { get; set; }
    public bool TaggedMoveReceipt { get; set; }
    public bool MissingMoveReceipt { get; set; }
    public bool MissingMoveDestination { get; set; }
    public string? MoveReceiptOverride { get; set; }
    public bool ReadOnlySelection { get; set; }
    public bool AdvertiseUidPlus { get; set; }
    public bool AllowDeletedChanges { get; set; } = true;
    public bool RejectUidExpunge { get; set; }
    public bool DropAfterUidExpunge { get; set; }
    public bool IgnoreUidExpunge { get; set; }
    public bool DropAfterDeleteMark { get; set; }
    public int UidExpungeCount { get; private set; }
    public int DeleteMarkCount { get; private set; }
    public bool AllowSeenChanges { get; set; } = true;
    public bool AllowStarChanges { get; set; } = true;
    public bool RejectReadStateStore { get; set; }
    public bool DropAfterReadStateStore { get; set; }
    public bool IgnoreReadStateStore { get; set; }
    public bool RemoveAfterReadStateStore { get; set; }
    public int ReadStateStoreCount { get; private set; }
    public bool RejectAppend { get; set; }
    public bool DropAfterAppend { get; set; }
    public bool StallAfterAppend { get; set; }
    public int AppendCount { get; private set; }
    public string? AppendedMessage { get; private set; }
    public TaskCompletionSource AppendReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private string Capabilities(bool encrypted) => "IMAP4rev1 AUTH=PLAIN SASL-IR" + (AdvertiseUidPlus && Authenticated ? " UIDPLUS" : "") + (AdvertiseMove && Authenticated ? " MOVE" : "") + (AdvertiseSpecialUse && Authenticated ? " SPECIAL-USE LIST-EXTENDED" : "") + (!encrypted && _advertiseStartTls ? " STARTTLS" : "");

    private async Task RunAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_lifetime.Token);
                await RunClientAsync(client);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private async Task RunClientAsync(TcpClient client)
    {
        Stream transport = client.GetStream();
        bool encrypted = _security == TransportSecurity.Tls;
        if (encrypted) transport = await UpgradeAsync(transport);
        try
        {
            if (_silent)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, _lifetime.Token);
                return;
            }
            bool selectedDestination = false;
            using var initialWriter = Writer(transport);
            await initialWriter.WriteLineAsync($"* OK [CAPABILITY {Capabilities(encrypted)}] Fixture ready");
            while (true)
            {
                // Latin-1 preserves each literal octet as one char, including 8-bit MIME from APPEND.
                using var reader = new StreamReader(transport, Encoding.Latin1, false, 1024, leaveOpen: true);
                using var writer = Writer(transport);
                bool upgrade = false;
                string? line;
                while ((line = await reader.ReadLineAsync(_lifetime.Token)) is not null)
                {
                    string[] parts = line.Split(' ', 4);
                    string tag = parts[0];
                    string command = parts[1].ToUpperInvariant();
                    Commands.Enqueue(command);
                    switch (command)
                    {
                        case "CAPABILITY":
                            await writer.WriteLineAsync($"* CAPABILITY {Capabilities(encrypted)}\r\n{tag} OK Capabilities");
                            break;
                        case "STARTTLS":
                            if (encrypted || !_advertiseStartTls) throw new InvalidOperationException("Unexpected TLS upgrade.");
                            await writer.WriteLineAsync($"{tag} OK Begin TLS");
                            upgrade = true;
                            break;
                        case "AUTHENTICATE":
                            string encoded;
                            if (parts.Length == 4) encoded = parts[3];
                            else
                            {
                                await writer.WriteLineAsync("+ ");
                                encoded = (await reader.ReadLineAsync(_lifetime.Token))!;
                            }
                            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                            bool valid = decoded == "\0test\0" + Password;
                            if (!_rejectAuthentication && valid)
                            {
                                Authenticated = true;
                                AuthenticatedOverTls = encrypted;
                                await writer.WriteLineAsync($"{tag} OK [CAPABILITY IMAP4rev1] Authenticated");
                            }
                            else
                            {
                                // Ensure the application never echoes server diagnostics, even if they include credentials.
                                await writer.WriteLineAsync($"{tag} NO [AUTHENTICATIONFAILED] Rejected {Password}");
                            }
                            break;
                        case "LIST":
                            MailCommands.Enqueue(line[(tag.Length + 1)..]);
                            if (line.Contains("LIST \"\" \"*\"", StringComparison.Ordinal))
                            {
                                await writer.WriteLineAsync(RejectFolderList ? $"{tag} NO Rejected" :
                                    (FolderListResponse == "" ? "" : (FolderListResponse ?? "* LIST () \"/\" \"INBOX\"") + "\r\n") + $"{tag} OK List");
                                break;
                            }
                            if (line.Contains("Sent", StringComparison.Ordinal))
                                await writer.WriteLineAsync(MissingSentFolder ? $"{tag} OK No matching folder" : $"* LIST (\\HasNoChildren) \"/\" \"Sent\"\r\n{tag} OK List");
                            else if (line.EndsWith("\"\" \"\"", StringComparison.Ordinal))
                                await writer.WriteLineAsync($"* LIST (\\Noselect) \"/\" \"\"\r\n{tag} OK List");
                            else
                                await writer.WriteLineAsync($"* LIST (\\HasNoChildren) \"/\" \"INBOX\"\r\n{tag} OK List");
                            break;
                        case "APPEND":
                            if (!AuthenticatedOverTls) throw new InvalidOperationException("APPEND must authenticate over TLS.");
                            AppendCount++;
                            MailCommands.Enqueue(line[(tag.Length + 1)..]);
                            if (RejectAppend) { await writer.WriteLineAsync($"{tag} NO Permission denied {Password}"); break; }
                            var literal = Regex.Match(line, @"\{(\d+)\}$");
                            if (!literal.Success) throw new InvalidOperationException("Expected synchronizing APPEND literal.");
                            int size = int.Parse(literal.Groups[1].Value);
                            if (size > 2 * 1024 * 1024) throw new InvalidOperationException("Fixture literal too large.");
                            await writer.WriteLineAsync("+ Send literal");
                            var buffer = new char[size];
                            int offset = 0;
                            while (offset < size)
                            {
                                int read = await reader.ReadAsync(buffer.AsMemory(offset), _lifetime.Token);
                                if (read == 0) throw new IOException("Incomplete APPEND literal.");
                                offset += read;
                            }
                            if (await reader.ReadLineAsync(_lifetime.Token) != "") throw new InvalidOperationException("Unexpected APPEND terminator.");
                            AppendedMessage = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(buffer));
                            AppendReceived.TrySetResult();
                            if (DropAfterAppend) return;
                            if (StallAfterAppend) { await Task.Delay(Timeout.InfiniteTimeSpan, _lifetime.Token); return; }
                            await writer.WriteLineAsync($"{tag} OK Appended");
                            break;
                        case "LOGIN" when _rejectAuthentication:
                            await writer.WriteLineAsync($"{tag} NO [AUTHENTICATIONFAILED] Rejected {Password}");
                            break;
                        case "SELECT":
                            selectedDestination = AdvertiseMove && line.EndsWith("\"" + (_movedMailboxWireName ?? MoveDestinationWireName) + "\"", StringComparison.Ordinal);
                            MailCommands.Enqueue(line[(tag.Length + 1)..]);
                            await writer.WriteLineAsync($"* FLAGS (\\Seen \\Flagged)\r\n* {(selectedDestination ? MovedMessages.Count : Messages.Count)} EXISTS\r\n* OK [UIDVALIDITY {(selectedDestination ? MoveDestinationValidity : UidValidity)}] Validity\r\n* OK [PERMANENTFLAGS ({(AllowSeenChanges ? "\\Seen " : "")}{(AllowStarChanges ? "\\Flagged " : "")}{(AllowDeletedChanges ? "\\Deleted" : "")})] Flags\r\n{tag} OK [{(ReadOnlySelection ? "READ-ONLY" : "READ-WRITE")}] Open");
                            break;
                        case "EXAMINE":
                            selectedDestination = AdvertiseMove && line.EndsWith("\"" + (_movedMailboxWireName ?? MoveDestinationWireName) + "\"", StringComparison.Ordinal);
                            MailCommands.Enqueue(line[(tag.Length + 1)..]);
                            var examined = selectedDestination ? MovedMessages : Messages;
                            uint next = examined.Count == 0 ? 1 : examined.Max(message => message.Uid) + 1;
                            await writer.WriteLineAsync($"* FLAGS (\\Seen)\r\n* {examined.Count} EXISTS\r\n* 0 RECENT\r\n* OK [UIDVALIDITY {(selectedDestination ? MoveDestinationValidity : UidValidity)}] Validity\r\n* OK [UIDNEXT {next}] Next\r\n{tag} OK [READ-ONLY] Open");
                            break;
                        case "FETCH":
                        case "UID":
                            MailCommands.Enqueue(line[(tag.Length + 1)..]);
                            if (command == "UID" && line.EndsWith("UID SEARCH ALL", StringComparison.Ordinal))
                            {
                                int census = ++SnapshotCensusCount; BeforeSnapshotCensus?.Invoke(census);
                                string response = SnapshotCensusResponse?.Invoke(census) ?? "* SEARCH" + string.Concat((selectedDestination ? MovedMessages : Messages).Select(row => " " + row.Uid));
                                await writer.WriteLineAsync(response + $"\r\n{tag} OK Search"); break;
                            }
                            if (command == "UID" && line.Contains("UID EXPUNGE ", StringComparison.Ordinal))
                            {
                                if (!AdvertiseUidPlus || !AuthenticatedOverTls) throw new InvalidOperationException("Unexpected UID EXPUNGE context.");
                                var expunge = Regex.Match(line, @"UID EXPUNGE ([0-9]+)$");
                                if (!expunge.Success) throw new InvalidOperationException("A single UID is required.");
                                UidExpungeCount++;
                                if (RejectUidExpunge) { await writer.WriteLineAsync($"{tag} NO Denied"); break; }
                                uint uid = uint.Parse(expunge.Groups[1].Value);
                                if (!IgnoreUidExpunge) (selectedDestination ? MovedMessages : Messages).RemoveAll(message => message.Uid == uid && message.IsDeleted);
                                if (DropAfterUidExpunge) return;
                                await writer.WriteLineAsync($"{tag} OK Expunged"); break;
                            }
                            if (command == "UID" && line.Contains("UID MOVE ", StringComparison.Ordinal))
                            {
                                if (!AdvertiseMove || !AuthenticatedOverTls) throw new InvalidOperationException("Unexpected move context.");
                                MoveCount++;
                                var move = Regex.Match(line, @"UID MOVE ([0-9]+) ""([^""]+)""$");
                                if (!move.Success || move.Groups[2].Value != MoveDestinationWireName) throw new InvalidOperationException("Unexpected move command.");
                                uint original = uint.Parse(move.Groups[1].Value);
                                if (RejectMove) { await writer.WriteLineAsync($"{tag} NO Rejected"); break; }
                                if (!selectedDestination) _movedMailboxWireName ??= MoveDestinationWireName;
                                var moveFrom = selectedDestination ? MovedMessages : Messages;
                                var moveTo = selectedDestination ? Messages : MovedMessages;
                                int target = moveFrom.FindIndex(message => message.Uid == original);
                                if (target >= 0 && !IgnoreMove)
                                {
                                    if (!MissingMoveDestination) moveTo.Add(moveFrom[target] with { Uid = MoveDestinationUid });
                                    if (!KeepSourceAfterMove) moveFrom.RemoveAt(target);
                                }
                                if (DropAfterMove) return;
                                string mapping = "[COPYUID " + (MoveReceiptOverride ?? $"{(selectedDestination ? UidValidity : MoveDestinationValidity)} {original} {MoveDestinationUid}") + "]";
                                string receipt = MissingMoveReceipt || TaggedMoveReceipt ? "" : "* OK " + mapping + " Moved\r\n";
                                await writer.WriteLineAsync(receipt + $"{tag} OK " + (TaggedMoveReceipt && !MissingMoveReceipt ? mapping + " " : "") + "Moved"); break;
                            }
                            if (command == "UID" && line.Contains("UID STORE ", StringComparison.Ordinal))
                            {
                                ReadStateStoreCount++;
                                var store = Regex.Match(line, @"UID STORE ([0-9]+) ([+-])FLAGS\.SILENT \(\\(Seen|Flagged|Deleted)\)$");
                                if (!store.Success) throw new InvalidOperationException("Unexpected state mutation.");
                                if (RejectReadStateStore) { await writer.WriteLineAsync($"{tag} NO Denied"); break; }
                                int target = Messages.FindIndex(message => message.Uid == uint.Parse(store.Groups[1].Value));
                                if (store.Groups[3].Value == "Deleted") DeleteMarkCount++;
                                if (target >= 0 && !IgnoreReadStateStore) Messages[target] = store.Groups[3].Value switch
                                {
                                    "Seen" => Messages[target] with { IsRead = store.Groups[2].Value == "+" },
                                    "Flagged" => Messages[target] with { IsStarred = store.Groups[2].Value == "+" },
                                    "Deleted" => Messages[target] with { IsDeleted = store.Groups[2].Value == "+" },
                                    _ => throw new InvalidOperationException("Unsupported flag")
                                };
                                if (store.Groups[3].Value == "Deleted" && DropAfterDeleteMark) return;
                                if (target >= 0 && RemoveAfterReadStateStore) Messages.RemoveAt(target);
                                if (DropAfterReadStateStore) return;
                                await writer.WriteLineAsync($"{tag} OK Stored"); break;
                            }
                            if (command == "UID" && line.EndsWith("(UID FLAGS)", StringComparison.Ordinal))
                            {
                                var uid = uint.Parse(Regex.Match(line, @"UID FETCH ([0-9]+)").Groups[1].Value);
                                var selectedMessages = selectedDestination ? MovedMessages : Messages;
                                int target = selectedMessages.FindIndex(message => message.Uid == uid);
                                if (target >= 0) await writer.WriteLineAsync($"* {target + 1} FETCH (UID {uid} FLAGS ({(selectedMessages[target].IsRead ? "\\Seen " : "")}{(selectedMessages[target].IsStarred ? "\\Flagged " : "")}{(selectedMessages[target].IsDeleted ? "\\Deleted " : "")}custom))");
                                await writer.WriteLineAsync($"{tag} OK Flags"); break;
                            }
                            if (DisconnectOnFetch) return;
                            if (StallOnFetch) { await Task.Delay(Timeout.InfiniteTimeSpan, _lifetime.Token); return; }
                            if (MalformedFetch) { await writer.WriteLineAsync("* NOT-AN-IMAP-RESPONSE"); return; }
                            await FetchAsync(writer, tag, line, command == "UID");
                            break;
                        case "LOGOUT":
                            await writer.WriteLineAsync($"* BYE Closing\r\n{tag} OK Logout");
                            return;
                        default: throw new InvalidOperationException($"Unexpected fixture command: {command}");
                    }
                    if (upgrade) break;
                }
                if (!upgrade) return;
                transport = await UpgradeAsync(transport);
                encrypted = true;
            }
        }
        finally { await transport.DisposeAsync(); }
    }

    private async Task FetchAsync(StreamWriter writer, string tag, string line, bool byUid)
    {
        if (byUid)
        {
            var sizeRequest = Regex.Match(line, @"UID FETCH ([0-9]+) \(RFC822\.SIZE\)");
            if (sizeRequest.Success)
            {
                uint sizeUid = uint.Parse(sizeRequest.Groups[1].Value); int indexForSize = Messages.FindIndex(row => row.Uid == sizeUid);
                if (indexForSize >= 0) await writer.WriteLineAsync($"* {indexForSize + 1} FETCH (UID {sizeUid} RFC822.SIZE {SourceReportedSize ?? SourceOctets?.Length ?? Encoding.ASCII.GetByteCount(Messages[indexForSize].Raw)})");
                await writer.WriteLineAsync($"{tag} OK Size"); return;
            }
            var match = Regex.Match(line, @"UID FETCH (\d+) .*BODY\.PEEK\[\]<0\.(\d+)>");
            if (!match.Success) throw new InvalidOperationException("Body requests must be bounded and use PEEK.");
            uint uid = uint.Parse(match.Groups[1].Value);
            int index = Messages.FindIndex(message => message.Uid == uid);
            if (index >= 0)
            {
                byte[] body = SourceOctets ?? Encoding.ASCII.GetBytes(Messages[index].Raw);
                body = body[..Math.Min(body.Length, int.Parse(match.Groups[2].Value))];
                uint responseUid = BodyResponseUid ?? uid;
                string uidPrefix = BodyUidAfterLiteral || OmitBodyUid ? "" : $"UID {responseUid} ";
                string ending = InvalidBodyEnding ? "BROKEN)" : BodyUidAfterLiteral && !OmitBodyUid ? $" UID {responseUid})" : ")";
                async Task WriteBody()
                {
                    await writer.WriteLineAsync($"* {index + 1} FETCH ({uidPrefix}BODY[]<{BodyResponseOffset}> {{{DeclaredBodyLiteralLength ?? body.Length}}}");
                    await writer.FlushAsync(); await writer.BaseStream.WriteAsync(body);
                    await writer.WriteLineAsync(ending);
                }
                await WriteBody(); if (DuplicateBodyLiteral) await WriteBody();
            }
        }
        else
        {
            if (HeaderResponseOverride is {} response) { await writer.WriteLineAsync(response + $"\r\n{tag} OK Fetch"); return; }
            var match = Regex.Match(line, @"FETCH (\d+)(?::(\d+))? ");
            if (!match.Success) throw new InvalidOperationException("Expected a bounded sequence range.");
            int start = int.Parse(match.Groups[1].Value);
            int end = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : start;
            BeforeSnapshotHeaderFetch?.Invoke(start, end);
            for (int sequence = start; sequence <= end; sequence++)
            {
                var message = Messages[sequence - 1];
                string flags = (message.IsRead ? "\\Seen " : "") + (message.IsStarred ? "\\Flagged" : "");
                string address = "((\"Sender\" NIL \"sender\" \"example.test\"))";
                string flagField = OmitSnapshotFlags ? "" : $"FLAGS ({flags}) ";
                await writer.WriteLineAsync($"* {sequence} FETCH (UID {message.Uid} {flagField}INTERNALDATE \"28-Sep-2026 08:00:00 +0000\" ENVELOPE (\"Mon, 28 Sep 2026 08:00:00 +0000\" \"=?utf-8?b?R3LDvMOfZQ==?=\" {address} {address} {address} NIL NIL NIL NIL \"<fixture@example.test>\"))");
            }
        }
        await writer.WriteLineAsync($"{tag} OK Fetch");
    }

    internal sealed record FixtureMessage(uint Uid, string Raw, bool IsRead = false, bool IsStarred = false, bool IsDeleted = false);

    private async Task<SslStream> UpgradeAsync(Stream stream)
    {
        var tls = new SslStream(stream, leaveInnerStreamOpen: false);
        try
        {
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = Certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, _lifetime.Token);
            return tls;
        }
        catch { await tls.DisposeAsync(); throw; }
    }

    private static StreamWriter Writer(Stream stream) => new(stream, Encoding.ASCII, 1024, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        // SChannel requires an accessible private key for the server credential. No trust-store entry
        // is installed; the temporary key is released when the certificate is disposed.
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet);
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _listener.Stop();
        try { await _session; }
        catch (Exception error) when (error is OperationCanceledException or IOException or AuthenticationException or SocketException or ObjectDisposedException) { }
        finally { _lifetime.Dispose(); Certificate.Dispose(); }
    }
}
