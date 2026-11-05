using System.Text.Json;
using System.Net.Security;
using System.Security.Authentication;
using System.Text.Json.Serialization;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Windows.Preview;

namespace Broiler.Mail.ImageNetworkProbe;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args is not ["--public-https-check", "--output", var output])
        { Console.Error.WriteLine("Explicit public-network diagnostic: --public-https-check --output <new-report.json>. Never run automatically."); return 2; }
        var results = new List<Check>();
        foreach (var (name, url, expectImage) in new[]
        {
            ("public-png", "https://www.w3.org/Icons/w3c_home.png", true),
            ("expired-certificate-endpoint", "https://expired.badssl.com/favicon.ico", false),
            ("hostname-mismatch-endpoint", "https://wrong.host.badssl.com/favicon.ico", false),
            ("self-signed-endpoint", "https://self-signed.badssl.com/favicon.ico", false)
        })
        {
            try
            {
                using var broker = new RemoteImageBroker(); // Production DNS pinning and platform TLS validation, with no test overrides.
                var result = await broker.LoadAsync("<img src='" + url + "'>", new Dictionary<string, MailEmbeddedImage>());
                bool image = result.Result.TotalSucceeded == 1 && result.Result.TotalDownloadedBytes > 0 && result.Document.Html.Contains("data:image/png;base64,");
                bool refused = result.Result.TotalSucceeded == 0 && result.Result.Results.Count == 1 && result.Result.Results[0].Reason == HtmlResourceFailureReason.NetworkError;
                string tls;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var transport = await RemoteImageBroker.ConnectProbeAsync(new Uri(url), timeout.Token);
                    await using var ssl = new SslStream(transport); // No certificate callback or custom trust policy.
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = new Uri(url).IdnHost }, timeout.Token);
                    tls = "Authenticated";
                }
                catch (AuthenticationException) { tls = "AuthenticationRejected"; }
                catch (Exception failure) when (failure is not OutOfMemoryException) { tls = failure.GetType().Name; }
                bool tlsExpected = tls == (expectImage ? "Authenticated" : "AuthenticationRejected");
                results.Add(new(name, url, (expectImage ? image : refused) && tlsExpected, result.Result.TotalSucceeded, result.Result.TotalDownloadedBytes,
                    result.Result.Results.Count == 1 ? result.Result.Results[0].Reason.ToString() : "UnexpectedCount", tls));
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            { results.Add(new(name, url, false, 0, 0, failure.GetType().Name, "NotRun")); }
        }
        var report = new Report(1, DateTimeOffset.UtcNow, System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, results,
            "Explicit fixed public fixtures only; production SocketsHttpHandler/public-DNS pinning; no handler/resolver/TLS overrides, cookies, credentials, account/profile/mail input or renderer. A separate default SslStream handshake over the production DNS-pinned connector verifies authentication success/rejection without certificate callbacks or custom trust; broker failures remain coarse NetworkError. No startup/CI invocation.");
        string destination = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(file, report, ProbeJson.Default.Report); await file.FlushAsync(); file.Flush(flushToDisk: true);
        Console.WriteLine(JsonSerializer.Serialize(report, ProbeJson.Default.Report)); return results.All(row => row.Passed) ? 0 : 1;
    }
}
internal sealed record Check(string Name, string Url, bool Passed, int Loaded, long PayloadBytes, string Outcome, string TlsOutcome);
internal sealed record Report(int SchemaVersion, DateTimeOffset CheckedAtUtc, string Runtime, IReadOnlyList<Check> Checks, string Scope);
[JsonSerializable(typeof(Report))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class ProbeJson : JsonSerializerContext;
