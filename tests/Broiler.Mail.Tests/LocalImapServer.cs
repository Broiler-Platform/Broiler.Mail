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
    public bool DisconnectOnFetch { get; set; }
    public bool MalformedFetch { get; set; }
    public bool StallOnFetch { get; set; }

    private string Capabilities(bool encrypted) => "IMAP4rev1 AUTH=PLAIN SASL-IR" + (!encrypted && _advertiseStartTls ? " STARTTLS" : "");

    private async Task RunAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(_lifetime.Token);
            await RunClientAsync(client);
        }
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
            using var initialWriter = Writer(transport);
            await initialWriter.WriteLineAsync($"* OK [CAPABILITY {Capabilities(encrypted)}] Fixture ready");
            while (true)
            {
                using var reader = new StreamReader(transport, Encoding.ASCII, false, 1024, leaveOpen: true);
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
                            if (line.EndsWith("\"\" \"\"", StringComparison.Ordinal))
                                await writer.WriteLineAsync($"* LIST (\\Noselect) \"/\" \"\"\r\n{tag} OK List");
                            else
                                await writer.WriteLineAsync($"* LIST (\\HasNoChildren) \"/\" \"INBOX\"\r\n{tag} OK List");
                            break;
                        case "LOGIN" when _rejectAuthentication:
                            await writer.WriteLineAsync($"{tag} NO [AUTHENTICATIONFAILED] Rejected {Password}");
                            break;
                        case "EXAMINE":
                            MailCommands.Enqueue(line[(tag.Length + 1)..]);
                            uint next = Messages.Count == 0 ? 1 : Messages.Max(message => message.Uid) + 1;
                            await writer.WriteLineAsync($"* FLAGS (\\Seen)\r\n* {Messages.Count} EXISTS\r\n* 0 RECENT\r\n* OK [UIDVALIDITY {UidValidity}] Validity\r\n* OK [UIDNEXT {next}] Next\r\n{tag} OK [READ-ONLY] Open");
                            break;
                        case "FETCH":
                        case "UID":
                            MailCommands.Enqueue(line[(tag.Length + 1)..]);
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
            var match = Regex.Match(line, @"UID FETCH (\d+) .*BODY\.PEEK\[\]<0\.(\d+)>");
            if (!match.Success) throw new InvalidOperationException("Body requests must be bounded and use PEEK.");
            uint uid = uint.Parse(match.Groups[1].Value);
            int index = Messages.FindIndex(message => message.Uid == uid);
            if (index >= 0)
            {
                string body = Messages[index].Raw;
                body = body[..Math.Min(body.Length, int.Parse(match.Groups[2].Value))];
                await writer.WriteLineAsync($"* {index + 1} FETCH (UID {uid} BODY[]<0> {{{Encoding.ASCII.GetByteCount(body)}}}");
                await writer.WriteAsync(body);
                await writer.WriteLineAsync(")");
            }
        }
        else
        {
            var match = Regex.Match(line, @"FETCH (\d+)(?::(\d+))? ");
            if (!match.Success) throw new InvalidOperationException("Expected a bounded sequence range.");
            int start = int.Parse(match.Groups[1].Value);
            int end = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : start;
            for (int sequence = start; sequence <= end; sequence++)
            {
                var message = Messages[sequence - 1];
                string flags = message.IsRead ? "\\Seen" : "";
                string address = "((\"Sender\" NIL \"sender\" \"example.test\"))";
                await writer.WriteLineAsync($"* {sequence} FETCH (UID {message.Uid} FLAGS ({flags}) INTERNALDATE \"28-Sep-2026 08:00:00 +0000\" ENVELOPE (\"Mon, 28 Sep 2026 08:00:00 +0000\" \"=?utf-8?b?R3LDvMOfZQ==?=\" {address} {address} {address} NIL NIL NIL NIL \"<fixture@example.test>\"))");
            }
        }
        await writer.WriteLineAsync($"{tag} OK Fetch");
    }

    internal sealed record FixtureMessage(uint Uid, string Raw, bool IsRead = false);

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
        catch (Exception error) when (error is OperationCanceledException or IOException or AuthenticationException or SocketException) { }
        finally { _lifetime.Dispose(); Certificate.Dispose(); }
    }
}
