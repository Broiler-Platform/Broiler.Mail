using System.Net;
using System.Net.Http.Headers;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Windows.Hosting;
using Broiler.Mail.Windows.Preview;

namespace Broiler.Mail.Windows.Tests;

public sealed class RemoteImageBrokerTests
{
    [Fact]
    public async Task RecognizedGifIsBlockedWithoutDiscardingSupportedImages()
    {
        const string root = "https://images.example.test/";
        byte[] gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
        byte[] png = Convert.FromBase64String(RemoteImageBrokerSmokeCheck.Pixel);
        using var broker = new RemoteImageBroker(new Handler((request, _) =>
        {
            bool isGif = request.RequestUri!.AbsolutePath == "/pixel";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(isGif ? gif : png) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png"); // The encoded format controls policy.
            return Task.FromResult(response);
        }));
        var (document, result, images) = await broker.LoadBatchAsync(
            $"<img src='{root}before'><img src='{root}pixel'><img src='{root}after'>",
            new Dictionary<string, MailEmbeddedImage>(), null);

        Assert.Equal(HtmlResourceOutcomeKind.Partial, result.OverallOutcome);
        Assert.Equal(2, result.TotalSucceeded);
        var blocked = Assert.Single(result.Results, item => item.Outcome == HtmlResourceOutcomeKind.Blocked);
        Assert.Equal("image/gif", blocked.ImageInfo?.MediaType);
        Assert.Equal(HtmlResourceFailureReason.PolicyBlocked, blocked.Reason);
        Assert.Equal(new[] { root + "after", root + "before" }, images.Keys.Order());
        Assert.Contains("data:image/png;base64,", document.Html);
        Assert.Contains("Remote image unavailable", document.Html);
        Assert.DoesNotContain("data:image/gif", document.Html);
    }

    [Fact]
    public async Task ProductionSocketsHandlerRejectsANameResolvingToPrivateAddresses()
    {
        int resolutions = 0;
        using var broker = new RemoteImageBroker(fixtureResolver: (host, _) =>
        {
            Assert.Equal("images.example.test", host); resolutions++;
            return Task.FromResult(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Loopback });
        });
        var (_, result) = await broker.LoadAsync("<img src='https://images.example.test/a'>", new Dictionary<string, MailEmbeddedImage>());
        Assert.True(resolutions > 0); Assert.Equal(0, result.TotalSucceeded); Assert.Equal(1, result.TotalFailed);
    }

    [Fact]
    public async Task StreamingFailuresShareTheBatchTransferBudgetWithoutContentLength()
    {
        long read = 0; int requests = 0;
        using var broker = new RemoteImageBroker(new Handler((request, _) =>
        {
            requests++;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StreamContent(new EndlessStream(n => read += n)) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        }));
        var (_, result) = await broker.LoadAsync(string.Concat(Enumerable.Range(0, 3).Select(i => $"<img src='https://images.example.test/{i}'>")), new Dictionary<string, MailEmbeddedImage>());
        Assert.Equal(2, requests); Assert.Equal(3, result.TotalBudgetExceeded);
        Assert.InRange(read, RemoteImageBroker.MaximumEncodedBytes, RemoteImageBroker.MaximumEncodedBytes + 8192);
    }

    [Fact]
    public Task PublishedFixtureBlocksPrivateRedirectAndDoesNotShareCookies() => RemoteImageBrokerSmokeCheck.RunAsync();

    [Fact]
    public async Task MessageSuppliedRemoteUrlKeysCannotMasqueradeAsApprovedDownloads()
    {
        const string url = "https://images.example.test/missing";
        using var broker = new RemoteImageBroker(new Handler((request, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request, Content = new ByteArrayContent([]) })));
        var (document, result) = await broker.LoadAsync($"<img src='{url}'>", new Dictionary<string, MailEmbeddedImage>
        { [url] = new(url, "image/png", Convert.FromBase64String(RemoteImageBrokerSmokeCheck.Pixel)) });
        Assert.Equal(0, result.TotalSucceeded); Assert.DoesNotContain("data:image/png;base64,", document.Html);
        Assert.Contains("Remote image unavailable", document.Html);
    }

    [Theory]
    [InlineData("http://images.example.test/a")]
    [InlineData("https://images.example.test:444/a")]
    [InlineData("https://user:secret@images.example.test/a")]
    [InlineData("https://127.0.0.1/a")]
    [InlineData("https://10.0.0.1/a")]
    [InlineData("https://169.254.169.254/a")]
    [InlineData("https://192.168.1.1/a")]
    [InlineData("https://100.64.0.1/a")]
    [InlineData("https://[::1]/a")]
    [InlineData("https://[::ffff:127.0.0.1]/a")]
    [InlineData("https://[fc00::1]/a")]
    [InlineData("https://[fe80::1]/a")]
    [InlineData("https://printer.local/a")]
    [InlineData("https://localhost./a")]
    [InlineData("https://intranet/a")]
    public async Task ForbiddenDestinationsNeverReachTheHandler(string url)
    {
        int calls = 0;
        using var broker = new RemoteImageBroker(new Handler((_, _) => { calls++; throw new InvalidOperationException("Must not dispatch"); }));
        var (document, result) = await broker.LoadAsync($"<img src='{url}'>", new Dictionary<string, MailEmbeddedImage>());
        Assert.Equal(0, calls); Assert.Equal(0, result.TotalSucceeded);
        Assert.DoesNotContain("src=\"https:", document.Html);
    }

    [Fact]
    public void MixedDnsAnswersAndAddressAliasesAreRejectedBeforeConnecting()
    {
        Assert.Throws<HttpRequestException>(() => RemoteImageBroker.RequirePublicAddresses([IPAddress.Parse("8.8.8.8"), IPAddress.Loopback]));
        Assert.Throws<HttpRequestException>(() => RemoteImageBroker.RequirePublicAddresses([]));
        Assert.Throws<HttpRequestException>(() => RemoteImageBroker.RequirePublicAddresses(Enumerable.Repeat(IPAddress.Parse("8.8.8.8"), 33)));
        Assert.Single(RemoteImageBroker.RequirePublicAddresses([IPAddress.Parse("2606:4700:4700::1111")]));
        Assert.False(RemoteImageBroker.IsAllowedUri(new Uri("https://2130706433/a")));
        Assert.False(RemoteImageBroker.IsAllowedUri(new Uri("https://0x7f000001/a")));
    }

    [Fact]
    public async Task CountAndDeclaredByteBudgetsRejectWithoutReadingBodies()
    {
        int calls = 0;
        using var broker = new RemoteImageBroker(new Handler((request, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]), RequestMessage = request };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            response.Content.Headers.ContentLength = 1024 * 1024 + 1;
            return Task.FromResult(response);
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.LoadAsync(string.Concat(Enumerable.Range(0, 17).Select(i => $"<img src='https://images.example.test/{i}'>")), new Dictionary<string, MailEmbeddedImage>()));
        Assert.Equal(0, calls);
        var (_, result) = await broker.LoadAsync("<img src='https://images.example.test/large'>", new Dictionary<string, MailEmbeddedImage>());
        Assert.Equal(1, calls); Assert.Equal(1, result.TotalBudgetExceeded);
    }

    [Fact]
    public async Task CancellationReachesAnOutstandingRequest()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var broker = new RemoteImageBroker(new Handler(async (_, token) =>
        {
            entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new InvalidOperationException();
        }));
        using var cancellation = new CancellationTokenSource();
        var loading = broker.LoadAsync("<img src='https://images.example.test/held'>", new Dictionary<string, MailEmbeddedImage>(), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }

    private sealed class EndlessStream(Action<int> read) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { Array.Clear(buffer, offset, count); read(count); return count; }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(Read(buffer, offset, count)); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
