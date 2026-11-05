# Broiler.Net session/profile reuse review

Reviewed 2026-11-02 for M0. Result: **reuse HTTP session/policy infrastructure;
keep durable Mail profile/account/mailbox storage in Mail.** Broiler.Net already
implements useful sessions, but not a general persistent profile manager.

## Sources and consumed version

Inspected clean local Broiler.Net revision
`30ab0b26a70ef04ed87c766ce4d25f7b6dcf9793` and Broiler.Browser revision
`057cbd0f646011fa04a18b54d9fe176988fa08a9`. Mail's restored Windows graph already
contains Broiler.Net **0.1.0-preview.4** through its renderer dependencies. The new
development-only NetSessionProbe references that exact package and tests its
published APIs, rather than assuming the sibling checkout matches NuGet.
No Net package upgrade or Browser dependency was introduced into the application.

## What already exists

| Need | Available component | Fit for Mail |
| --- | --- | --- |
| Reusable HTTP connection pools, redirects, request policy, cancellation | `BrowserNetworkSession`, `BrowserNetworkSessionOptions`, `IBrowserRequestTransport`, `RequestContext.HopPolicy` | Reuse for a host-owned HTTP broker; do not write another cookie/redirect engine. |
| Separate HTTP cookie state | One supplied/default `CookieStore` per network session | Keep owners explicit. Same-origin requests from independent sessions do not share cookies. Sharing a store intentionally shares state. |
| Cookie policy, expiry, quotas, changes/revisions, administrative snapshots | `CookieStore` and related cookie/site APIs | Useful for an authenticated HTTP consumer, not a mailbox store. Snapshots include HttpOnly values and are sensitive. |
| Cookie-free remote email resources | `CookiesEnabled=false` plus `CredentialsMode.Omit` | Required for received-message image downloads. Never attach browser/OAuth/account cookies to messages. |
| Per-hop resource policy | `RequestContext.HopPolicy` | A Mail-owned broker supplies URL policy for the initial request and every redirect. Net does not define email permission, image quotas, private-address policy, or renderer containment. |
| Persistent profile directory/manifest, migration/backup/locking | Not implemented in Broiler.Net | Retain the M0/M1 Mail profile design and existing configuration stores. |
| Persistent cookies or HTTP response cache | Not implemented in the inspected Net component | Cookie snapshots are not a persistence/restore service. Fresh stores are empty. Do not use cookies as a substitute for account credentials. |
| IMAP/SMTP connections, account identities, UIDVALIDITY/checkpoints, durable mail/drafts/outbox | Not implemented in Broiler.Net | Keep Mail's protocol adapters, account IDs, protected credentials, and selected SQLite design. HTTP pooling does not pool IMAP/SMTP sessions. |
| OAuth authorization/token lifecycle | No complete sign-in/token manager here | Use the system-browser design and protected token storage when needed. A cookie engine alone is insufficient. |

The name BrowserNetworkSession and the presence of document.cookie/Fetch interfaces
do **not** imply a JavaScript engine. The package is BCL-only and the published
NativeAOT probe passes Mail's known-engine dependency gate. No script bindings are
needed in native Mail.

Broiler.Browser's `BrowserProfile` is an **internal application composition class**:
it owns CookieStore, BrowserNetworkSession, and a favorites path. Its source says
only favorites persist. It is a useful ownership reference, not a reusable durable
profile library. Do not import Browser.Core and its broader application graph to
obtain this small wrapper.

## Ownership decision for Mail

1. The Mail composition root owns one durable local profile, its account collection,
   cache/drafts/sync state, and platform credential adapter. Preserve AccountId
   values during migration. Neither Net session identity nor cookie partition keys
   replace `(AccountId, MailboxId, UIDVALIDITY, UID)`.
2. Receive/send jobs remain account-scoped and cancellable behind existing Mail
   contracts. If later authenticated HTTP APIs need sessions, own them separately
   per account/purpose and dispose/cancel them on disable/removal. Never use a
   process-global authenticated network session for multiple accounts.
3. Received-message resources use a separate cookie-free HTTP broker, activated only
   by explicit per-message permission. Reuse Net with cookies disabled, credentials
   omitted, bounded redirects, and Mail HopPolicy. Retain MIME/image verification,
   per-resource/aggregate byte and pixel limits, bounded body-read deadlines, and
   cancellation. Net's timeout covers headers/redirects, not the subsequent streamed
   response body. The renderer itself keeps its denying transport and receives only
   approved bytes, never an authenticated session.
   The broker must also construct requests without account Authorization headers or
   credential-bearing custom handlers; omitting cookies is not a general sanitizer
   for arbitrary caller-supplied HTTP credentials.
4. Do not treat session disposal as erasing a supplied cookie store. The host owns
   externally supplied stores and must clear/release them deliberately. Persisting
   cookies would require a separately designed protected format, schema, expiry,
   recovery and session-cookie policy; it is unnecessary for received mail images.

At the M0 inspection, Mail's pop-out preview used a static HttpClient for explicit
image downloads and a deny-all Broiler.Net transport for renderer-owned requests.
M2's [inline image broker](m2-remote-image-broker.md) now uses a fresh cookie-free
BrowserNetworkSession for explicitly consented images, with per-hop/public-IP policy,
bounded downloads, cancellation and surface-scoped results. The legacy static client
remains in the old pop-out measurement adapter. The M0 probe itself is still only
component evidence; durable Mail profiles remain separate from HTTP sessions.

## Verification and limits

`eng/Broiler.Mail.NetSessionProbe` compiles against NuGet preview.4 and is included
in `scripts/Confirm-M0Probes.ps1`, so the existing Windows x64/ARM64 and Linux x64
CI jobs now exercise it too. It uses a synthetic HttpMessageHandler, no live sites
or real cookies, and verifies:

- repeated requests retain the chosen profile's cookie, while a second session
  targeting the same origin sees none; HttpOnly stays out of document access;
- explicit reuse of a CookieStore preserves its memory state; a fresh store does
  not reload it; this alone is not a general proof of absence of disk I/O;
- CookiesEnabled=false and CredentialsMode.Omit suppress sending and storing;
- HopPolicy rejects an unapproved redirect before dispatch to that hop;
- host cancellation reaches requests, disposed sessions reject sends, and clearing
  a supplied store remains the host's responsibility.

Windows x64 managed and NativeAOT runs passed, with linker/AOT warnings treated as
errors and no JavaScript runtime in the resolved probe graph. See the retained
[result](evidence/m0-win-x64/net-session-results.json) and
[inventory](evidence/m0-win-x64/net-session-inventory.json). ARM64/Linux execution
results remain pending. This probe does not certify sockets/DNS, live HTTP,
OAuth, storage persistence, the proposed Mail broker, or an application sandbox.

Source references:

- [Net session implementation](https://github.com/Broiler-Platform/Broiler.Net/blob/30ab0b26a70ef04ed87c766ce4d25f7b6dcf9793/src/Broiler.Net/Http/BrowserNetworkSession.cs)
- [Session options and transport contracts](https://github.com/Broiler-Platform/Broiler.Net/blob/30ab0b26a70ef04ed87c766ce4d25f7b6dcf9793/src/Broiler.Net/Http/Transport.cs)
- [CookieStore](https://github.com/Broiler-Platform/Broiler.Net/blob/30ab0b26a70ef04ed87c766ce4d25f7b6dcf9793/src/Broiler.Net/Cookies/CookieStore.cs)
- [Net documented persistence/cache limits](https://github.com/Broiler-Platform/Broiler.Net/blob/30ab0b26a70ef04ed87c766ce4d25f7b6dcf9793/README.md)
- [Browser's internal profile composition](https://github.com/Broiler-Platform/Broiler.Browser/blob/057cbd0f646011fa04a18b54d9fe176988fa08a9/src/Broiler.Browser.Core/BrowserProfile.cs)
