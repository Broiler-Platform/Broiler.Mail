# Three M2 remote-image permission reset items

Implemented 2026-11-04. The ordinary Windows inline reader now supports **Block remote images again** for the current open message.

1. **Surface permission reset.** Cancels pending requests, invalidates queued/late results, drops the approved remote-image map and rebuilds the restricted document from original HTML and embedded resources. CID/inline images, approximate reading position, zoom and current theme survive. A rebuild failure hides the HTML content and requests the existing text fallback. Reset never starts a request; a fresh explicit load refetches remote images. Other open surfaces are independent.
2. **Explicit reader control.** The optional platform-neutral `IInlineRemoteImageReset` capability keeps existing hosts compatible. The control is enabled only in HTML mode while loading or with approved images. Focus returns to the load control after blocking. There is no default action, saved permission, sender-wide allowlist or server mutation. The reader explains that requests already sent cannot be undone. Cancel loading continues to retain earlier completed approvals; blocking clears them.
3. **Regression and published validation.** Synthetic handlers cover pending cancellation, queued/late rejection, fresh consent, retained CID images, another surface, theme, zoom and approximate reading position. Shared UI tests cover loaded/pending reset, focus, optional hosts and HTML-mode guards. Published smoke exercises the real broker/surface, confirms reset causes no new request, and requires fresh consent to load again in NativeAOT and framework-dependent builds.

[Windows x64 evidence](evidence/m2-three-image-reset-items-win-x64/README.md). No public server or actual mail account is contacted by these fixtures.

This is a per-open-surface permission reset, not a promise to erase process/OS caches or retract a request. Native renderer failure containment, controlled production DNS/TLS acceptance, wider accessibility/desktop acceptance and other M0–M4 work remain open. JavaScript remains absent by design; M2 is not complete.
