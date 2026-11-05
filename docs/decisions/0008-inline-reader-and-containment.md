# Decision 0008: Inline reader and independent containment boundary

Date: 2026-11-02. Status: inline Windows implementation started; containment pending (M2).
Supersedes the separate-window presentation choice in ADR 0005, not its content policy.

The normal reader will prefer available HTML inside the main reading pane, with an
explicit plain-text toggle. Pop-out may be optional later. JavaScript engines and
script evaluation remain excluded from native Mail and received-message rendering.

M2 will evolve IHtmlPreviewHost into an embeddable adapter with attach/update/dispose,
viewport, scroll, zoom, input, and accessibility responsibilities. Keep the current
interface operational until a tested adapter replaces it. Scope requests/results to
MailMessageKey plus a request generation so late results cannot replace another
selection. Reuse shared UI/hosting contracts; do not introduce a full browser host.

The initial adapter is `IInlineHtmlPreviewHost`: it creates a reader-owned UI surface
on the UI thread. Windows reuses `ScrollableHtmlView` and its bounded tile cache,
native renderer and DPI provider. The reader disposes it synchronously on message
replacement. The previous window adapter remains for existing tests/measurement
tools. This first slice has no asynchronous rendering result or worker IPC.

Presentation does not establish a sandbox. The target renderer worker receives only
sanitized markup and approved bounded resources, never account secrets, profile
paths, or direct network capability. IPC needs versioning, size/time limits, worker
termination, and a usable plain-text fallback. OS restrictions and crash/hang/memory
containment require separate platform evidence. Retain the gate in
[html-renderer-security.md](../html-renderer-security.md) until that evidence exists.

Preserve CID scope, default denial of remote resources, explicit per-message remote
image permission through the controlled broker, and deliberate external links.
No replacement renderer may introduce a JavaScript runtime to solve hosting.
Native acceptance must cover fast switching, clipping, resize/DPI, focus, copy,
keyboard scrolling, long layouts, blocked resource paths, and worker failure.
