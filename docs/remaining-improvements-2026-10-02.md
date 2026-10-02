# Remaining improvements after Hosting adoption

Reviewed **2 October 2026**, against Mail commit `6bdcb83` and the pinned NuGet
packages. This is a source and acceptance-evidence audit, not a new runtime test
run. The previous package validation passed 256 tests and exercised the Windows
x64 NativeAOT demo; its limitations still apply. See [the evidence](hosting-packages.md).

The next work should finish native acceptance and improve the actual Mail flows.
Much of the shared control implementation already exists; rebuilding those controls
would duplicate completed work.

UI implementation is now split into the [dedicated UI roadmap](ui-implementation-roadmap.md),
with UI-01 through UI-14 tasks, ownership, dependencies, source entry points, and
acceptance criteria. This audit remains the cross-cutting overview, including
renderer isolation, NativeAOT, product features, and platform prerequisites.

## What is already delivered

| Earlier work | Current evidence | What remains |
| --- | --- | --- |
| C-01 / EX-02: structured inbox rows | `MailMessageItemPresenter` renders sender, subject, date, and unread state through the two-line presenter | Narrow-window polish, optional snippets, refresh continuity |
| C-02: adjustable panes | `InboxView` uses `StandardSplitContainer` and binds the splitter fraction | Responsive single-pane reading and native scaling acceptance |
| C-03 / EX-03: selectable reading text | `ScrollableMessageText` uses a read-only RichEdit with wrapping and scrolling | Header hierarchy, reading margins, local reply actions |
| C-04 / EX-04 / EX-08: forms and feedback | Composer/account/settings use `FormSurface`; Cc/Bcc and Sent settings collapse; validation/status are structured | A less form-like composer, guided setup, native accessibility/text-scale acceptance |
| H-03: common platform hosting | Windows/Linux consume Hosting preview.1 from NuGet; Android is in the dependency probe | Actual Linux and Android Mail applications and device acceptance |
| NativeAOT compilation | Windows project, publish script, and folder profile enable AOT; x64 publishing and headless rendering succeeded | Broader native execution coverage and removal of the HTML compatibility substitution |
| HTML rendering improvements | Layout snapshots, visible tiles/LRU, resource outcomes, cancellation, remote-image controls | Process containment, byte budgets, performance measurement |

## Prioritized backlog

P0 denotes an existing release gate. P1 improves current functionality or the
strength of its verification. P2 adds larger product/platform capabilities.

### P0 — A1: make native UI Automation expose the controls

**Owner:** Broiler.Hosting.Windows, with a Mail consumer test. **Status:** root cause found and
fixed in Mail (bridges were constructed before the native windows existed, with zero handles); the
tree is now exposed natively, including the published NativeAOT build. With Broiler.UI
0.1.0-preview.12 and Broiler.Hosting 0.1.0-preview.3, names, labels, the Text pattern, and hidden
tab content pass the external-client check; a real screen-reader pass remains. See UI-09 in the
[UI roadmap](ui-implementation-roadmap.md).
The investigation below records the earlier state.

The external inspection of the published NativeAOT demo saw the window and render
pane but no Inbox, recipient, or Send controls. Managed peer tests cannot establish
cross-process COM discovery. Explicit COM initialization did not resolve it.

Investigate in this order:

1. Capture `WM_GETOBJECT` routing to the render HWND, successful subclass attachment,
   the value returned by `UiaReturnRawElementProvider`, and host-provider HRESULTs.
2. Query the published provider through its native interfaces. Check interface IDs,
   independent fragment vtables, VARIANT/BSTR/SAFEARRAY ownership, runtime identity,
   disposal, and calls from a client thread.
3. Compare Raw, Control, and Content trees. The unhandled `IsControlElement` and
   `IsContentElement` properties deserve explicit mappings, but they are **not a
   confirmed explanation**: Microsoft's documented default for both is true.
4. Verify focus/selection/live-region events, virtual item identity after a refresh,
   password protection, labels, and text selection support with a real screen reader.

**Done when:** an external client can find Inbox/To/Send by name and role, navigate
and invoke controls without coordinates, and observe selection and validation
announcements in the published executable. Add this test to Windows package CI.
Reopen H-01 acceptance until that passes.

Reference: [Microsoft's UIA property definitions](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-automation-element-propids).

### P0 — A2: implement the HTML renderer process boundary

**Owner:** Mail preview host/broker, with shared HTML/Hosting mechanisms where useful.
**Status:** specified, not implemented.

[`WindowsHtmlPreviewHost`](../src/Broiler.Mail.Windows/Preview/WindowsHtmlPreviewHost.cs)
starts a thread in the mail process. Sanitization, deny-by-default loading, and
caught exceptions are useful, but a separate window/thread cannot contain a native
crash, runaway allocation, or compromised renderer. This gate already exists in
the project's [renderer security specification](html-renderer-security.md).

Implement restricted renderer startup, bounded/versioned IPC, an image-fetch broker,
no credential access, process memory/CPU limits, cancellation/termination, and stale
generation rejection. Preserve plain text and the active draft after renderer failure.
Keep HTML integration into the main reading pane behind this boundary.

**Done when:** automated fixtures demonstrate denied file/network/credential access,
bounded malformed input and images, enforced timeouts, and a usable mail shell after
forced renderer termination. Ordinary fixture rendering alone is insufficient.

### P1 — A3: preserve reading context during Receive

**Owner:** Mail. **Status:** implemented as UI-02 (see the [UI roadmap](ui-implementation-roadmap.md));
native new-mail anchoring check pending. The description below records the earlier behavior.

[`InboxViewModel.LoadPageAsync`](../src/Broiler.Mail.Application/ViewModels/InboxViewModel.cs)
sets `SelectedMessage` and `Body` to null after every successful newest-page fetch.
The view replaces list items and closes the HTML preview when the body changes.

Reconcile by full message identity, preserve the selected body when still valid,
and keep list/body scroll anchors. Specify what happens when a message disappears,
falls outside the newest page, or UIDVALIDITY/account changes. Preserve the existing
rule that failed refresh retains the old inbox, and keep stale async results excluded.

**Done when:** refresh during reading does not jump to the top or clear a still-valid
message; deletion and identity changes produce an explicit state. Cover successful
refresh, reordered/new messages, removal, empty results, failure, and stale completion.

### P1 — A4: finish the reader and reply flow

**Owner:** Mail, using the existing UI controls. **Status:** reader header and local reply
actions implemented as UI-03; compact single-pane mode remains (UI-04). The text below records the earlier state.

[`InboxView`](../src/Broiler.Mail.Application/Views/InboxView.cs) still puts subject,
sender, date, and server flags into one label. Reply actions exist only in Compose.
The body is selectable now, so that part of C-03 need not be repeated.

Add reading margins, a distinct subject heading, quieter/selectable sender details,
and Reply/Reply all/Forward beside the message. Connect these to the existing draft
state machine, switch to Compose, and focus the appropriate field without discarding
an existing draft. Add a compact single-pane mode for narrow windows.

**Done when:** reading and replying require no unrelated tab detour; long subjects,
addresses, 200% text scale, and narrow layouts remain readable and operable.

### P1 — A5: apply appearance and window preferences live

**Owner:** Mail integration plus Hosting/UI appearance contracts. **Status:** partial;
EX-09 remains open.

The host detects settings-change messages, but Mail selects its paint theme at startup.
[`SettingsViewModel`](../src/Broiler.Mail.Application/ViewModels/SettingsViewModel.cs)
still promises application after restart; shell settings changes update the footer.
Window resizing updates the viewport but does not save actual geometry.

Wire saved appearance to the live theme controller; follow system changes only in
System mode. Propagate tokens to existing controls and preview windows. Verify actual
high-contrast colors, system text scale, reduced motion, focus visibility, and RTL.
Debounce geometry persistence and recover windows to an available monitor.

**Done when:** changing theme needs no restart, System mode follows OS appearance,
an explicit theme stays selected, enlarged text does not hide actions, and geometry
survives reopening without placing the app off-screen.

### P1 — A6: finish the composer and setup ergonomics

**Owner:** Mail. **Status:** partial, building on C-04 rather than replacing it.

The composer already has persistent submission actions and optional Cc/Bcc. Its body
still requests a fixed 300-DIP height beneath several instruction rows. Make writing
occupy the available space, reduce repeated instructions, retain one clear scrolling
owner, and provide deliberate focus on New/Reply. Keep the separate submission,
Sent-copy, and storage outcomes; they prevent unsafe resend assumptions.

Account setup is grouped now, but still requires users to understand server settings,
separate password saves, and provider Sent-copy policy. Add a guided first-account
path, concise field help, and an SMTP connection/authentication test that sends no mail.
Retain access to advanced settings and explicit credential binding.

**Done when:** keyboard-only setup and composition work at minimum size and enlarged
text; actions and feedback remain visible; collapsing fields preserves their values;
draft recovery and uncertain-send behavior remain intact.

### P1 — A7: strengthen NativeAOT runtime evidence

**Owner:** Mail CI and Broiler.HTML.Image. **Status:** native build works; test scope is narrow.

[`ShellSmokeCheck`](../src/Broiler.Mail.Windows/Hosting/ShellSmokeCheck.cs) renders four
tabs headlessly. It does not exercise external COM, persisted-data round trips,
credential-key compatibility, HTML painting, or mail transport in the native binary.

Add an isolated executable acceptance mode with generated-JSON save/reload fixtures,
legacy credential-binding golden values, offline HTML/image rendering, and controlled
local IMAP/SMTP fixtures. Use synthetic data and never a developer's account store.
Run the published binary on x64 and ARM64 runners; record actual per-platform results.

Replace the version-specific [`NativeAot.Substitutions.xml`](../src/Broiler.Mail.Windows/NativeAot.Substitutions.xml)
with upstream typed backend registration/canvas calls. Until that is published, guard
the workaround against unreviewed renderer-version changes and verify pixel/geometry
equivalence between managed and AOT rendering.

**Done when:** the published executable passes these paths with reflection-based JSON
disabled and no new trimming/AOT warnings. A successful build does not establish every
runtime path. See [the NativeAOT deployment guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).

### P1 — A8: measure smoothness and budget renderer memory in bytes

**Owner:** Mail instrumentation; optimize shared Graphics/UI/HTML only where measured.
**Status:** layout timing exists; a reproducible end-to-end performance baseline is missing.

Measure startup-to-interactive, input-to-paint, frame p50/p95/p99, allocations, working
set, and tile misses for a 500-row inbox, long message, large draft, resize, and HTML
scrolling at 100/150/200% DPI. Use fixed fixtures and record machine, build, and DPI.
Treat any frame budget as a target until measured; do not promise smoothness from test counts.

[`HtmlViewElement`](../src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs) bounds its
cache to 16 tiles, but memory also grows with width and DPI. As an illustrative raw
RGBA estimate, sixteen 1000-by-1024 DIP tiles at 2x scale total about 250 MiB, before
temporary images and encoding buffers. This is arithmetic, not measured memory use.
Add pixel/byte budgets as well as tile-count limits. Profile the existing bitmap →
PNG encode → renderer image creation path before considering a direct pixel upload API.

**Done when:** a repeatable report identifies the dominant costs; targeted changes
improve those measurements without stale layout, broken selection, or lost invalidations.

## Larger work after the current experience is dependable

| Priority | Extension | Main missing pieces |
| --- | --- | --- |
| P2 | Everyday mailbox operations | Folder navigation, mark read/unread, move/archive/trash, attachments, search, multiple drafts |
| P2 | Multiple accounts | Account-scoped state, credentials, drafts, connections, sender identity, safe removal |
| P2 | Provider coverage | OAuth and a recorded provider/authentication/Sent-copy matrix; live-provider checks remain user-owned |
| P2 | Linux Mail | The current entry point only offers diagnostics. Build the interactive shell, desktop text/IME input, Secret Service storage, preview boundary, packaging, and native acceptance |
| P2 | Android Mail | Hosting package availability is only a prerequisite. Add an application head, Keystore storage, lifecycle/draft recovery, mobile layout, keyboard/back behavior, and device tests |
| P2 | Shipping and maintenance | Signed releases, upgrade/recovery behavior, content-free diagnostics, and clean-machine acceptance |

Follow the existing [product roadmap](roadmap.md) and [platform plan](cross-platform-roadmap.md)
for dependency order. Adding a Hosting package does not complete a platform port.

## Recommended delivery order

1. Diagnose A1 and add the external-client regression before declaring H-01 complete.
2. Deliver A3 and A4 together: preserve reading context, improve the reader, and expose
   local reply actions. This is the clearest next visible improvement.
3. Complete A5 and A6 with text-scale and keyboard acceptance, reusing the shipped controls.
4. Begin A7 and the A8 measurements early so each subsequent change has useful evidence.
5. Deliver A2 before treating HTML-enabled version 2 as release-ready. It is a separate
   substantial workstream, not a final cosmetic task.
6. Expand mailbox features and platforms once these foundations meet their acceptance criteria.

## Roadmap and package hygiene

The older experience table predates the new controls. Treat the delivered/remaining
split above as the current audit rather than interpreting every unchecked row as
unstarted work. H-01's previous Completed heading contradicted the native evidence.
The main roadmap also claimed isolated WebView2 rendering while the current preview
is in-process Broiler.HTML; those status claims are corrected with this review.

NuGet flat-container indexes checked on 2 October still list Hosting preview.1 and
UI preview.11 as the latest published versions. HTML.Image preview.10 is available
while Mail pins preview.9. Evaluate that update against rendering fixtures and the
AOT substitution; availability alone is not evidence that it fixes either issue.
No application/package changes were made as part of this investigation.
