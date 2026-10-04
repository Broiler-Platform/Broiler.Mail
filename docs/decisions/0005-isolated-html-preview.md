# Decision 0005: Isolated HTML preview and content policy

Status: content policy implemented; process isolation pending. Date: 2026-09-30. Extends decisions 0001–0004.

Implement the version 2 HTML preview milestone using the native Broiler ecosystem
component `Broiler.HTML` (`Broiler.HTML.Image` / `Broiler.HTML.Core`) hosted in a
native `Direct2DWindow` behind the `IHtmlPreviewHost` boundary. This runs in the mail
process and does not meet the isolation requirement in `docs/roadmap.md`. The
[Phase 0 renderer specification](../html-renderer-security.md) defines the required
process boundary and supersedes the earlier sandbox claim.

## Content reduction and sanitization

Raw message HTML is reduced to passive markup using MimeKit's tokenizer in
`HtmlPreviewPolicy`. Tag allowances are restricted to passive typography and tables
(e.g., `p`, `div`, `h1`-`h6`, `table`, `code`, `blockquote`, `a`). Scripts, frames,
forms, plugins, styles, SVG, and interactive handlers are discarded.

A strict Content-Security-Policy (CSP) meta header is injected:
`default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; font-src 'none'; connect-src 'none'; media-src 'none'; object-src 'none'; frame-src 'none'; form-action 'none'; base-uri 'none'`.

## Current native preview host

The native preview host (`WindowsHtmlPreviewHost`, `HtmlPreviewWindow`) hosts
`Broiler.HTML` (`HtmlContainer` / `BBitmap`) inside a native `Direct2DWindow` using
`Broiler.UI` layout and rendering without external web engine or WinForms dependencies:
- Zero script execution: `Broiler.HTML` has no JavaScript engine or script evaluation.
- Network requests are blocked at both transport and container layers:
  `HtmlContainer.RequestTransport` is bound to a strict `DenyingRequestTransport` that
  returns HTTP 403 Forbidden without dispatching network requests, and `StylesheetLoad`
  and `ImageLoad` events intercept and block external/remote stylesheets and images.
- Resource policy denies local file and loopback references; this is not an OS restriction
  on a compromised renderer, which still shares the mail process's privileges.
- Raster snapshots are bounded: the document is cut at 32,768 CSS pixels tall (and content
  that cannot wrap at 8,192 wide), it is drawn in 1,024-DIP tiles of at most 8 M pixels and
  8,192 pixels a side, and the tile cache holds at most 16 tiles and 256 MB. Images are
  decoded via `Broiler.Media.Image.Managed` codecs.
- Tiles are rastered by the preview window's own thread alone. Broiler.HTML's parallel raster
  made that STA thread wait in a way that dispatched input and UI Automation calls in the
  middle of a tile (NativeAOT), so it is switched off for the preview.
- Zoom (50–300 %) is a page zoom: the document is laid out at the viewport's width over the
  zoom and drawn larger, so text still wraps to the window. A preview opens at the system text
  size and follows it until the reader zooms; the zoom also enlarges the plain-text view.

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
- Renderer failure: handled errors can show the plain-text body. Process crashes and
  hangs are not contained until the restricted renderer boundary is implemented.
