# M2 Windows x64 validation

Local checkout results from 2 November 2026; [scope and remaining work](../../m2-inline-reader.md).

- Shared Release suite: 740 passed, no failures/skips.
- Windows broker, inline adapter, profile smoke and reader keyboard/zoom/theme tests: 79 passed.
- Actual raster fixtures verify page/text/link colors for light, dark and both
  high-contrast palettes. Live changes retain reading position/custom zoom and
  usable focus; delayed approved images adopt the current palette without refetching.
  Published smoke additionally rasters the high-contrast reader.
- Shell-level keyboard coverage verifies scoped zoom, precision Ctrl+wheel, AltGr,
  compact hidden-pane rejection, named off-screen link reveal/activation, and the
  keyboard-operable Plain text alternative. Published smoke exercises zoom/reset keys.
- Controls cover zoom limits/reset, system text-scale following, disabled-button
  focus, and a reader resized to 640 x 480. Managed paint-failure notification and
  queued current/stale-surface fallback have dedicated coverage.
- NativeAOT and framework-dependent Windows x64 packages both passed the published
  profile/cache/credential/MIME checks and real inline-adapter zoom/reset/layout/raster.
  The smoke also exercises a cookie-free Broiler.Net image fetch and private-redirect
  rejection through a synthetic handler; no live HTTP request is made.
  It also checks explicit inline consent state. Additional tests cover partial retry,
  cancellation/disposal, queued completion rejection, no-length transfer budgets,
  and mixed public/private DNS rejection through the production sockets handler.
- Both dependency gates passed with 72 runtime libraries; renderer fingerprints
  matched the restored packages. No IL2xxx/IL3xxx warnings were observed.

The paired build manifests describe unsigned local validation artifacts from a
dirty checkout. Their profile smoke uses the inline adapter; this is not visual
desktop acceptance or process-containment evidence. Logs remain in ignored
artifacts/m2-theme-shared.log, m2-theme-windows.log, m2-theme-aot.log and
m2-theme-framework.log. Consent is per reader surface; process containment and
broader native/network acceptance remain open.
