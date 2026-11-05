# Production image HTTPS/TLS acceptance — Windows x64

Validated locally 2026-11-04 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. This follow-up postdates the earlier local CI snapshot. Nothing was published externally.

The explicitly invoked [diagnostic](../../../eng/Broiler.Mail.ImageNetworkProbe/README.md) was published as NativeAOT for win-x64. The dependency policy passed with **13 runtime libraries**. The production broker uses its default socket handler, public-DNS address guard/pinning and platform TLS validation; no synthetic handler, resolver, certificate callback or trust override was installed.

All four checks passed; [retained machine-readable report](results.json):

| Fixed fixture | Broker result | Independent default TLS handshake |
| --- | --- | --- |
| W3C PNG | 1 image, 1,848 payload bytes | Authenticated |
| badssl expired certificate | 0 images, NetworkError | Authentication rejected |
| badssl hostname mismatch | 0 images, NetworkError | Authentication rejected |
| badssl self-signed certificate | 0 images, NetworkError | Authentication rejected |

The separate TLS handshake uses the same production DNS-pinned connector. It distinguishes authentication rejection from a mere broker network failure without weakening certificate validation. These are external fixtures, not controlled provider/fault environments. Fixtures may change and environmental failures must be investigated.

No account, profile, mail or credentials were used. No mailbox operation occurred. The diagnostic remains outside startup and CI. Ignored logs: `artifacts/m2-image-network-publish.log`, `artifacts/m2-image-network-live.log`. Renderer process containment, adversarial acceptance of all delivered-adapter resource paths and other-platform execution remain open in the [completion audit](../../m0-m4-completion-status.md).
