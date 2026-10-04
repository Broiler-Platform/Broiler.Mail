# Broiler.Mail component reuse review

Historical review. The [current disposition](#4-october-2026-current-disposition)
at the end records which Mail wrappers remain and why. The
[4 October consolidated audit](roadmap-status-2026-10-04.md) records package
adoption and deferred extraction work.

Date: 2026-09-28. Scope: source investigation and recommendations; no runtime code
or component dependencies changed.

## Conclusion

Yes. The best opportunities are the layout workarounds in **Broiler.UI**, native
API declarations in **Broiler.Native.Windows**, and window sizing behavior in
**Broiler.Graphics.Windows**. Shared Windows UI hosting would also remove code
repeated across applications, but needs a new integration component. Mail's account,
protocol, and message policies should remain in Broiler.Mail.

This review compared Mail with the local UI, Graphics, Native, Input, Code, Writer,
Browser, Net, DOM/HTML, Documents, Fond, Plate, and JSeal sources where relevant.
When this review was written, Mail pinned UI `0.1.0-preview.9` and Graphics
`0.1.0-preview.5` in [Directory.Packages.props](D:/Broiler.Mail/Directory.Packages.props:4);
the [follow-up](#follow-up) records the current pins.
Local sibling source is evidence of implementation and ownership, not evidence
that a fix is available in a published package. Proposed API/package names below
are suggestions, not existing products.

## Recommended moves and fixes

| Priority | Mail code | Destination | Nature of the change |
| --- | --- | --- | --- |
| First | `TabContent` | `Broiler.UI.TabView.Standard` | Fix the control, then remove Mail's workaround. |
| First | `ViewportScrollView` and the measurement part of `ScrollableMessageText` | `Broiler.UI.ScrollView` / `.Standard` | Add viewport-constrained content measurement. |
| Next | Native declarations in clipboard, IME, credentials, and window sizing | `Broiler.Native.Windows` | Reuse existing bindings and add missing binding families. |
| Next | DPI handling and minimum-window-size mechanics | `Broiler.Graphics` / `.Windows` | Expose size constraints and implement them in the backend. |
| Next | Generic focus traversal and scrolling focused controls into view | `Broiler.UI` / control implementations | Add neutral focus and reveal contracts. |
| Next | Literal label text and real UI dispatch | `Broiler.UI.Label` and UI infrastructure | Add small reusable capabilities; preserve existing behavior. Dispatch is completed; see the follow-up. |
| Later | Windows host, clipboard adapter, caret integration, system theme query | A new `Broiler.Hosting.Windows` component | Package the integration shared by several applications. |
| Evaluate later | JSON configuration mechanics and a generic credential service | New narrowly scoped shared libraries, if adopted by another app | No suitable existing general-purpose owner was found. |

### 1. Fix tab and scroll layout in Broiler.UI

[TabContent](D:/Broiler.Mail/src/Broiler.Mail.Application/Views/TabContent.cs:6)
remeasures its child during arrangement to correct the tab control's initial
measurement. [StandardTabView](D:/Broiler.UI/src/Implementations/Standard/Layout/Broiler.UI.TabView.Standard/StandardTabView.cs:44)
measures content at its preferred size, then arranges it at the actual allocation.
That mismatch affects wrapped content. Correct allocation-aware measurement in the
control; do not publish Mail's workaround as a new helper.

`ViewportScrollView` (since removed)
and [ScrollableMessageText](D:/Broiler.Mail/src/Broiler.Mail.Application/Preview/ScrollableMessageText.cs:27)
contain almost the same content-width wrapper. The message variant even hardcodes
the scrollbar thickness as `12`, while the form variant reads the control property.
[StandardScrollView](D:/Broiler.UI/src/Implementations/Standard/Layout/Broiler.UI.ScrollView.Standard/StandardScrollView.cs:87)
measures children with infinite width and height. Add an **opt-in constrained-axis
measurement mode** so vertically scrolling content can wrap to the viewport.
Hiding a horizontal scrollbar alone does not provide this behavior.

Preserve normal two-dimensional scrolling, handle the width consumed by a visible
vertical scrollbar, and remeasure on resize. Mail should keep its message text,
theme choices, and scroll-to-start-on-selection behavior. While awaiting an upstream
release, the two Mail wrappers could also share one implementation locally.

### 2. Centralize native bindings and window behavior

Mail declares its own Win32 APIs and ABI structures in
[WindowsClipboard](D:/Broiler.Mail/src/Broiler.Mail.Windows/Services/WindowsClipboard.cs:58),
[WindowsTextInput](D:/Broiler.Mail/src/Broiler.Mail.Windows/Services/WindowsTextInput.cs:30),
[WindowsCredentialStore](D:/Broiler.Mail/src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs:84),
and [WindowsWindowSizing](D:/Broiler.Mail/src/Broiler.Mail.Windows/Hosting/WindowsWindowSizing.cs:25).
These declarations fit [Broiler.Native's stated ownership](D:/Broiler.Native/README.md:3).
Its [WindowNative](D:/Broiler.Native/src/Broiler.Native.Windows/WindowNative.cs:7)
already supplies window-style and DPI rectangle APIs. Clipboard, IME, and credential
binding families would need additions to the inspected source.

Move declarations and native structures, not Mail models or UI interfaces, into
Native. Preserve ABI layout, encoding, last-error handling, and buffer ownership.

[WindowsWindowSizing](D:/Broiler.Mail/src/Broiler.Mail.Windows/Hosting/WindowsWindowSizing.cs:5)
also implements generic behavior: translating minimum client dimensions into a
native tracking size and applying the rectangle suggested by `WM_DPICHANGED`.
This belongs in the Graphics window backend. The inspected
[Direct2DWindow](D:/Broiler.Graphics/src/Broiler.Graphics.Windows/Direct2DWindow.cs:565)
refreshes the rendering surface on a DPI change; Mail adds the suggested-rectangle
handling. [BWindowOptions](D:/Broiler.Graphics/src/Broiler.Graphics/Windowing/BWindowOptions.cs:7)
has no minimum client size option.

Add neutral minimum-size options and implement them in Graphics.Windows. Keep
Mail's **640 x 480 choice** in Mail. Coordinate removal of Mail's native handler
with the backend upgrade so the same message is not handled twice.

### 3. Share UI behavior without importing Mail policy

[MailKeyboardNavigation](D:/Broiler.Mail/src/Broiler.Mail.Application/Views/MailKeyboardNavigation.cs:57)
implements generic traversal and reveals focused controls inside scroll views.
Its concrete `StandardEdit`/`StandardButton`/`StandardComboBox` checks show the need
for a neutral focusability/tab-stop contract. Existing
[StandardFocusScope](D:/Broiler.UI/src/Foundation/Broiler.UI.Standard/Focus/StandardFocusScope.cs:16)
only sets focus. Add traversal and a reveal operation upstream, covering disabled
and collapsed controls, active tabs, nested scrolling, and modal scopes. Keep
F5 receive, Escape cancellation, Enter-to-read, and Mail's tab shortcuts in Mail.

[UiLabel](D:/Broiler.UI/src/Abstractions/Content/Broiler.UI.Label/UiLabel.cs:35)
always interprets ampersands as mnemonic markers. Mail escapes ampersands in the
message reader, message header, and status label. A literal-text mode should retain
the original text in both rendering and accessibility semantics, without creating
an inferred access key. Keep mnemonic behavior available for form labels.

Mail's former `WindowsUiDispatcher` was another reusable piece: thread-affinity
checking plus a host-supplied posting delegate. Code independently has a queued
[UiThreadDispatcher](D:/Broiler.Code/src/Broiler.Code.Core/Hosting/UiThreadDispatcher.cs:21).
A neutral implementation can live in UI infrastructure, with the native wake-up
and window lifetime in its host. Define callback ordering and shutdown semantics
before consolidating them: Mail runs same-thread callbacks immediately, whereas
Code queues them. UI's existing `ImmediateUiDispatcher` is not a replacement for
Mail's background completion dispatch.

**Completed:** Broiler.UI `0.1.0-preview.10` added `StandardQueuedUiDispatcher`,
which settles both questions the way Code does. Callbacks from any thread, the owner
included, run in order only when the host drains them on the owner thread, and a
closed host simply stops draining. Mail's window now uses it, and `WindowsUiDispatcher`
is removed.

### 4. Package Windows hosting separately

[WindowsUiHost](D:/Broiler.Mail/src/Broiler.Mail.Windows/Hosting/WindowsUiHost.cs:9)
and the generic parts of
[WindowsMailWindow](D:/Broiler.Mail/src/Broiler.Mail.Windows/Hosting/WindowsMailWindow.cs:15)
combine a UI session, graphics window, rendering, input forwarding, dispatcher,
clipboard, and caret placement. Clipboard implementations also exist in
[Writer](D:/Broiler.Writer/src/Broiler.App/WindowsClipboard.cs:25),
[Browser](D:/Broiler.Browser/src/Broiler.App/WindowsClipboard.cs:25), and
[Code](D:/Broiler.Code/src/Broiler.App/WindowsClipboard.cs:25).
These are application-owned implementations, not an existing shared host package.

A new **Broiler.Hosting.Windows** component could depend on UI abstractions,
Graphics.Windows, and Native.Windows and supply this integration. Do not move native
dependencies into today's Broiler.UI runtime projects: their
[dependency rules](D:/Broiler.UI/README.md:130) explicitly prohibit that direction.
Graphics likewise should not acquire a dependency on UI merely to implement its
host interfaces.

Extract the clipboard adapter first, reconciling limits and memory ownership.
Then compare caret/IME support with Code's richer
[WindowsTextInputService](D:/Broiler.Code/src/Broiler.Code.Windows/WindowsTextInputService.cs:25)
before adopting Mail's smaller adapter. Mail currently places the default IME
composition window; it is not a full composition service. Keep application content,
commands, settings, and disposal of Mail operations in Mail.
Composition-event decoding would belong in Broiler.Input, with the host adapter
above it; preserve exactly-once text delivery when changing the current WM_CHAR path.

[WindowsTheme](D:/Broiler.Mail/src/Broiler.Mail.Windows/Services/WindowsTheme.cs:7)
can eventually use the same host's system-theme query. The choice between System,
Light, and Dark remains a Mail preference. UI already has
[IUiSystemSettingsHost](D:/Broiler.UI/src/Foundation/Broiler.UI/Host/IUiSystemSettingsHost.cs:3),
so reuse that neutral contract. Do not introduce a new component solely to remove
this small class.

## Candidates to defer

- **JSON persistence:**
  [JsonConfigurationFile](D:/Broiler.Mail/src/Broiler.Mail.Infrastructure/Persistence/JsonConfigurationFile.cs:8)
  has reusable bounded reads, locking, validation, and temporary-file replacement.
  Code's [workspace storage](D:/Broiler.Code/src/Broiler.Code.Workspaces/Storage/FileSystemWorkspaceStorage.cs:106)
  and recovery journal show related demand, but are not interchangeable stores.
  Consider a small shared persistence library with configurable schema/serialization,
  limits, validation, and lock policy when a second application adopts it. Keep
  `JsonAccountStore`, the one-account rule, defaults, and configuration validation
  in Mail. Do not make Mail depend on the Code application to obtain file helpers.
  Preserve each consumer's existing file format and revision/corruption semantics;
  do not impose Mail's JSON envelope on other applications.
- **Credential service:** move native declarations now; retain the Mail adapter.
  A later generic Windows secret service could take an application namespace and
  opaque key. Preserve Mail's account/protocol slot and server-identity binding;
  moving code must not allow a saved password to be reused after an endpoint change.
  No existing general-purpose Broiler credential service was found in the reviewed
  components. JSeal is a JavaScript engine abstraction, not a secret store.
- **HTML-to-text:** a bounded standalone HTML text extractor could fit
  [Broiler.Dom.Html](D:/Broiler.DOM/Broiler.Dom.Html/HtmlTokenizer.cs:49)
  if another consumer needs it. Existing DOM text content and document
  import are not equivalent to Mail's suppression of script/style/embedded content.
  Keep MIME traversal, charset decoding, message-size limits, and fallback/truncation
  policy in `MessageTextDecoder`. There is no immediate redundancy that warrants a
  second parser dependency for version 1.

## Keep in Broiler.Mail

Keep `ImapMailReceiver`, SMTP integration, message/account identities, credential
binding, mailbox cursor rules, view models, and account/settings forms. These encode
mail behavior rather than shared infrastructure.
[Broiler.Net](D:/Broiler.Net/README.md:3) currently owns HTTP,
cookies, and site policy; moving a MailKit adapter there would broaden its contract
without removing a demonstrated duplicate. Fond and Plate are applications, not
general-purpose foundation libraries. `SaveViewModel` also carries Mail-specific
failure and status policy; a generic application framework is unnecessary here.

## Suggested implementation order and verification

1. Fix TabView measurement and add constrained ScrollView measurement upstream.
   Retain default scrolling behavior and add control-level layout regression tests.
2. Consume a published UI package in Mail and remove the corresponding wrappers.
   Retain Mail's [full-shell acceptance checks](D:/Broiler.Mail/tests/Broiler.Mail.Tests/Version1AcceptanceTests.cs:91)
   and [reader scrolling checks](D:/Broiler.Mail/tests/Broiler.Mail.Tests/InboxWorkflowTests.cs:144).
3. Consolidate native declarations, then add Graphics minimum-size/DPI behavior.
   Verify native struct layout, resizing, and transitions between DPI settings.
4. Add literal labels, focus traversal, and shared dispatch with targeted tests.
   Preserve password-field copy protection and worker-thread completion behavior.
5. Extract Windows hosting with Mail and at least one other application as consumers.
   Verify clipboard ownership/limits, caret placement, shutdown, and native input.
   Retain the [credential isolation tests](D:/Broiler.Mail/tests/Broiler.Mail.Windows.Tests/WindowsCredentialStoreTests.cs:28)
   when replacing credential bindings.

Use versioned package references after upstream releases, not permanent absolute
project references to neighboring checkouts. This investigation ran no builds or
runtime tests because it changed documentation only. Existing acceptance coverage
is identified above as a migration requirement, not a new validation result.

## Follow-up

2026-09-28: Mail now pins Broiler.UI `0.1.0-preview.10` and Graphics
`0.1.0-preview.7`, the Graphics release that UI preview.10 requires. Of the moves
above, that UI release contains only the shared dispatch. TabView and ScrollView
measurement, label mnemonics, and focus traversal are unchanged, so `TabContent`,
`ViewportScrollView`, the `ScrollableMessageText` wrapper, ampersand escaping, and
`MailKeyboardNavigation` stay in Mail. The release's other changes, a RichEdit
decomposition and a cancellable directory provider for the file dialog, concern
controls Mail does not use yet. The provider matters once attachments need a file
dialog in version 4. Graphics preview.6 and preview.7 changed only the window class
icon and packaging.

Validation: the Release build has no warnings, all 77 tests pass, and the headless
smoke check passes. The inbox and configuration workflow tests now use the real
dispatcher and drain it on the thread that created it. One test checks that the
receiver runs on a worker while every change is published on that thread. The native
demo was driven with posted window messages. Mail received from the thread pool,
selected a message, and rendered its body. Closing the window during a receive
exited cleanly with code 0.

### 4 October 2026: current disposition

Mail now pins Broiler.UI `0.1.0-preview.17`, Graphics `0.1.0-preview.7`, Native
`0.1.0-preview.6`, Input `0.1.0-preview.5`, and Hosting `0.1.0-preview.5`. Each
Mail-local wrapper from this review now stands as follows:

| Mail code | Disposition | Why, and what would retire it |
| --- | --- | --- |
| `TabContent` | Kept, for a different reason | `StandardTabView` now measures every tab's content at its allocated size, and the selected tab lays out the same without the wrapper: `ShellLayoutTests` passed without it, also after height-only resizes, and NativeAOT Accept-UI (inbox, large-draft, save-error; 640x480 and 1100x720; light; 100% and 200% text) matched a build with it. But the tab view arranges each hidden tab at an empty rectangle, so a hidden form was laid out at no width and re-laid out on return, and the composer's status area came back scrolled to its top (640x480, 200% text). The wrapper skips that empty arrange; `ShellLayoutTests.ATabIsAsItWasLeftAfterAnotherTabWasShown` fails without it. It still re-measures at the given size, which covers a height difference the tab view's own arrange check (width only) ignores. Upstream need: `StandardTabView` should leave hidden content unarranged, or keep its last arrangement. |
| `ViewportScrollView` and `ConfigurationForm.Wrap` | Removed | Dead code: nothing called `Wrap`, its only user. The forms scroll in `FormSurface`/`FormViewport`; width-constrained scrolling is `StandardScrollView.Constraint = ConstrainWidth`. |
| `ScrollableMessageText` | Kept as a composite | Its own measurement wrapper is gone; it composes a read-only `StandardRichEdit`, a `ReadingColumn`, and a `ConstrainWidth` scroll view, and names the reader for screen readers. Since the preview zoom it also keeps the reader at the same relative place through a zoom, also one made while it is hidden: it takes an empty arrange as hidden, the same kind of hidden-layout workaround as `TabContent`, and restores the place at the next arrange at a real size. A scroll view that kept its relative place across a reflow, and left hidden content unarranged, would retire that part upstream. |
| `ReadingColumn` | Kept | Bounds the reader's and the header's line length at 720 DIP and narrows the margins before the text in small windows. Broiler.UI has no maximum width; an upstream maximum size or reading-width layout would retire it (optional). |
| `AdaptiveInboxLayout` | Kept by design | Mail's responsive policy: the list and reader side by side when both are readable, one at a time otherwise. It is product behavior built on the standard split container, not a missing control feature. |
| `BoundedScrollArea` | Kept | Caps the message header (45%) and the inbox notice (40%) at a share of the available height and scrolls past it. Broiler.UI has no maximum size or height share. An upstream `MaxHeight`/max-fraction option on a scroll view would retire it (optional). |
| `FillLastStack` | Kept | Gives the composer body the height left under the fields, but never less than a minimum; outer scrolling starts only when the fields need the room. Broiler.UI's dock fill has no minimum. An upstream minimum size or fill-with-minimum stack would retire it (optional). |
| Literal labels | Done | `UiLabel.UseMnemonic = false` on every label showing addresses, subjects, server text, or the footer status. No `&&` escaping remains in Mail. |
| Combo-box sizing | Workaround kept | `AppearanceController` sizes each `StandardComboBox` and its rows from the applied font, because `StandardComboBox.MeasureCore` returns only `PreferredSize`. Needs the combo to measure from its font upstream. |
| IME placement (`WindowsTextInput`) | Kept | Mail positions the default IME window at the caret and turns the IME off while a password field has the caret. Broiler.Hosting.Windows has no `IUiTextInputHost` yet (only the Android host has one), so this waits for Hosting; a Hosting host would need the same password rule. |
| `WindowsTitleBar` | Removed | Mail calls Broiler.Hosting.Windows' `WindowsTitleBar.ApplyDarkMode`, published since Hosting preview.5, at the same points as before. |
