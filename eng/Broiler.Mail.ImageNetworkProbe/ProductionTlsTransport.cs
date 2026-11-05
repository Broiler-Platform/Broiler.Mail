using System.Net;

namespace Broiler.Mail.Windows.Preview;

internal sealed partial class RemoteImageBroker
{
    // Compiled only into the opt-in diagnostic, sharing the production DNS-pinned socket implementation.
    internal static async Task<Stream> ConnectProbeAsync(Uri uri, CancellationToken token)
    {
        if (!IsAllowedUri(uri)) throw new InvalidOperationException("Probe destination is not public HTTPS.");
        return await ConnectPublicEndpointAsync(new DnsEndPoint(uri.IdnHost, uri.Port), token, null);
    }
}
