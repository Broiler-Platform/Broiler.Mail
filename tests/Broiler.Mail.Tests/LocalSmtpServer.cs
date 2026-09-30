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

public enum SmtpFixtureOutcome { Accept, RejectAuthentication, RejectRecipient, RejectMessage, DropAfterData, StallAfterData, MalformedAfterData }

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
    public LocalSmtpServer(TransportSecurity security, SmtpFixtureOutcome outcome = SmtpFixtureOutcome.Accept,
        bool advertiseStartTls = true, bool silent = false)
    {
        _security = security; _outcome = outcome; _advertiseStartTls = advertiseStartTls; _silent = silent;
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

    private async Task RunAsync()
    {
        using var client = await _listener.AcceptTcpClientAsync(_lifetime.Token);
        Stream transport = client.GetStream();
        bool encrypted = _security == TransportSecurity.Tls;
        try
        {
            if (encrypted) transport = await UpgradeAsync(transport);
            if (_silent) { await Task.Delay(Timeout.InfiniteTimeSpan, _lifetime.Token); return; }
            using (var greeting = Writer(transport)) await greeting.WriteLineAsync("220 localhost SMTP fixture");
            while (true)
            {
                bool upgrade = false;
                using var reader = new StreamReader(transport, Encoding.UTF8, false, 4096, leaveOpen: true);
                using var writer = Writer(transport);
                while (await reader.ReadLineAsync(_lifetime.Token) is { } line)
                {
                    string command = line.Split(' ', 2)[0].ToUpperInvariant();
                    Commands.Enqueue(command);
                    switch (command)
                    {
                        case "EHLO":
                            await writer.WriteLineAsync("250-localhost");
                            if (!encrypted && _advertiseStartTls) await writer.WriteLineAsync("250-STARTTLS");
                            await writer.WriteLineAsync("250 AUTH PLAIN");
                            break;
                        case "STARTTLS":
                            await writer.WriteLineAsync("220 Begin TLS"); upgrade = true; break;
                        case "AUTH":
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
                        case "QUIT": await writer.WriteLineAsync("221 Bye"); return;
                        default: throw new InvalidOperationException("Unexpected fixture command: " + command);
                    }
                    if (upgrade) break;
                }
                if (!upgrade) return;
                transport = await UpgradeAsync(transport); encrypted = true;
            }
        }
        finally { await transport.DisposeAsync(); }
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
