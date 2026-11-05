using System.Net;
using System.Net.Http.Headers;
using Broiler.Mail.Windows.Preview;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>Synthetic handler only: no DNS, sockets, accounts or persisted permission.</summary>
internal static class RemoteImageBrokerSmokeCheck
{
    internal const string Pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
    internal static async Task RunAsync()
    {
        var handler = new FixtureHandler();
        using var broker = new RemoteImageBroker(handler);
        var (document, result) = await broker.LoadAsync("<p>Fixture</p><img src='https://images.example.test/pixel'><img src='https://images.example.test/redirect'>", new Dictionary<string, Broiler.Mail.Core.Messages.MailEmbeddedImage>());
        if (result.TotalSucceeded != 1 || handler.Requests != 2 || handler.LeakedHeaders ||
            !document.Html.Contains("data:image/png;base64,") || document.Html.Contains("src=\"https:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Remote-image broker fixture failed.");
        int inlineRequests = 0;
        using var host = new WindowsHtmlPreviewHost { CreateImageBroker = () => new(new FixtureHandler(() => inlineRequests++)) };
        var key = new Broiler.Mail.Core.Messages.MailMessageKey(new(Guid.NewGuid()), "INBOX", 7, 1);
        using var surface = (ScrollableHtmlView)host.CreateInlineContent(new(key, "Fixture", "<img src='https://images.example.test/pixel'>"));
        if (surface.RemoteImages.Loaded != 0) throw new InvalidOperationException("Remote images loaded before consent.");
        await surface.LoadRemoteImagesAsync();
        if (surface.RemoteImages.Loaded != 1 || surface.RemoteImages.CanLoad)
            throw new InvalidOperationException("Inline remote-image consent fixture failed.");
        surface.BlockRemoteImages();
        if (surface.RemoteImages.Loaded != 0 || !surface.RemoteImages.CanLoad || inlineRequests != 1 ||
            surface.Content.CalculateLayout(640).Diagnostics.Any(box => box.TagName == "img"))
            throw new InvalidOperationException("Inline remote-image permission reset fixture failed.");
        await surface.LoadRemoteImagesAsync();
        if (surface.RemoteImages.Loaded != 1 || inlineRequests != 2)
            throw new InvalidOperationException("Inline remote-image fresh consent fixture failed.");
    }

    private sealed class FixtureHandler(Action? onRequest = null) : HttpMessageHandler
    {
        internal int Requests; internal bool LeakedHeaders;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests++; onRequest?.Invoke();
            LeakedHeaders |= request.Headers.Contains("Cookie") || request.Headers.Contains("Authorization") || request.Headers.Referrer is not null;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String(Pixel)), RequestMessage = request };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            response.Headers.TryAddWithoutValidation("Set-Cookie", "tracking=must-not-persist; Secure; Path=/");
            if (request.RequestUri!.AbsolutePath == "/redirect")
            {
                response.StatusCode = HttpStatusCode.Found;
                response.Headers.Location = new Uri("https://127.0.0.1/blocked");
            }
            return Task.FromResult(response);
        }
    }
}
