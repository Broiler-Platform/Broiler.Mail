using System.Net;
using System.Net.Sockets;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Media;
using Broiler.Net.Http;

namespace Broiler.Mail.Windows.Preview;

/// <summary>Explicit, message-scoped fetching. Never supplies network capability to the renderer.</summary>
internal sealed partial class RemoteImageBroker : IDisposable
{
    internal const int MaximumImages = 16;
    internal const long MaximumEncodedBytes = 2 * 1024 * 1024;
    private readonly HttpClient _client;
    private static readonly MediaLimits Limits = new(maxEncodedBytes: 1024 * 1024,
        maxDecodedBytes: 64L * 1024 * 1024, maxImagePixels: 16_000_000,
        maxImageDimension: 4096, maxFrames: 1);

    // Supplying a handler is only for deterministic fixtures; production always pins public DNS
    // answers in ConnectCallback and leaves TLS certificate/hostname checks to SocketsHttpHandler.
    internal RemoteImageBroker(HttpMessageHandler? fixtureHandler = null,
        Func<string, CancellationToken, Task<IPAddress[]>>? fixtureResolver = null)
    {
        var network = fixtureHandler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
            Credentials = null, PreAuthenticate = false, AutomaticDecompression = DecompressionMethods.None,
            MaxResponseDrainSize = 0,
            ConnectTimeout = TimeSpan.FromSeconds(5), ConnectCallback = (context, token) => ConnectPublicAsync(context, token, fixtureResolver),
        };
        _client = new HttpClient(new SessionHandler(network)) { Timeout = TimeSpan.FromSeconds(10) };
    }

    internal async Task<(HtmlPreviewDocument Document, HtmlResourceBatchResult Result)> LoadAsync(
        string rawHtml, IReadOnlyDictionary<string, MailEmbeddedImage> embeddedImages,
        CancellationToken cancellationToken = default)
    {
        var batch = await LoadBatchAsync(rawHtml, embeddedImages, null, cancellationToken).ConfigureAwait(false);
        return (batch.Document, batch.Result);
    }

    internal async Task<(HtmlPreviewDocument Document, HtmlResourceBatchResult Result, Dictionary<string, MailEmbeddedImage> Images)> LoadBatchAsync(
        string rawHtml, IReadOnlyDictionary<string, MailEmbeddedImage> embeddedImages,
        IReadOnlyDictionary<string, MailEmbeddedImage>? approvedDownloads,
        CancellationToken cancellationToken = default)
    {
        // Extract every candidate through the same markup policy as the displayed document.
        var blocked = HtmlPreviewPolicy.Create(rawHtml, embeddedImages);
        if (blocked.RemoteImageUrls.Count > MaximumImages)
            throw new InvalidDataException("This message exceeds the 16 remote-image limit.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        long embeddedBytes = embeddedImages.Values.Sum(image => (long)image.Data.Length);
        long approvedBytes = approvedDownloads?.Values.Sum(image => (long)image.Data.Length) ?? 0;
        if (embeddedBytes + approvedBytes > MaximumEncodedBytes) throw new InvalidDataException("Images exceed the message budget.");
        var (result, images) = await HtmlResourceLoader.LoadBatchAsync(_client,
            blocked.RemoteImageUrls, approvedDownloads, Limits, MaximumEncodedBytes - embeddedBytes - approvedBytes,
            deadline.Token, maxTransferredBytes: MaximumEncodedBytes,
            acceptImage: info => info.MediaType is "image/png" or "image/jpeg").ConfigureAwait(false);
        deadline.Token.ThrowIfCancellationRequested();
        // Header inspection is not a full decoder boundary. Keep this first broker to PNG/JPEG,
        // and bound total declared RGBA size before returning anything to the current renderer.
        long pixels = 0;
        foreach (var image in images.Values.Concat(embeddedImages.Values))
        {
            var inspection = HtmlResourceLoader.InspectImageBytes(image.Data, Limits);
            if (!inspection.IsValid || inspection.Info is null || inspection.Info.MediaType is not ("image/png" or "image/jpeg"))
                throw new InvalidDataException("The broker currently permits verified PNG/JPEG images only.");
            pixels = checked(pixels + inspection.Info.PixelCount);
        }
        if (pixels > 16_000_000) throw new InvalidDataException("Images exceed the aggregate decoded pixel budget.");
        var renderingImages = new Dictionary<string, MailEmbeddedImage>(images, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, image) in embeddedImages)
            if (!Uri.TryCreate(name, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) renderingImages.TryAdd(name, image);
        return (HtmlPreviewPolicy.Create(rawHtml, renderingImages, allowRemoteImages: true, approvedRemoteImagesOnly: true), result, images);
    }

    internal static bool IsAllowedUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0) return false;
        string host = uri.IdnHost.TrimEnd('.');
        if (IPAddress.TryParse(host.Trim('[', ']'), out var address)) return IsPublicAddress(address);
        return host.Contains('.') && !new[] { "localhost", "local", "internal", "home", "lan" }
            .Any(suffix => host.Equals(suffix, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        byte[] b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224 &&
                !(b[0] == 100 && b[1] is >= 64 and <= 127) &&
                !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31) &&
                !(b[0] == 192 && (b[1] == 168 || (b[1] == 0 && b[2] is 0 or 2) || (b[1] == 88 && b[2] == 99))) &&
                !(b[0] == 198 && (b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100))) &&
                !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        // Conservative global-unicast policy: also exclude special 2001/16 and 6to4.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId == 0 &&
            (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && b[1] is 1 or 2) &&
            !(b[0] == 0x3f && (b[1] & 0xf0) == 0xf0);
    }

    internal static IPAddress[] RequirePublicAddresses(IEnumerable<IPAddress> answers)
    {
        var addresses = answers.Take(33).ToArray();
        if (addresses.Length is 0 or > 32 || addresses.Any(address => !IsPublicAddress(address)))
            throw new HttpRequestException("Remote image destination is not an approved public address.");
        return addresses;
    }

    private static ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken token,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolver) => ConnectPublicEndpointAsync(context.DnsEndPoint, token, resolver);

    private static async ValueTask<Stream> ConnectPublicEndpointAsync(DnsEndPoint endpoint, CancellationToken token,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolver)
    {
        var answers = resolver is null ? await Dns.GetHostAddressesAsync(endpoint.Host, token).ConfigureAwait(false)
            : await resolver(endpoint.Host, token).ConfigureAwait(false);
        var addresses = RequirePublicAddresses(answers);
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException("Remote image destination could not be reached.");
    }

    public void Dispose() => _client.Dispose();

    private sealed class SessionHandler : HttpMessageHandler
    {
        private readonly BrowserNetworkSession _session;
        internal SessionHandler(HttpMessageHandler handler) => _session = new(new()
        {
            Handler = handler, CookiesEnabled = false, MaximumRedirects = 3, Timeout = TimeSpan.FromSeconds(10),
        });
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var result = await _session.SendAsync(request, new RequestContext
            {
                Destination = RequestDestination.Image, Credentials = CredentialsMode.Omit,
                Client = DocumentRequestContext.CreateTopLevel(request.RequestUri!),
                HopPolicy = (uri, _) => IsAllowedUri(uri),
            }, token).ConfigureAwait(false);
            // TransportResponse owns only this response; HttpClient's caller disposes it.
            return result.Message;
        }
        protected override void Dispose(bool disposing) { if (disposing) _session.Dispose(); base.Dispose(disposing); }
    }
}
