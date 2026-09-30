# Decision 0005: Isolated HTML preview and content policy

Status: implemented. Date: 2026-09-30. Extends decisions 0001–0004.

Implement the version 2 HTML preview milestone using the native Broiler ecosystem
component `Broiler.HTML` (`Broiler.HTML.Image` / `Broiler.HTML.Core`) hosted in a
native `Direct2DWindow` behind the `IHtmlPreviewHost` boundary, meeting the isolation
and resource policy requirements defined in `docs/roadmap.md`.

## Content reduction and sanitization

Raw message HTML is reduced to passive markup using MimeKit's tokenizer in
`HtmlPreviewPolicy`. Tag allowances are restricted to passive typography and tables
(e.g., `p`, `div`, `h1`-`h6`, `table`, `code`, `blockquote`, `a`). Scripts, frames,
forms, plugins, styles, SVG, and interactive handlers are discarded.

A strict Content-Security-Policy (CSP) meta header is injected:
`default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; font-src 'none'; connect-src 'none'; media-src 'none'; object-src 'none'; frame-src 'none'; form-action 'none'; base-uri 'none'`.

## Native isolated preview host

The native preview host (`WindowsHtmlPreviewHost`, `HtmlPreviewWindow`) hosts
`Broiler.HTML` (`HtmlContainer` / `BBitmap`) inside a native `Direct2DWindow` using
`Broiler.UI` layout and rendering without external web engine or WinForms dependencies:
- Zero script execution: `Broiler.HTML` has no JavaScript engine or script evaluation.
- Network requests are blocked at both transport and container layers:
  `HtmlContainer.RequestTransport` is bound to a strict `DenyingRequestTransport` that
  returns HTTP 403 Forbidden without dispatching network requests, and `StylesheetLoad`
  and `ImageLoad` events intercept and block external/remote stylesheets and images.
- No local file access: `file:` and loopback resources are denied and cannot escape the host.
- Raster snapshots are bounded to Direct2D texture limits (clamped height up to 8192 px)
  and encoded via `Broiler.Media.Image.Managed` codecs.

## Inline images and remote resources

- Embedded `cid:` images: Extracted from MIME `multipart/related` parts by
  `MessageTextDecoder` with strict bounds (1 MiB per image, 16 images maximum, 2 MiB total).
  Content types are restricted to safe raster images (`png`, `jpeg`, `gif`, `webp`).
  Resolved exclusively within the current message and converted into inlined data URIs.
- Remote images: Blocked by default. The preview UI displays a placeholder indicator.
  When the user explicitly chooses "Load remote images", requests are fetched through
  a controlled .NET `HttpClient` that validates HTTP/HTTPS URLs, enforces non-SVG MIME types,
  bounds size to 5 MB per image, and uses a 10-second timeout.
- Plain-text toggle: The preview window includes a toggle button switching seamlessly
  between the formatted HTML snapshot and the message's plain-text representation.
- External links: Valid absolute HTTP/HTTPS links are opened exclusively in the user's
  external system browser via shell execute; internal browser navigation is cancelled.
- Renderer failure: If the sandboxed renderer encounters a crash or initialization failure,
  it falls back to displaying the plain-text body in a read-only viewer.
