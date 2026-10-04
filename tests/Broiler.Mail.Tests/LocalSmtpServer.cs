using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Tests;

public enum SmtpFixtureOutcome
{
    Accept, RejectAuthentication, RejectRecipient, RejectMessage, DropAfterData, StallAfterData, MalformedAfterData,
    /// <summary>Greets with 554 instead of 220, as a server refusing service does.</summary>
    RefuseGreeting,
    /// <summary>Receives AUTH and never answers it.</summary>
    StallAuthentication,
    /// <summary>Receives AUTH and closes the connection without an answer.</summary>
    DropOnAuthentication,
    /// <summary>Accepts the sign-in, then receives QUIT and never answers it.</summary>
    StallQuit,
    /// <summary>Accepts the connection, or answers STARTTLS, and never answers the TLS handshake.</summary>
    StallTlsHandshake,
}

/// <summary>Loopback-only SMTP fixture. Records synthetic envelopes and MIME, never authentication payloads.</summary>
internal sealed class LocalSmtpServer : IAsyncDisposable
{
    public const string Password = "smtp-fixture-secret";
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(20));
    private readonly Task _session;
    private readonly TransportSecurity _security;
    private readonly SmtpFixtureOutcome _outcome;
    private readonly bool _advertiseStartTls;
    private readonly bool _silent;
    private readonly string? _authMechanisms;
    private readonly bool _connectionTestOnly;

    /// <param name="authMechanisms">The AUTH mechanisms offered after EHLO; null offers no AUTH at all.</param>
    /// <param name="connectionTestOnly">
    /// A sign-in test may only greet, secure the connection, authenticate, and quit. Any other command, such as
    /// MAIL, RCPT, DATA, BDAT, RSET, NOOP, VRFY, EXPN or ETRN, ends the session with an error that disposal rethrows.
    /// </param>
    public LocalSmtpServer(TransportSecurity security, SmtpFixtureOutcome outcome = SmtpFixtureOutcome.Accept,
        bool advertiseStartTls = true, bool silent = false, string? authMechanisms = "PLAIN", bool connectionTestOnly = false)
    {
        _security = security; _outcome = outcome; _advertiseStartTls = advertiseStartTls; _silent = silent;
        _authMechanisms = authMechanisms; _connectionTestOnly = connectionTestOnly;
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback); names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        Certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _session = RunAsync();
    }
    public X509Certificate2 Certificate { get; }
    public int Port { get; }
    public bool AuthenticatedOverTls { get; private set; }
    public ConcurrentQueue<string> Commands { get; } = new();
    public List<string> Recipients { get; } = [];
    public string? Sender { get; private set; }
    public string? RawMessage { get; private set; }
    public int DataCount { get; private set; }
    public TaskCompletionSource DataReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource AuthenticationReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource QuitReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task RunAsync()
    {
        using var client = await _listener.AcceptTcpClientAsync(_lifetime.Token);
        Stream transport = client.GetStream();
        bool encrypted = _security == TransportSecurity.Tls;
        try
        {
            if (encrypted) transport = await UpgradeOrStallAsync(transport);
            if (_silent) { await Task.Delay(Timeout.InfiniteTimeSpan, _lifetime.Token); return; }
            using (var greeting = Writer(transport))
                await greeting.WriteLineAsync(_outcome == SmtpFixtureOutcome.RefuseGreeting ? "554 No service " + Password : "220 localhost SMTP fixture");
            bool first = true;
            while (true)
            {
                bool upgrade = false;
                using var reader = new StreamReader(transport, Encoding.UTF8, false, 4096, leaveOpen: true);
                using var writer = Writer(transport);
                while (await reader.ReadLineAsync(_lifetime.Token) is { } line)
                {
                    // A TLS handshake sent to this plain-text port is no command; the client gives up on its own.
                    if (first && line.Any(c => c is < ' ' or > '~')) return;
                    first = false;
                    string command = line.Split(' ', 2)[0].ToUpperInvariant();
                    Commands.Enqueue(command);
                    if (_connectionTestOnly && command is not ("EHLO" or "HELO" or "STARTTLS" or "AUTH" or "QUIT"))
                        throw new InvalidOperationException("Submission command during a connection test: " + command);
                    switch (command)
                    {
                        case "EHLO":
                            var extensions = new List<string> { "localhost" };
                            if (!encrypted && _advertiseStartTls) extensions.Add("STARTTLS");
                            if (_authMechanisms is not null) extensions.Add("AUTH " + _authMechanisms);
                            for (int index = 0; index < extensions.Count; index++)
                                await writer.WriteLineAsync((index == extensions.Count - 1 ? "250 " : "250-") + extensions[index]);
                            break;
                        case "STARTTLS":
                            await writer.WriteLineAsync("220 Begin TLS"); upgrade = true; break;
                        case "AUTH":
                            AuthenticationReceived.TrySetResult();
                            if (_outcome == SmtpFixtureOutcome.StallAuthentication) { await Task.Delay(Timeout.InfiniteTimeSpan, _lifetime.Token); return; }
                            if (_outcome == SmtpFixtureOutcome.DropOnAuthentication) return;
                            string[] parts = line.Split(' ');
                            string encoded;
                            if (parts.Length == 3) encoded = parts[2];
                            else { await writer.WriteLineAsync("334 "); encoded = (await reader.ReadLineAsync(_lifetime.Token))!; }
                            bool valid = encrypted && Encoding.UTF8.GetString(Convert.FromBase64String(encoded)) == "\0test\0" + Password;
                            AuthenticatedOverTls = valid && _outcome != SmtpFixtureOutcome.RejectAuthentication;
                            await writer.WriteLineAsync(AuthenticatedOverTls ? "235 Authenticated" : "535 Rejected " + Password);
                            break;
                        case "MAIL":
                            if (!AuthenticatedOverTls) throw new InvalidOperationException("Unauthenticated submission.");
                            Sender = line;
                            await writer.WriteLineAsync("250 Sender accepted"); break;
                        case "RCPT":
                            Recipients.Add(line);
                            // Accept the first recipient, reject the second to exercise all-or-nothing behavior.
                            await writer.WriteLineAsync(_outcome == SmtpFixtureOutcome.RejectRecipient && Recipients.Count > 1 ? "550 Rejected " + Password : "250 Recipient accepted");
                            break;
                        case "DATA":
                            DataCount++;
                            await writer.WriteLineAsync("354 Send data");
                            var body = new StringBuilder();
                            while (await reader.ReadLineAsync(_lifetime.Token) is { } data && data != ".")
                            {
                                body.Append(data.StartsWith("..", StringComparison.Ordinal) ? data[1..] : data).Append("\r\n");
                                if (body.Length > 2 * 1024 * 1024) throw new InvalidOperationException("Fixture data limit exceeded.");
                            }
                            RawMessage = body.ToString(); DataReceived.TrySetResult();
                            if (_outcome == SmtpFixtureOutcome.DropAfterData) return;
                            if (_outcome == SmtpFixtureOutcome.StallAfterData) { await Task.Delay(Timeout.InfiniteTimeSpan, _lifetime.Token); return; }
                            await writer.WriteLineAsync(_outcome switch
                            {
                                SmtpFixtureOutcome.RejectMessage => "554 Message rejected " + Password,
                                SmtpFixtureOutcome.MalformedAfterData => "invalid response",
                                _ => "250 Accepted " + Password,
                            });
                            break;
                        case "RSET": await writer.WriteLineAsync("250 Reset"); break;
                        case "QUIT":
                            QuitReceived.TrySetResult();
                            if (_outcome == SmtpFixtureOutcome.StallQuit) { await Task.Delay(Timeout.InfiniteTimeSpan, _lifetime.Token); return; }
                            await writer.WriteLineAsync("221 Bye"); return;
                        default: throw new InvalidOperationException("Unexpected fixture command: " + command);
                    }
                    if (upgrade) break;
                }
                if (!upgrade) return;
                transport = await UpgradeOrStallAsync(transport); encrypted = true;
            }
        }
        finally { await transport.DisposeAsync(); }
    }
    private async Task<Stream> UpgradeOrStallAsync(Stream stream)
    {
        // The client's handshake stays unread until the fixture is disposed.
        if (_outcome == SmtpFixtureOutcome.StallTlsHandshake) await Task.Delay(Timeout.InfiniteTimeSpan, _lifetime.Token);
        return await UpgradeAsync(stream);
    }
    private async Task<SslStream> UpgradeAsync(Stream stream)
    {
        var tls = new SslStream(stream);
        try
        {
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            { ServerCertificate = Certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, _lifetime.Token);
            return tls;
        }
        catch { await tls.DisposeAsync(); throw; }
    }
    private static StreamWriter Writer(Stream stream) => new(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel(); _listener.Stop();
        try { await _session; }
        catch (Exception error) when (error is OperationCanceledException or IOException or AuthenticationException or SocketException or ObjectDisposedException) { }
        finally { Certificate.Dispose(); _lifetime.Dispose(); }
    }
}
