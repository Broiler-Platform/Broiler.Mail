# M2 controlled remote-image broker

Updated 2026-11-04. The broker is connected to Windows' inline reader with explicit
per-message consent. Remote images remain blocked by default.

The reader explains that loading contacts external servers and may reveal that the
message was opened. **Load remote images** starts a batch; **Cancel image loading**
stops it. A partial/failed result offers **Retry unavailable images**, retaining
approved images from earlier completed batches without contacting their URLs again.
Switching messages/accounts, disposing the reader, or choosing Plain text cancels
an outstanding batch. Queued completions check the surface generation and lifetime.
Canceled in-progress batches are not committed; previously completed batches remain.
**Block remote images again** explicitly clears completed approvals as well as
canceling pending requests. It rebuilds blocked markup from the original message,
retains original embedded images and reader settings, and rejects queued/late results.
Loading again requires a fresh click and refetches the remote images. Previously
sent requests cannot be undone; clearing permission does not promise secure erasure
of process/OS memory. See [three reset items](m2-remote-image-reset.md).
No sender-wide permission is offered, and reopening a message starts blocked again.

`RemoteImageBroker` takes only bounded raw markup and decoded inline-image metadata.
It extracts candidate image URLs through the existing mail policy. It does not take
an account, profile path, credential store or sender-wide permission. Constructing
the broker performs no request; a caller must explicitly invoke its batch operation.

## Network boundary

The broker reuses Broiler.Net's `BrowserNetworkSession` with cookies disabled,
credentials omitted, and a per-hop policy. A fresh handler has no credentials,
referrer, cookie container, application headers or proxy configuration. HTTPS on
port 443 is required; userinfo, private/local destinations, and unqualified local
names are refused. Redirects are checked before dispatch and limited to three.

Production uses SocketsHttpHandler with automatic redirects/cookies/proxies disabled.
Its connection callback resolves DNS, rejects an empty/oversized answer or any
non-public address in a mixed answer, then connects directly to an approved IP.
There is no second hostname lookup between validation and connection. Standard TLS
hostname/certificate validation stays enabled. The IP policy is conservative: IPv4
special/private ranges and IPv6 outside the selected global-unicast ranges are
blocked; 2001/16 and 6to4 are excluded even where some addresses could be legitimate.

## Resource limits and output

- At most 16 distinct remote images per batch; reject larger sets before requests.
- 10-second request timeout, 5-second connection timeout and 30-second batch deadline.
  Caller cancellation reaches requests and body reads.
- Per-image encoded payload limit: 1 MiB, checked by declared length and streamed
  reads. A rejected stream can consume one additional 8 KiB read. Accepted image
  bytes, including the supplied inline images, are limited to 2 MiB.
- Header inspection and an aggregate 16-million-pixel ceiling precede returning
  PNG/JPEG data to the current renderer. This is not full decoding or containment.
- Response-body reads, including failed images, share a separate 2 MiB batch budget
  with at most one extra 8 KiB read. Remaining candidates are refused without a new
  request once it is exhausted. Unread response draining is disabled. This is an
  application payload-read budget, not accounting for TLS/HTTP headers or transport
  buffering. Each explicit retry starts a new bounded batch.

The broker passes only approved data images into the returned reduced markup.
Unresolved remote images become placeholders, and the policy excludes remote image
sources from its CSP. Message-supplied dictionary keys naming remote URLs cannot
masquerade as approved downloads. The renderer's deny-all transport remains active.
No permission or downloaded remote image is persisted by this component.

## Validation and remaining work

Tests use synthetic handlers and a production-handler DNS guard test. They verify cookie/header suppression, private
redirect rejection, HTTPS/port/userinfo/IP/local-name policy, mixed DNS answer
rejection, address aliases, count/declared-byte limits, cancellation, and untrusted
remote-key rejection. No-length streaming failures exercise cumulative body limits.
UI/session tests cover explicit start, cancellation, partial retries without refetching
approved URLs, reader disposal, queued-result rejection, focus and permission reset.
The published smoke exercises the actual broker, policy and inline consent state
with a valid PNG and a blocked private redirect. These deterministic tests contact no public server.

The production socket-handler test supplies controlled mixed public/private DNS
answers and verifies rejection before connection. A separate explicitly invoked NativeAOT
[public HTTPS diagnostic](evidence/m2-production-https-win-x64/README.md) now validates a real PNG download,
public DNS and default TLS authentication, plus expired, wrong-host and self-signed endpoint rejection
on Windows x64. It has fixed destinations and accepts no mailbox/profile input, custom resolver,
certificate callback or trust override. It is outside application startup and CI. Broader delivered-adapter
resource-path and other-target acceptance remain open. Restricted-process image decoding remains part of the
[renderer security gate](html-renderer-security.md).
