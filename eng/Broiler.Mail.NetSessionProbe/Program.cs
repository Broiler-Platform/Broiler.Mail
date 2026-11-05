using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Broiler.Net.Cookies;
using Broiler.Net.Http;

namespace Broiler.Mail.NetSessionProbe;

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            var checks = new List<string>();
            var origin = new Uri("https://mail.example.test/");
            using var handlerA = new FixtureHandler();
            using var handlerB = new FixtureHandler();
            var storeA = new CookieStore();
            using var accountA = new BrowserNetworkSession(new() { Cookies = storeA, Handler = handlerA });
            using var accountB = new BrowserNetworkSession(new() { Handler = handlerB });
            using (await Send(accountA, new Uri(origin, "seed"))) { }
            using (await Send(accountA, origin)) { }
            using (await Send(accountB, origin)) { }
            Require(handlerA.Requests[^1].Cookie == "fixture=synthetic", "Same profile did not retain its cookie.");
            Require(handlerB.Requests[^1].Cookie is null && accountB.Cookies.Snapshot().Count == 0, "Separate profiles leaked cookies.");
            Require(accountA.TryGetCookie(DocumentRequestContext.CreateTopLevel(origin), out string visible) && visible.Length == 0,
                "HttpOnly fixture leaked into document access.");
            checks.Add("one store/session retains cookies across requests; independent sessions isolate same-origin cookies; HttpOnly stays privileged");

            // Sharing the store is explicit host ownership, not a disk-profile implementation.
            using (var replacement = new BrowserNetworkSession(new() { Cookies = storeA, Handler = handlerA }))
            using (await Send(replacement, origin)) { }
            Require(handlerA.Requests[^1].Cookie == "fixture=synthetic", "Explicit store reuse lost cookie state.");
            using (var fresh = new BrowserNetworkSession(new() { Handler = handlerB }))
            {
                Require(fresh.Cookies.Snapshot().Count == 0, "Fresh session unexpectedly restored persisted state.");
            }
            checks.Add("explicit store reuse preserves memory state; a fresh store does not restore cookies from disk");

            int countBefore = storeA.Snapshot().Count;
            using (var cookieFree = new BrowserNetworkSession(new() { Cookies = storeA, CookiesEnabled = false, Handler = handlerA }))
            using (await Send(cookieFree, new Uri(origin, "seed-other"))) { }
            Require(handlerA.Requests[^1].Cookie is null && storeA.Snapshot().Count == countBefore,
                "CookiesEnabled=false sent or stored cookies.");
            using (var omit = new BrowserNetworkSession(new() { Handler = handlerB }))
            using (await Send(omit, new Uri(origin, "seed"), ResourceContext(origin) with { Credentials = CredentialsMode.Omit }))
                Require(omit.Cookies.Snapshot().Count == 0 && handlerB.Requests[^1].Cookie is null,
                    "CredentialsMode.Omit sent or stored cookies.");
            checks.Add("CookiesEnabled=false and CredentialsMode.Omit suppress both sending and storing cookies");

            using var redirectHandler = new FixtureHandler();
            using var redirectSession = new BrowserNetworkSession(new() { CookiesEnabled = false, Handler = redirectHandler });
            bool blocked = false;
            try
            {
                using var response = await Send(redirectSession, new Uri(origin, "redirect"), ResourceContext(origin) with
                { HopPolicy = (uri, _) => uri.Scheme == "https" && uri.Host == origin.Host });
            }
            catch (TransportException error) when (error.Error == TransportError.Blocked) { blocked = true; }
            Require(blocked && redirectHandler.Requests.Count == 1, "Redirect policy dispatched a forbidden hop.");
            checks.Add("host HopPolicy rejects an unapproved redirect before handler dispatch");

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            bool cancelled = false;
            try { using var response = await Send(accountA, new Uri(origin, "wait"), cancellationToken: cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
            Require(cancelled, "Host cancellation did not reach the session request.");
            accountA.Dispose();
            bool disposed = false;
            try { using var response = await Send(accountA, origin); } catch (ObjectDisposedException) { disposed = true; }
            Require(disposed && storeA.Snapshot().Count == countBefore, "Session disposal contract changed.");
            storeA.Clear();
            Require(storeA.Snapshot().Count == 0, "Host could not explicitly clear its store.");
            checks.Add("host cancellation is honored; disposed sessions reject sends; externally owned stores require explicit clearing");

            var report = new SessionReport(RuntimeInformation.RuntimeIdentifier, "Broiler.Net/0.1.0-preview.4",
                "Synthetic HttpMessageHandler only: no live HTTP, disk persistence, OAuth or IMAP/SMTP claim.", checks.ToArray());
            Console.WriteLine(JsonSerializer.Serialize(report, SessionJsonContext.Default.SessionReport));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static RequestContext ResourceContext(Uri origin) => new()
    { Destination = RequestDestination.Image, Client = DocumentRequestContext.CreateTopLevel(origin), Credentials = CredentialsMode.Omit };

    private static async Task<TransportResponse> Send(BrowserNetworkSession session, Uri url,
        RequestContext? context = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await session.SendAsync(request, context ?? RequestContext.TopLevelNavigation(null), cancellationToken);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

internal sealed class FixtureHandler : HttpMessageHandler
{
    public List<(Uri Url, string? Cookie)> Requests { get; } = [];
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
        SendAsync(request, cancellationToken).GetAwaiter().GetResult();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = request.RequestUri ?? throw new InvalidOperationException("Missing fixture URL.");
        Requests.Add((url, request.Headers.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : null));
        if (url.AbsolutePath == "/wait") await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fixture"), RequestMessage = request };
        if (url.AbsolutePath.StartsWith("/seed", StringComparison.Ordinal))
            response.Headers.TryAddWithoutValidation("Set-Cookie", url.AbsolutePath == "/seed"
                ? "fixture=synthetic; Path=/; Secure; HttpOnly; Max-Age=3600"
                : "other=synthetic; Path=/; Secure; HttpOnly");
        if (url.AbsolutePath == "/redirect")
        { response.StatusCode = HttpStatusCode.Found; response.Headers.Location = new Uri("http://127.0.0.1/blocked"); }
        return response;
    }
}

internal sealed record SessionReport(string RuntimeIdentifier, string Package, string Scope, string[] Checks);
[JsonSerializable(typeof(SessionReport))]
internal partial class SessionJsonContext : JsonSerializerContext;
