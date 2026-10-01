# HTML renderer boundary and acceptance specification

Status: Phase 0 design, 2026-09-30. This specifies required behavior; process isolation is not implemented by the current Windows preview. It supersedes any claim that a separate window/thread or content policy alone is a sandbox. The existing native preview tests check behavior of selected fixtures, not containment of a compromised renderer.

## Threat model and trust boundary

Treat message HTML/CSS, image/font data, malformed MIME-derived assets, parser state, and every renderer response as attacker-controlled. Assume a rendering bug can execute code in the renderer. Assets to protect are saved credentials, account/draft data, unrelated messages, files, network endpoints, and availability of the mail UI. Resource denial and sanitization remain required but do not establish containment.

The mail process owns credentials, provider connections, persisted data, selection state, the native shell, and explicit user decisions. A broker passes only the currently selected preview document and bounded approved assets to a restricted renderer. Never pass `MailMessageBody` wholesale, account/profile identifiers, authentication material, other message bodies, paths, or inherited application handles. IPC uses an opaque per-preview identifier and generation counter, not an account or IMAP identifier.

The renderer starts restricted, before it receives untrusted content. It has no credential access, arbitrary filesystem access, general network access (including loopback/DNS), child-process creation, shell execution, clipboard, or direct external navigation. Avoid writable persistent renderer profiles; allow only explicitly required, read-only runtime/font resources and narrowly scoped ephemeral storage. Parent-level Flatpak restrictions do not separate renderer privileges from mail privileges.

Platform implementations must demonstrate these restrictions using OS mechanisms. Candidate mechanisms include a restricted Windows AppContainer with process/job resource controls, a Linux sandbox with filesystem/network/process restrictions, or a maintained sandboxed renderer on the platform. These are design candidates, not completed security claims. If a platform cannot enforce the boundary, use a maintained sandboxed renderer or keep HTML unavailable there.

## Broker and IPC protocol

- Use an authenticated/private local channel created by the parent, with only the intended endpoint inherited/passed. Do not open a listening TCP port. Deny other processes/users attachment and close unused handles.
- Version every frame. Validate type, declared length, dimensions, identifiers, and generation before allocating/reading payloads. Reject unknown commands, mismatched versions, unexpected order, duplicate completion, and oversized payloads by closing the channel and terminating the renderer.
- Parent messages: initialize with protocol/version and viewport limits; load a bounded document plus opaque asset IDs; resize within limits; cancel/close. Renderer responses: ready; bounded frame; bounded link geometry/opaque link ID; completion; a small non-sensitive error code. No arbitrary object deserialization, filesystem path, URI fetch command, or arbitrary callback execution.
- Render into broker-owned or validated bounded pixel buffers. Validate stride, dimensions, multiplication overflow, format, and buffer length before display; reject stale/out-of-generation frames and link geometry. Do not decode renderer-controlled compressed images in the privileged mail process.
- Keep the original validated link map in the broker. External navigation requires a real user action observed by the trusted host, the current generation, an approved HTTP(S) target from that map, and confirmation showing the destination. A renderer event alone cannot launch a browser or trigger a download.
- Remote images remain blocked by default. Only the trusted host may authorize a bounded broker fetch; the renderer cannot nominate new destinations. Require HTTPS or the explicit application policy, validate destinations and redirects against local/private/link-local networks, set time/size/redirect limits, and send no cookies or credentials. Pass bounded encoded bytes into the restricted process for decoding, never an unrestricted network capability. An image fetch failure leaves plain text usable.

## Initial enforced budgets

These are proposed hard ceilings for the first restricted implementation. Implement and test them before enabling the adapter; document any deliberate revision. Apply the smaller of existing mail policy limits and these limits.

| Resource | Initial ceiling / behavior |
| --- | --- |
| HTML | Current policy maximum 1,280,000 UTF-16 code units and 5 MiB UTF-8 document payload; validate both. |
| Inline images | Current limits: 16 assets, 1 MiB each, 2 MiB total encoded data per document. |
| IPC control/document frame | 8 MiB including metadata/assets; reject the header before allocating a larger buffer. |
| Decoded image | 16 megapixels per image, 64 MiB total decoded asset memory; reject decompression bombs. |
| Render tile | At most 2048 by 2048 RGBA pixels, 16 MiB per tile, at most two outstanding tiles. Use separate bounded pixel transport, not the 8 MiB control frame. |
| Remote fetch | 5 MiB per response, 10 MiB total per preview, at most 16 responses and 3 redirects each, 10 seconds per fetch; no automatic retry. |
| Renderer | One active renderer per preview session; 256 MiB process memory ceiling, 10 seconds startup, 5 seconds per layout/tile request, 2 seconds to exit after cancellation. Parent forcibly terminates on overrun. |

Wall-clock deadlines and OS-enforced memory/process limits must work even if the renderer's event loop is hung. Limit accumulated responses, queues, and total decoded allocations, not only individual messages. Smaller-device budgets may be lower. Oversized content gets a clear failure with the existing plain-text fallback; never silently remove a limit to display it.

## Lifecycle and failure behavior

Account/selection changes, window closure, and disposal invalidate the generation, cancel pending broker fetches, and close/terminate the renderer. Late messages cannot update another message's preview. A renderer crash, malformed IPC, startup failure, or resource violation closes its handles and discards ephemeral state, while the mail UI and draft remain usable. Show the plain-text fallback and allow an explicit fresh preview attempt; do not automatically restart a crashing document in a loop. Keep diagnostic codes/version/timing only, without HTML, addresses, URLs, credentials, or document contents.

## Required acceptance matrix

Each OS/architecture/backend intended for release must pass the following against the actual packaged adapter. Use synthetic credentials/files and controlled local listeners; no real mailbox secrets or external recipients are required.

1. **OS containment independent of sanitization:** a purpose-built child launched with the same restrictions attempts arbitrary file reads/writes, credential access, DNS/TCP/UDP/loopback access, child creation, clipboard access, and browser launch. Assert OS denial, not merely absence of a request in a sanitized fixture. Verify only the intended runtime/font resources are readable.
2. **Untrusted rendering:** hostile scripts, event handlers, CSS imports/fonts/URLs, frames, redirects, SVG and malformed/oversized image fixtures cannot exceed policy or reach file/network sentinels. Confirm readable benign content still renders.
3. **Protocol validation:** oversized/truncated frames, invalid lengths/strides, arithmetic overflow, unknown commands, version mismatch, stale generations, and response floods cause bounded termination. Assert no broker allocation follows an unchecked length and no stale frame/link becomes visible.
4. **Navigation and remote consent:** forged renderer clicks, unapproved targets, stale link IDs, redirects to private networks, image redirects, cookies, and authentication challenges do not grant privileges. A real host-observed action can open only the approved target; default image loading produces no network traffic.
5. **Availability:** crash/kill the renderer, hang startup/layout, exhaust its memory, cancel during a fetch, and close/switch accounts repeatedly. Check time/memory budgets, child/handle cleanup, responsive UI, intact drafts, and readable plain text.
6. **Packaged execution:** repeat containment and lifecycle checks for each packaged platform, including Flatpak/Android where applicable. Record OS, architecture, sandbox configuration, engine/package versions, revision, pass/fail/skip counts, and evidence.

Missing or skipped containment checks block HTML-enabled release on that target. Current Windows tests remain useful regressions but do not satisfy item 1. The production adapters must not claim this specification is implemented until that evidence exists.
