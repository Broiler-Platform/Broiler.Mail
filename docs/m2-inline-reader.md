# M2 inline reader progress

Updated 2026-11-02. M2 is in progress; this does not close the renderer security gate.

Windows now prefers available HTML directly in the ordinary reading pane. The
Plain text / HTML button changes presentation without refetching mail or opening a
second window. The preference is saved in settings and reused after restart;
older settings default to HTML when present. Plain-text-only messages retain the
selectable text reader. Initialization/policy failure leaves that text reader usable.

The inline toolbar now provides Zoom out, a percentage/reset button, and Zoom in
from 50 to 300 percent. It starts at the system text scale, follows subsequent
text-scale changes until manually zoomed, and resumes following on reset. Zoom is
per-message and preserves the existing scroll anchor during reflow. Controls disable
while showing plain text, and keyboard focus moves off a disabled limit button.

Inline HTML now follows the application's light, dark and high-contrast palettes.
Only the host-generated stylesheet and blocked-image placeholder are recolored:
message text, links and approved image data stay intact, and author styles remain
discarded. Page/background, text, underlined links and table/quote borders use the
theme roles. A live palette change preserves relative reading position and custom
zoom and keeps focus usable. Previously approved images remain in memory without
another fetch; downloads completing after a palette change use the current colors.

Keyboard zoom now supports Ctrl+= / Ctrl+Plus, Ctrl+- and Ctrl+0 on main/keypad
keys. Ctrl+wheel zooms only over the visible HTML body and accumulates precision
wheel fractions. Keyboard zoom requires focus in the HTML document or its actions;
list focus, dialogs, AltGr, plain text and a hidden compact reader do not trigger it.
Tab reaches named hyperlinks and reveals off-screen targets before Enter opens
them. The Plain text alternative is reachable and operable through the same shell
Tab/Enter path. Space / Shift+Space pages the focused HTML document, while modified
Control/Alt+Space is left to other input handling. Settings documents these gestures.

Recoverable managed layout/paint exceptions now stop that surface and queue a
fallback to the selectable text reader. The failure notice disables the failed HTML
controls. A message/surface identity check discards a queued failure after selection
has changed. This does not contain native crashes, out-of-memory conditions, hangs,
or renderer compromise; the process-isolation work remains required.

`IInlineHtmlPreviewHost` supplies a message-scoped UI surface. The Windows adapter
uses the existing tiled `ScrollableHtmlView`, main-window renderer and DPI provider.
Selection/account changes dispose the previous document, links and tile handles;
no asynchronous rendering completion can replace a later document in this slice.
The legacy preview host remains available to existing measurement tools/tests.

Markup passes through `HtmlPreviewPolicy` with bounded CID resolution. Script,
handler, CSS resource and active-content restrictions stay in place. Automatic
remote images are blocked until **Load remote images** is explicitly chosen.
The [controlled image broker](m2-remote-image-broker.md) reuses Broiler.Net
for cookie-free, public-HTTPS-only, redirect-checked fetches with production DNS
pinning and bounded image output. The reader explains the privacy effect, supports
cancel/partial retry, and resets consent when the message is reopened. Switching
messages or choosing Plain text cancels loading; stale results cannot update a new
surface. Downloaded remote images and permissions are not written to the mail cache.
Only deliberate activation of an allowed HTTP(S) link can launch the system browser.
The renderer still shares the application process; the reader labels containment
as pending, and the [security gate](html-renderer-security.md) remains open.

## Validation

- Shared tests cover inline presentation without a window action, text switching,
  selection disposal, initialization failure, and persisted mode alongside geometry.
- A Windows test lays out and rasters the actual inline adapter with hostile script,
  remote image and CSS-import input against a loopback sentinel; no window opens or
  request reaches the listener.
- The published profile smoke now uses the inline adapter for the real HTML raster
  fixture in both Windows x64 package variants.

Current local logs: artifacts/m2-theme-shared.log, m2-theme-windows.log,
m2-theme-aot.log and m2-theme-framework.log. These are local checkout results,
not provider or platform certification.
The shared Release suite passes 740 tests; 79 Windows broker/inline/profile/keyboard/zoom/theme
checks pass. The shared UI cases also check focus moves from hidden text into the
HTML surface and returns to text when the next message has no HTML.
Both Windows x64 package variants pass their published smoke checks, dependency
gate and renderer audit. See [retained evidence](evidence/m2-win-x64/README.md).

## Remaining M2 work

- Restricted renderer process, versioned bounded IPC, timeout/termination/memory
  limits, compromise probes and recovery from crashes/hangs/native failures.
- Broader production networking/TLS and native UI acceptance of consented images.
- Direct HTML selection/copy, broader assistive-technology, native high-contrast
  and resize/DPI/long-message acceptance. The selectable text alternative remains
  available; keyboard/link traversal has deterministic shell-level coverage.
- Wider native UI acceptance, including fast switching and rendering failures.

Palette behavior has actual layout/raster fixture coverage across all four presets;
this is not broad screen-reader or operating-system desktop certification. The text
alternative follows application appearance and provides selectable/copyable content.

## Viewport handoff and navigation (2026-11-03)

The inline HTML and selectable text surfaces now implement a neutral relative-
viewport contract. Switching representation or entering managed-failure fallback
keeps an approximate vertical position for the same message. Top/End controls on
HTML-capable messages use that contract and support keyboard activation; a new
message starts at the top. See [three viewport items](m2-reader-viewport.md).
Legacy window hosting, direct HTML selection and process containment remain open.

## Find and copy in the text alternative (2026-11-03)

Find text and reader-scoped Ctrl+F now search the selected decoded preview locally.
Next/Previous/F3 navigate bounded matches, selecting and revealing them in text;
Copy match writes only the selected hit and does not read the clipboard. A find-
initiated HTML/text switch is temporary and does not save a mode preference.
See [five message find items](m2-message-find.md). Direct HTML selection/copy and
process containment remain open.

The per-message image controls now include **Block remote images again**. It cancels pending loads and removes approved remote images from the current preview, retaining embedded images and reader settings. Earlier requests cannot be undone. See [three permission-reset items](m2-remote-image-reset.md).
