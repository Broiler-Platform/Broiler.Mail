# Broiler.Mail roadmap

Current implementation and acceptance status for every roadmap is consolidated in
the [5 October status](roadmap-status-2026-10-05.md), which updates the
[4 October audit](roadmap-status-2026-10-04.md). The version scopes below remain
the product plan; historical component observations are not current package claims.

Status: version 1 implementation is complete and packaged for Windows x64.
Automated acceptance and native demo checks passed. The user deferred live-provider
testing to a later run; no public provider is claimed as certified. See the
[acceptance checklist](version-1-acceptance.md) and
[implementation notes](architecture.md). Version 2 now includes optional SMTP
account configuration, the plain-text composer, local draft recovery, and secure
SMTP submission with configurable Sent-copy handling. Updated: 2026-09-30.

Broiler.Mail should become a straightforward, responsive mail client that reuses
Broiler components where they fit. The first release should make one workflow work
well: configure an account, receive messages, and read them.

Version labels below describe product milestones, not a commitment to semantic
version numbers or release dates. Each milestone builds on the previous one and
ships when its acceptance criteria are met. Versions 1–3 preserve the requested
sequence; later milestones can be reprioritized after real use.

## Release overview

| Version | User outcome | Main additions |
| --- | --- | --- |
| **1 — Simple reader** | I can configure one account and read incoming mail. | Settings, account profile, manual receive, inbox, plain-text reading. |
| **2 — Send and preview** | I can write mail and read formatted messages. | SMTP, plain-text composition, replies, forwards, HTML preview. |
| **3 — Multiple accounts** | I can manage personal and work mail separately. | Account management, switching, independent status, sender selection. |
| **4 — Everyday mail** | I can organize mail and exchange files. | Folders, archive/trash, attachments, search, saved drafts. |
| **5 — Reliable sync and offline use** | My mail stays available through connection changes. | Background sync, local cache, offline reading, notifications. |
| **6 — Productivity** | I can find and handle mail faster. | Unified inbox, conversations, contacts, saved searches, rules, templates. |
| **7 — Wider availability** | I can use Broiler.Mail comfortably on supported platforms. | Additional providers, Linux, accessibility completion, distribution and performance. |

## Starting assumptions

- Start with a **Windows desktop application on .NET 10**, following the current
  Broiler.UI target framework. Keep the application core platform-neutral so Linux
  can follow. This is a proposed starting point, not a user-imposed restriction.
- Use **IMAP for receiving** and **SMTP for sending**. Version 1 supports one
  explicitly tested server/authentication combination with manual configuration.
- Password or app-password authentication is sufficient only where the chosen
  provider supports it. If the first target provider requires OAuth, implement its
  sign-in flow in version 1; protocol support alone is not a provider-support claim.
- Reuse existing components and pin tested versions. Build mail-specific behavior
  in Broiler.Mail, extracting shared libraries only after another consumer needs them.
- Keep storage local initially. Accounts, secrets, drafts, and future caches must
  have an account ID from the beginning, even with a single-account interface.

## Version 1 — Settings, account profile, receive mail

**Goal:** a small, usable reader for one inbox.

- [x] Build a Broiler.UI window with an inbox list, reading pane, refresh action,
  settings tab, and operation status.
- [x] Persist theme and initial window dimensions, apply them on restart, and report
  corrupt settings without resetting them silently.
- [x] Edit and persist one account profile with display name, email address, IMAP
  host, port, username, and TLS mode, with validation and stable identity.
- [x] Add password/app-password authentication and a cancellable IMAP connection
  test to the account workflow, with timeout and actionable failure states.
- [x] Store saved credentials in the operating system's protected credential store.
  Keep secrets out of ordinary settings and logs; validate server certificates.
- [x] Receive mail on manual refresh. Fetch the newest headers in bounded pages
  and retrieve message bodies on demand, with cancellation and useful errors.
- [x] Use simple text rows in the inbox; show sender, subject, date, and server
  read/unread status across the list and reading pane. Decode MIME, character sets,
  and multipart messages through a mail library.
- [x] Read plain text. For HTML-only messages, extract readable text without
  rendering active content or fetching external resources; label that fallback.
- [x] Validate empty inboxes, bad credentials, disconnections, malformed responses,
  restart, refresh, and recovery with automated IMAP/MIME and application tests.
- [x] Check native-window reading/forms and keyboard navigation; fix full-window
  text measurement, add clipboard support, default IME placement, and DPI awareness.
- [x] Provide a self-contained Windows x64 build, synthetic demo mode, and test guide.
- [ ] **Deferred by the user:** live-provider acceptance with a documented
  provider/authentication pair, including the user's display scaling and IME.
  This is a follow-up validation item; it does not block handing over the version 1 build.

The current reader loads 50 headers per page (up to 500 per session), limits raw
messages to 2 MiB including attachments, and labels text previews truncated at
32,000 characters. Mailbox membership changes require a fresh receive before
continuing to older pages. These are deliberate version 1 limits; see
[decision 0003](decisions/0003-read-only-receiving.md).

**Scope boundary:** inbox only, manual refresh, read-only server access, and an
in-memory message session. Settings and the account survive restart; offline mail,
attachments, sending, HTML rendering, and multiple accounts arrive later.

**Done when:** a fresh installation can save one profile, restart, connect, refresh,
and display plain-text and HTML-only test messages. Repeated refresh creates no
duplicate rows, and reading leaves server message flags unchanged. A failed
connection is recoverable without restarting the app.

## Version 2 — Sending mail and HTML preview

**Goal:** turn the reader into a basic two-way mail client.

- [x] Extend the account profile with SMTP host, port, TLS, and authentication.
  Optional SMTP setup now saves/reloads a separate host, port, username, TLS mode,
  and authentication method. Password/app-password is the available setup choice;
  separate SMTP credential storage and submission are implemented below. Connection
  testing remains IMAP-only. Existing IMAP-only profiles remain compatible.
- [x] Add a plain-text composer with To, Cc, Bcc, subject, and body; support reply,
  reply-all, and forward, including correct reply threading headers.
  The Compose tab reuses Broiler.UI.RichEdit, pins the sender, and preserves an
  existing draft until explicitly discarded. Parsed source headers supply
  Reply-To/From, visible recipients, Message-ID, In-Reply-To, and References.
  Reply-all excludes the account's address and received Bcc recipients. Check draft
  validates without sending. The active composition is now saved locally.
- [x] Preserve unfinished composition locally across failures and restart. Show
  sending, accepted-by-server, failed, and outcome-unknown states explicitly.
  Autosave retains raw edits, sender identity, and reply metadata. Failed saves
  keep edits open; normal closing waits for saving. Conflicting instances cannot
  overwrite newer saved work. Interrupted submission recovers as unknown with no
  automatic resend. Submission transitions are tested with injected senders and
  the controlled SMTP fixture.
- [x] Implement SMTP submission with separate protected SMTP credentials, required
  TLS, certificate validation, bounded operations, and MIME serialization that
  keeps Bcc out of delivered headers. Validate acceptance/rejection/unknown outcomes
  against a controlled server. MailKit submits plain-text MIME with stable message
  IDs and reply headers; rejected recipients abort before DATA. No automatic retry
  occurs after disconnect, timeout, or cancellation with uncertain acceptance.
- [ ] **Provider validation remains with the user:** verify SMTP sending and the
  provider's Sent-copy behavior using the [SMTP checklist](version-2-smtp-checklist.md).
- [x] Save sent mail according to the tested provider's behavior: use its automatic
  Sent copy or append a copy once. Treat a failed Sent-copy operation separately
  from delivery, so it never triggers a resend.
  The account explicitly chooses provider-managed copies or one IMAP append to an
  existing folder; older accounts default to unconfigured. SMTP acceptance is saved
  before the append attempt. Copy status survives restart; an interrupted append
  becomes unknown without retry. Controlled TLS/STARTTLS fixtures verify copying,
  permissions, missing folders, lost acknowledgements, and persistence failures.
  Live-provider policy selection/validation remains in the user checklist above.
- [x] Add an HTML reading mode with a plain-text toggle and bounded inline images.
  Resolve embedded `cid:` resources only within the current message; keep remote
  images blocked until the user chooses to load them.
  HTML reading mode opens through an in-process Broiler.HTML preview window with a plain-text
  toggle button and bounded embedded `cid:` resolution (up to 1 MiB per image,
  16 images maximum, raster types only). Remote images are blocked by default and
  can be explicitly loaded via a controlled HTTP channel that enforces non-SVG image
  verification and 5 MB size bounds.
- [ ] Complete process isolation for untrusted rendering; retain the implemented
  blocking of scripts, forms, frames, plugins, local
  file access, and automatic navigation. Control all resource-loading paths,
  including stylesheets, fonts, and CSS images. Open user-selected links externally.
  HTML markup is reduced to passive formatting with strict CSP (`default-src 'none'`).
  Native `Broiler.HTML` (`HtmlContainer` / `BBitmap`) is hosted inside a `Direct2DWindow`
  with zero script evaluation. All subresource requests are intercepted by a strict
  `DenyingRequestTransport` returning 403 Forbidden without network dispatch, plus
  `StylesheetLoad` and `ImageLoad` blocking handlers. External HTTP/HTTPS links open
  exclusively in the system browser. Handled render failures fall back to plain-text
  display. Renderer process isolation and containment of crashes,
  hangs, and memory exhaustion remain unimplemented; see the
  [renderer security gate](html-renderer-security.md) and the
  [current improvement audit](remaining-improvements-2026-10-02.md).

**Scope boundary:** HTML is for received-message preview. A rich HTML composer,
attachment sending, scheduled sending, and automatic send retries are deferred.

**Done when:** test messages can be sent and received, replies retain their thread
headers, and Bcc recipients are absent from delivered headers. Failed sends retain
the draft. A timeout after possible SMTP acceptance does not cause an automatic
resend. Preview fixtures cannot execute code or access files/network resources
outside the explicit content policy, and a renderer failure leaves the app usable.

**Broiler integration gate:** Broiler's published [security policy](https://github.com/Broiler-Platform/Broiler.JS/security/policy)
states that its rendering paths are not hardened for hostile content and that the
engine is not a sandbox. Sanitization alone is insufficient. Evaluate Broiler.HTML
behind a restricted renderer process with no account credentials, arbitrary file
access, or direct network access. If that boundary cannot be demonstrated, use a
maintained sandboxed renderer through the same adapter for this milestone. HTML
preview remains required for version 2; a safe renderer is a release dependency.

## Version 3 — Multiple accounts

**Goal:** manage several accounts without mixing their data or sender identities.

- [ ] Add, edit, disable, remove, and switch accounts, with clear names and colors.
- [ ] Keep each account's credentials, settings, drafts, connections, and messages
  separate. Give each account its own refresh progress and error state.
- [ ] Show the sending account clearly. Replies default to the receiving account;
  changing the selected inbox must not silently change an existing draft's sender.
- [ ] Explain that account removal removes the chosen local data and credentials,
  not the remote mailbox. Resolve unsent drafts before removal.

**Done when:** two accounts can receive and send independently, including when one
is offline. Identical IMAP UIDs in different accounts or folders never collide.
Switching accounts during a refresh cannot display results in the wrong inbox.

## Version 4 — Everyday mailbox management

**Goal:** handle routine mail and files without another client.

- [ ] Browse server folders; mark read/unread, star, move, archive, and move to
  trash. Make destructive actions explicit and support undo where feasible.
- [ ] Download attachments to a chosen location and attach files to outgoing mail,
  with safe filenames, size limits, progress, and no automatic execution.
- [ ] Add search by sender, subject, date, and unread status. Clearly distinguish
  searching loaded messages from a server search.
- [ ] Manage multiple saved local drafts, add a simple signature per account, and
  map provider-specific Drafts, Sent, Archive, and Trash folders where needed.

**Done when:** folder actions reconcile with the server, failures remain visible,
attachments survive a send/receive round trip, search scope is clear, and drafts
survive restart. Operations always target the intended account and folder.

## Version 5 — Synchronization and offline use

**Goal:** stay useful on unreliable networks and with larger mailboxes.

- [ ] Introduce a versioned local message cache with storage limits, selectable
  offline folders, and visible download/staleness status.
- [ ] Add periodic refresh, incremental synchronization, and IMAP IDLE where
  supported, with reconnect backoff and cancellation.
- [ ] Reconcile server deletions, flag changes, and mailbox identity changes;
  recover interrupted synchronization without duplicate messages.
- [ ] Read cached messages and write drafts offline. Add a visible queue for
  supported pending changes, with defined conflict handling and deliberate sending.
- [ ] Offer per-account notifications, quiet hours, and private notification text.
  Suppress repeated notifications after reconnects and restarts.

**Done when:** an offline/reconnect cycle preserves drafts and reconciles mail
correctly, cache migrations preserve data, and disk limits are enforced. Pending
changes and uncertain sends remain visible rather than being silently discarded
or retried. Large-inbox interaction remains responsive within a documented budget.

## Version 6 — Organization and productivity

**Goal:** reduce the effort of processing a busy inbox.

- [ ] Offer an optional unified inbox with persistent account identity indicators.
- [ ] Group conversations using message reference headers, not subject alone.
- [ ] Add a local address book and recipient completion.
- [ ] Add saved searches, tags, templates, and configurable keyboard shortcuts.
- [ ] Add opt-in rules for filing/tagging and snooze/reminder workflows. Clearly
  state which actions require Broiler.Mail to remain running.
- [ ] Evaluate rich HTML composition through Broiler.UI.RichEdit, retaining a
  plain-text alternative and testing the resulting MIME and HTML interoperability.

**Done when:** unified views preserve account boundaries, conversation grouping
avoids unrelated mail, and rules expose what they will do before first use.
Common reading and composition workflows are efficient with the keyboard.

## Version 7 — Provider coverage, platforms, and release polish

**Goal:** expand access after the core workflow is dependable.

- [ ] Expand a documented provider/authentication matrix, including OAuth sign-in,
  refresh, revocation, and reauthorization for selected providers. Bring any
  required provider integration forward to the milestone that first needs it.
- [ ] Add Linux and Android through the existing Broiler host patterns (`Broiler.Graphics.Linux`, `Broiler.Native.Android`), including secure credential storage (Secret Service / Keystore), mobile adaptations, and platform-specific integration tests. See the [Cross-Platform Roadmap](cross-platform-roadmap.md).
- [ ] Complete screen-reader integration, high-contrast support, localization,
  right-to-left text, DPI scaling, and input-method testing. Basic labels, focus
  order, keyboard operation, and readable contrast are requirements from version 1.
- [ ] Package releases with an upgrade path, redacted diagnostics, and recovery
  guidance. Add installation/update signing where the distribution requires it.
- [ ] Profile large mailboxes, rendering, startup, and memory use; close the
  component-level accessibility or performance gaps found during application tests.

**Done when:** each advertised platform and provider passes the same essential
mail workflows, upgrades preserve profiles and drafts, and documented performance
and accessibility checks pass on real systems.

## Component reuse and gaps

These opportunities were checked against the available local component READMEs and
source. Links point to their upstream repositories; capability is not a claim that
an integration has already been implemented or validated in Broiler.Mail.

| Component | Proposed use | Boundary or gap |
| --- | --- | --- |
| [Broiler.UI](https://github.com/Broiler-Platform/Broiler.UI) | Window, dialogs, lists, splitters, text fields, menus, themes, and later folder trees. | Use the required control `.Standard` packages. ListView currently has simple text items; richer inbox rows need additional work. Platform hosting remains in Mail. |
| [Broiler.Graphics](https://github.com/Broiler-Platform/Broiler.Graphics), [Broiler.Input](https://github.com/Broiler-Platform/Broiler.Input), [Broiler.Native](https://github.com/Broiler-Platform/Broiler.Native) | Compose the graphics backend, input, and native window host used by Broiler.UI. | Follow the UI sample host; native bindings are not an existing account or credential service. |
| [Broiler.HTML](https://github.com/Broiler-Platform/Broiler.HTML), [Broiler.CSS](https://github.com/Broiler-Platform/Broiler.CSS), [Broiler.DOM](https://github.com/Broiler-Platform/Broiler.DOM) | Candidate HTML/CSS reading pipeline for version 2. | Requires the isolation and resource-policy gate above; do not enable JavaScript for email. |
| [Broiler.HtmlBridge](https://github.com/Broiler-Platform/Broiler.HtmlBridge), [Broiler.Browser](https://github.com/Broiler-Platform/Broiler.Browser) | Reference existing renderer/host integration patterns. | Do not assume a ready-made embeddable email viewer or import full browser sessions into message previews. |
| [Broiler.Net](https://github.com/Broiler-Platform/Broiler.Net) | Evaluate its policy-aware HTTP transport for explicitly allowed remote preview resources. | Its current scope is HTTP, cookies, and site policy; it does not supply IMAP, SMTP, or mail MIME handling. |
| [Broiler.Writer](https://github.com/Broiler-Platform/Broiler.Writer), [Broiler.Documents](https://github.com/Broiler-Platform/Broiler.Documents) | Reference platform composition; later evaluate export/printing or rich-text integration. | Avoid a word-processor dependency in the first releases. Rich text still needs email-specific serialization. |

For the mail-protocol gap, evaluate [MailKit](https://github.com/jstedfast/MailKit)
for IMAP/SMTP and [MimeKit](https://github.com/jstedfast/MimeKit) for MIME parsing
and creation, behind Mail-owned adapters. Their upstream documentation describes
these capabilities; version, licensing, runtime, and packaging compatibility must
be verified during the initial integration.

Use standard `DateTimeOffset`/`TimeZoneInfo` and mail-library date parsing for email.
[Broiler.DateTime](https://github.com/Broiler-Platform/Broiler.DateTime) addresses
expanded ISO dates with fixed offsets; it is not needed for ordinary mail dates
or local time-zone conversion.

## Architecture that supports the sequence

Keep four clear responsibilities, initially as a few projects or folders:

- **Application/UI:** Broiler.UI screens and presentation state.
- **Core:** account profiles, mail models, use cases, and small service interfaces.
- **Infrastructure:** mail protocols, MIME, settings, credentials, and later storage.
- **Platform host:** native window/input/graphics integration and OS services.

Keep HTML preview behind its own boundary when introduced in version 2. The
renderer receives only the selected message and approved resources, never account
secrets. Settings, credential storage, transport, and rendering should be
replaceable without rewriting screens.

Use `(AccountId, MailboxId, UIDVALIDITY, UID)` for IMAP message identity; neither
the message's `Message-ID` header nor an IMAP sequence number is a sufficient cache
key. Keep network and parsing work cancellable and off the UI thread. Bound message
sizes and resource use from version 1. Use synthetic or sanitized mail fixtures
for tests, and exclude credentials and message bodies from diagnostics by default.

## First implementation steps

1. Record the initial platform, provider/authentication pair, dependency versions,
   and package-consumption approach in a short architecture decision.
2. Prove a minimal Broiler.UI host with a settings form, inbox list, and text pane.
3. Implement profile persistence and the platform credential adapter.
4. Connect to a test mailbox through the protocol adapter and display one decoded
   message, then add paging, refresh, cancellation, and failure states.
5. Validate the version 1 acceptance criteria with MIME fixtures and a controlled
   IMAP server; provide a reproducible build and a runnable preview artifact.
6. Investigate the version 2 renderer boundary before committing to its integration.

## Ideas beyond the core roadmap

Consider these independently after usage establishes demand: `.eml` import/export,
mailbox backup/export, calendar invitations and contact synchronization, S/MIME or
OpenPGP with explicit key management, scheduled sending, advanced server search,
and additional protocols such as JMAP. Mobile and browser editions need separate
assessment of background execution, credential handling, and protocol access.
None should delay the first three milestones.
