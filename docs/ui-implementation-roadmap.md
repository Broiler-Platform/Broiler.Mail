# Broiler.Mail UI implementation roadmap

Updated **2 October 2026**. Status: implementation plan; unchecked work is not yet
accepted. Baseline: commit `6bdcb83`, the current source audit, and the prior native
Hosting validation. This document is the execution plan for the remaining UI work;
the [experience review](experience-roadmap.md) remains the original design rationale.

## Goal and scope

Make reading and writing mail feel calm, responsive, and predictable. Give the reader
clear visual hierarchy, keep actions within reach, preserve context across background
work, and make the same flows usable with a keyboard and assistive technology.

This plan covers the Windows Mail UI, shared application views, and the Broiler
UI/Hosting changes needed by those views. It includes native acceptance of the
published NativeAOT application. Linux and Android UI adaptations are explicitly
tracked below, with their application-host prerequisites.

The renderer process sandbox, OAuth, mailbox synchronization, multiple-account data
model, attachment transport, credential backends, and release signing remain separate
workstreams. Their user-facing states belong here; their underlying implementation
belongs in the [product roadmap](roadmap.md), [platform plan](cross-platform-roadmap.md),
and [renderer boundary specification](html-renderer-security.md). In particular, this
roadmap does not make an in-process HTML renderer safe by changing its presentation.

## Preserve the delivered foundation

- Two-line sender/subject/date rows and unread presentation already exist in
  `MailMessageItemPresenter`.
- `StandardSplitContainer` already provides an adjustable inbox/reader split.
- `ScrollableMessageText` already provides selectable, wrapping, read-only text.
- Published C-04 `FormField`, `FormSection`, `InlineFeedback`, and `FormSurface`
  already provide grouped forms, optional Cc/Bcc, persistent actions, and feedback.
- Broiler.Hosting packages already supply the shared native integration. Reuse them;
  repair reusable defects upstream and consume a published package when available.
- Preserve draft autosave, conflict handling, SMTP outcome-unknown behavior, separate
  Sent-copy outcomes, password protection, and stale-result rejection during every
  view change.

Do not mark these foundations as unimplemented because an older EX item is unchecked.
Do not mark native accessibility complete because managed peer tests pass.

## Visual and interaction direction

Use a **calm reader** direction: a quiet message list, a spacious reading surface,
restrained separators, and a small number of clearly ranked actions. Keep the native
window frame. Custom chrome, translucency, and decorative animation are deferred.

The following are starting design choices to verify in the gallery, not platform rules:

| Area | Starting choice | Adaptation rule |
| --- | --- | --- |
| Spacing | 4/8/12/16/24/32 DIP scale | Use existing shared tokens; add missing reusable roles upstream |
| Reading surface | 24–32 DIP wide-layout margins; 12–16 DIP compact margins | Reduce margins before shrinking essential content or hiding actions |
| Typography | Existing UI font; 22–26 DIP subject; 16–18 DIP message body | Respect system text scale; no fixed-height containers that clip text |
| Reading width | A bounded prose column, approximately 65–85 characters | Let users expand it for long lines/logs; preserve copying and original text |
| List | Existing two-line rows with aligned trailing date and unread marker | Sender and subject take precedence over optional metadata on narrow rows |
| Actions | One clear primary action per context; secondary actions quieter | Persistent essential actions wrap or use labeled overflow |
| Color | Theme surface/text/accent and semantic feedback roles | Status also has text or shape; high contrast follows system colors |
| Icons | Existing vector/control icon conventions; 16/20 DIP starting sizes | Keep text labels for Send, Reply, Save, and other consequential actions |
| Motion | Initially instant; optional short hover/press transitions later | Reduced motion is instant; animation must not introduce idle rendering |

Choose pane mode from the minimum readable widths of the current controls and text
scale. Avoid a device-name or fixed-pixel breakpoint. At the current minimum window
size, a single-pane list → reader flow is preferable to two unusably narrow panes.

## Work packages and dependencies

All packages below are open. “Partial” means existing implementation is reused, not
that the remaining acceptance criteria have passed. P0 is an acceptance blocker;
P1 improves the current app; P2 depends on larger product/platform work.

| ID | Priority | Work | Starting state | Dependencies |
| --- | --- | --- | --- | --- |
| UI-01 | P1 | Shared presentation tokens and fixture gallery | Partial | None |
| UI-02 | P1 | Refresh continuity and stable selection | Open | None |
| UI-03 | P1 | Reader hierarchy and local reply commands | Partial | UI-01, UI-02 |
| UI-04 | P1 | Responsive inbox and shell navigation | Partial | UI-01, UI-02, UI-03 |
| UI-05 | P1 | Writing-focused composer | Partial | UI-01; integrate with UI-03 commands |
| UI-06 | P1 | Guided account setup and concise settings | Partial | UI-01 |
| UI-07 | P1 | Live appearance and geometry persistence | Partial | UI-01 |
| UI-08 | P1 | Consistent state, feedback, and recovery UX | Partial | Apply to UI-02 through UI-07 |
| UI-09 | P0 | Native accessibility and semantic integration | Native acceptance failing | Start immediately; verify every delivered surface |
| UI-10 | P1 | Keyboard, IME, scrolling, and focus fidelity | Partial | Coordinate with UI-04, UI-05, UI-09 |
| UI-11 | P1 | HTML preview ergonomics | Partial | UI-01, UI-08; inline embedding also needs sandbox |
| UI-12 | P1 | Measured rendering and memory performance | Partial | Capture baseline first; repeat after affected changes |
| UI-13 | P1 | Native visual and interaction acceptance | Open | Continuous; final gate for UI-01 through UI-12 |
| UI-14 | P2 | Platform and later-feature UI adaptations | Planned | Product/platform services and UI-13 foundation |

### UI-01 — presentation tokens and a deterministic UI gallery

**Owner:** Mail view composition; reusable roles in Broiler.UI.

- [ ] Inventory current token usage and remove per-view color/font divergence through
  shared roles: heading, body, secondary text, separator, focus, selection, and feedback.
- [ ] Establish the spacing/type hierarchy above in the existing demo or an isolated
  fixture harness. Use synthetic messages only, with fixed dates for reproducible images.
- [ ] Include long subject/address, empty inbox, 500 messages, plain/HTML-only mail,
  large draft, failed save, send outcome unknown, narrow window, and long translated labels.
- [ ] Add compact/comfortable density only where the consumed controls support it;
  identify any missing API before creating a Mail-specific workaround.

**Files:** [MailMessageItemPresenter](../src/Broiler.Mail.Application/Views/MailMessageItemPresenter.cs),
[InboxView](../src/Broiler.Mail.Application/Views/InboxView.cs),
[ConfigurationForm](../src/Broiler.Mail.Application/Views/ConfigurationForm.cs),
[DemoApplication](../src/Broiler.Mail.Windows/DemoApplication.cs).

**Accept:** light/dark/high-contrast fixture screens have consistent hierarchy and
spacing; enlarged labels fit; focus remains distinct from selection. Record the
token choices once rather than maintaining screenshots as competing specifications.

### UI-02 — refresh without losing the reader's place

**Owner:** Mail view model and view state. Maps to A3 / EX-07.

- [ ] Reconcile refreshed summaries by full message key, including account/mailbox
  identity and UIDVALIDITY. Update metadata without treating a retained body as a new body.
- [ ] Preserve selected identity, list anchor plus within-row offset, body scroll,
  and text selection when the underlying content remains unchanged.
- [ ] Preserve an open HTML preview for the same valid body; close it for an actual
  account/message change or invalidation.
- [ ] Distinguish a message missing from the fetched page from confirmed deletion.
  Keep already loaded context within existing bounds or explain that it is outside
  the refreshed page; do not invent a server deletion from page absence.
- [ ] Define empty inbox, changed UIDVALIDITY, account switch, canceled refresh,
  failed refresh, and late completion transitions. Retain the existing generation guard.

**Files:** [InboxViewModel](../src/Broiler.Mail.Application/ViewModels/InboxViewModel.cs),
[InboxView](../src/Broiler.Mail.Application/Views/InboxView.cs),
[ScrollableMessageText](../src/Broiler.Mail.Application/Preview/ScrollableMessageText.cs).

**Accept:** successful and failed refresh preserve valid reading context; new/reordered
rows do not cause a jump; identity changes cannot display another account's content.
Use state-transition tests plus a native scroll/selection check. Add upstream anchor
support if the published ListView cannot express stable restoration.

### UI-03 — a readable message surface with nearby reply actions

**Owner:** Mail. Maps to A4 / EX-03 / EX-05.

- [ ] Replace the single combined header label with a wrapping subject heading,
  sender/address details, quieter timestamp, and optional expandable metadata.
- [ ] Make addresses and header values selectable/copyable without converting `&`
  into mnemonics. Preserve a usable narrow layout for long unbroken addresses.
- [ ] Apply reading margins and a bounded text column using existing layout controls.
- [ ] Put Reply, Reply all, and Forward near the message. Route both reader and
  composer buttons through the same application command behavior.
- [ ] If a draft already exists, preserve it and reveal the existing composition with
  an explanation. Do not replace it or create a discard/send side effect from navigation.
- [ ] On successful New/Forward, focus To; on Reply/Reply all, focus the body.
  Return navigation restores the prior reader selection and focus target.

**Files:** `InboxView`, [MailShellView](../src/Broiler.Mail.Application/Views/MailShellView.cs),
[ComposerView](../src/Broiler.Mail.Application/Views/ComposerView.cs),
[ComposerViewModel](../src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs).
Extract a reader header/command binding only if that makes ownership clearer; it is
an implementation choice, not a required new shared control.

**Accept:** read → reply takes one action, reply recipients/threading stay unchanged,
an existing draft survives, and keyboard/screen-reader focus lands in the intended field.

### UI-04 — responsive inbox and shell

**Owner:** Mail; generic responsive layout/anchor APIs in Broiler.UI when needed.

- [ ] Preserve the wide split view and user's splitter choice. Switch to one pane
  when both minimum readable widths cannot fit; retain the wide split ratio separately.
- [ ] Provide a labeled Back to inbox action in compact reader mode and restore the
  list anchor and selected row. Browser-style back behavior must not discard a draft.
- [ ] Keep two-line rows readable: ellipsize the less important field first, avoid
  date/sender overlap, and expose full information through selection and semantics.
- [ ] Wrap/overflow toolbars predictably. Keep existing tabs and shortcuts initially;
  avoid a navigation redesign unrelated to the reader improvement.
- [ ] Preserve tab, focus, and view state across resize and DPI transitions. Do not
  destroy and recreate editors merely because a breakpoint changed.

**Files:** `InboxView`, `MailMessageItemPresenter`, `MailShellView`,
[TabContent](../src/Broiler.Mail.Application/Views/TabContent.cs).

**Accept:** minimum-size, wide, and 200% text-scale views have reachable actions,
no overlapping rows, and no lost draft/selection on repeated resize.

### UI-05 — make writing occupy the composer

**Owner:** Mail; reusable sizing/focus behavior in Broiler.UI.Forms/RichEdit.

- [ ] Let the body consume the remaining viewport height rather than always requesting
  a 300-DIP editor under an instruction-heavy stack.
- [ ] Use a compact sender line and concise recipient/subject fields. Move secondary
  instructions into contextual help without hiding the sender or send outcome.
- [ ] Retain collapsed Cc/Bcc summaries and automatically reveal populated/recovered
  fields. Collapsing a section must preserve its values and move focus safely.
- [ ] Keep Send, Check draft, Save draft, and Discard reachable at every supported
  size. Keep submission, Sent-copy, and draft-storage feedback distinct.
- [ ] Make the body own normal editing scroll; allow outer scrolling when enlarged
  header fields genuinely exceed the viewport. Avoid two scrollbars moving the same area.
- [ ] Preserve caret/selection/undo and IME composition while status messages update.
  Ordinary refresh must not reload the entire draft into the editor.
- [ ] Show durable autosave state unobtrusively; keep failed/conflicting saves visible
  and actionable. Navigation/closing must retain the current save guard.

**Files:** `ComposerView`, `ComposerViewModel`, `ConfigurationForm`; reuse the
[C-04 contracts](c04-forms/README.md).

**Accept:** sustained typing, paste, Cc/Bcc expansion, background autosave, errors,
resize, and tab changes preserve text and caret. Unknown SMTP acceptance never turns
into an ordinary retry or an automatic resend. No network test is needed for layout;
use injected outcome states plus existing submission regression tests.

### UI-06 — account setup and settings that explain the next step

**Owner:** Mail; C-04 controls already supply the structure. Maps to A6 / EX-08.

- [ ] Add a first-account flow with explicit stages: identity/server details,
  save profile, credentials, connection test, optional outgoing setup, ready to receive.
- [ ] Keep a direct advanced-edit mode for existing accounts. Do not automatically
  guess provider settings or silently alter transport/security choices.
- [ ] Make unsaved profile changes, credential binding, and test readiness clear.
  Keep password values out of summaries, notifications, screenshots, and diagnostics.
- [ ] Keep per-field validation and focus/reveal behavior. Show actionable failure
  text at the relevant section instead of duplicating the same paragraph everywhere.
- [ ] Add SMTP-test UI only once a non-sending connection/authentication service exists.
  Separate receiving success from sending configuration; never send a trial message implicitly.
- [ ] Simplify appearance/geometry wording when UI-07 removes the restart requirement.

**Files:** [AccountProfileView](../src/Broiler.Mail.Application/Views/AccountProfileView.cs),
[AccountProfileViewModel](../src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs),
[SettingsView](../src/Broiler.Mail.Application/Views/SettingsView.cs).

**Accept:** keyboard-only first setup has a clear next action, back navigation retains
nonsecret edits, failed validation reveals the field, and existing accounts keep their
saved configuration and credential-binding behavior.

### UI-07 — live appearance, accessible scaling, and window restoration

**Owner:** Mail application preferences; Hosting system settings; UI token propagation.

- [ ] Subscribe once to saved preferences and OS appearance changes. Apply the theme
  to existing controls; follow OS color mode only when System is selected.
- [ ] Give high contrast and system text scaling an explicit precedence policy,
  including when a user chose Light/Dark. Verify actual system colors and readable focus.
- [ ] Update reader, composer, popups, and existing preview windows consistently.
  If paint tokens are process-global, coordinate updates across window threads.
- [ ] Respect reduced motion and verify RTL layout separately from simply translating
  labels. Keep message content direction independent from shell direction.
- [ ] Persist window size/placement with debounced writes; preserve restored bounds
  while maximized. Clamp restoration to an available monitor after monitor/DPI changes.
- [ ] Migrate settings compatibly; preserve splitter state and tolerate old files.

**Files:** [SettingsViewModel](../src/Broiler.Mail.Application/ViewModels/SettingsViewModel.cs),
[WindowsMailWindow](../src/Broiler.Mail.Windows/Hosting/WindowsMailWindow.cs),
[WindowsUiHost](../src/Broiler.Mail.Windows/Hosting/WindowsUiHost.cs),
[ApplicationSettings](../src/Broiler.Mail.Core/Settings/ApplicationSettings.cs),
[Program](../src/Broiler.Mail.Windows/Program.cs).

**Accept:** theme changes require no restart, explicit color preference survives OS
changes, enlarged text is usable, focus/caret survive restyling, and a removed monitor
cannot strand the app off-screen. Settings errors must not interrupt typing.

### UI-08 — coherent loading, empty, error, and recovery states

**Owner:** Mail view-state presentation. Reuse `InlineFeedback`; retain domain outcomes.

- [ ] Define each surface's idle/empty/loading/ready/canceled/failed state and its
  valid actions. Keep usable content visible during non-destructive background work.
- [ ] Put the explanation and retry/cancel action beside the affected pane. Keep the
  shell footer concise rather than repeating every section's full feedback text.
- [ ] Deduplicate announcements: background autosave must not continually interrupt
  a screen reader. Errors and submission outcome changes still need announcements.
- [ ] Separate transient success from persistent warnings. Critical unsaved/unknown
  states stay visible until resolved; decorative success may disappear without losing context.
- [ ] Specify focus after validation, cancel, retry, disclosure collapse, and recovery.
  Async completion must not steal focus from typing or move the active tab unexpectedly.

**Accept:** fixture coverage includes empty inbox, refresh failure with old data,
body failure, invalid setup, canceled test, failed/conflicting autosave, rejected send,
unknown send, and failed Sent copy. Every state has accurate text and valid actions.

### UI-09 — native accessibility, from discovery to text editing

**Owner:** Broiler.Hosting.Windows provider; Broiler.UI semantics; Mail integration.
Maps to A1 / EX-10 / H-01. This is a release acceptance blocker.

- [ ] First reproduce the published app's empty control tree using an external client.
  Trace subclass attachment, render-HWND `WM_GETOBJECT`, host provider, and return codes.
- [ ] Verify native COM interface discovery, vtables, marshalling ownership, fragment
  navigation, UI-thread dispatch, disposal, and runtime IDs. Compare Raw/Control/Content
  views; missing view flags alone have not been established as the cause.
- [ ] Expose stable names/roles/labels and correct supported patterns for the current
  controls. Keep collapsed/removed controls out of navigation; retain virtual row identity.
- [ ] Add the text/range and selection behavior needed to read and edit messages with
  assistive technology. A Value-only provider is not acceptance of rich text navigation.
- [ ] Verify focus, selection, busy/error/status announcements and password masking.
  Map grouped validation to its field and expose expanded/collapsed state.
- [ ] Run external-client checks on the published NativeAOT binary; add actual
  screen-reader acceptance before closing H-01.

**Files:** Mail's `WindowsMailWindow` integration and
[WindowsAutomationBridgeTests](../tests/Broiler.Mail.Windows.Tests/WindowsAutomationBridgeTests.cs);
provider implementation belongs in the Hosting repository/package.

**Accept:** find Inbox/To/Send by name/role, operate without coordinates, read/select
text, receive relevant announcements, and never expose password values. Evidence must
come from a native client as well as managed tests. Track any upstream package wait explicitly.

### UI-10 — keyboard, native text input, and scroll behavior

**Owner:** Mail commands/focus policy; Hosting/Input native event fidelity.

- [ ] Review the hard-coded focusable-control list and use semantic/control traversal
  where supported. New controls should not silently fall out of Tab navigation.
- [ ] Preserve Ctrl+1–4, Ctrl+Tab, F5, Escape, Enter-on-message, and standard edit keys.
  Define new reply/back shortcuts once at the shell command layer and display them consistently.
- [ ] Keep splitter/overflow/disclosure actions reachable by keyboard. Reveal focused
  controls without scrolling unrelated panes or rebuilding the editor.
- [ ] Verify AltGr, dead keys, surrogate pairs, IME composition/commit/cancel, paste,
  caret positioning at DPI changes, precision wheel, and horizontal scrolling where relevant.
- [ ] Ensure actions run once across native and neutral input paths. Remove legacy
  event adapters only after the consumed packages provide an equivalent neutral source.

**Files:** [MailKeyboardNavigation](../src/Broiler.Mail.Application/Views/MailKeyboardNavigation.cs),
`WindowsMailWindow`, Hosting's input bridge, and existing Windows input tests.

**Accept:** keyboard-only completion of reading/setup/composition, exactly-once text
input, no shortcut characters inserted into editors, and visible focus after every
navigation/disclosure/resize transition.

### UI-11 — predictable HTML preview presentation

**Owner:** Mail preview UX; shared HTML rendering APIs when required.

- [ ] Keep a clear message identity, Back to text/Close action, and remote-image state.
  Preserve useful error information without exposing internal implementation details in the UI.
- [ ] Distinguish initial load, canceled load, partial/resource-limited content, blocked
  remote resources, failure, and ready. Keep retry explicit and prevent automatic crash loops.
- [ ] Preserve zoom/scroll during harmless status updates; invalidate them deliberately
  when the message, document geometry, or DPI changes.
- [ ] Align typography, theme, focus, shortcuts, and scaling with the shell. Keep
  external navigation user-initiated and subject to the existing URL policy.
- [ ] After the renderer process boundary passes its acceptance gate, design an inline
  text/HTML toggle in the reader with stable message identity and plain-text fallback.

**Files:** [HtmlMessagePreview](../src/Broiler.Mail.Application/Preview/HtmlMessagePreview.cs),
[HtmlPreviewWindow](../src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs),
[WindowsHtmlPreviewHost](../src/Broiler.Mail.Windows/Preview/WindowsHtmlPreviewHost.cs).

**Accept:** selection change/cancel/close cannot reopen an obsolete preview; large or
failed documents leave text usable; resource approval belongs to the current document.
Inline embedding remains blocked by the separate renderer containment gate.

### UI-12 — performance with measured budgets

**Owner:** Mail scenarios/measurement; shared UI/Graphics/HTML optimizations when justified.

- [ ] Record startup-to-interactive, input-to-paint, frame p50/p95/p99, allocations,
  working set, text/layout calls, and tile misses on fixed demo fixtures.
- [ ] Cover idle, 500-row scrolling, body selection, sustained typing/autosave, live
  theme change, splitter drag, resize, and long HTML at multiple DPI scales.
- [ ] Set targets against a named reference machine. A 16.7 ms frame budget at 60 Hz
  is a starting target, not a claim that current hardware/builds meet it.
- [ ] Optimize only demonstrated costs: repeated text/layout, redundant invalidation,
  editor resets, cache misses, or bitmap/PNG upload conversion.
- [ ] Add tile pixel/byte bounds in addition to the existing count limit; higher DPI
  and width must not create an effectively unbounded memory allowance.
- [ ] Verify idle rendering stops, active input remains responsive, and performance
  changes preserve invalidations, selection, text accuracy, and resource disposal.

**Accept:** retain comparable before/after measurements and reproducible scenarios.
Do not substitute screenshot inspection or unit-test counts for latency/memory evidence.

### UI-13 — published-app acceptance and evidence

**Owner:** Mail integration/CI. This closes the UI release milestone, not just a build.

- [ ] Maintain the matrix below as results tied to a revision, package versions,
  architecture, SDK, monitor/DPI, and test method.
- [ ] Keep focused behavior tests for state transitions, data preservation, and native
  regressions. Use visual review for spacing/hierarchy rather than brittle tests of every pixel.
- [ ] Exercise the packaged NativeAOT executable with an isolated demo/test profile;
  headless `--smoke-test` remains a useful but limited composition check.
- [ ] Check screenshots for clipping/overlap and native interaction for focus/input.
  Synthetic examples must cover long strings, Unicode, errors, and populated forms.
- [ ] Record unmet platform checks explicitly. A test in a sibling source checkout
  does not prove the published package works in Mail.

| Dimension | Required cases |
| --- | --- |
| Layout | Current minimum 640×480 DIP, normal desktop, wide, repeated breakpoint transitions |
| Display | 100/150/200% DPI; move between monitors; 100/150/200% text scale independently |
| Appearance | Light, dark, System mode, actual high contrast, reduced motion |
| Input | Mouse, precision wheel, keyboard only, AltGr/dead keys, IME and Unicode |
| Content | Empty and 500-row inbox, long subject/address, large body/draft, HTML-only message |
| State | Busy/cancel/retry, failed save, conflicting draft, unknown send, failed Sent copy |
| Accessibility | External native tree/pattern tests plus screen-reader reading/editing/announcements |
| Deployment | Published Windows x64 and ARM64; record unavailable hardware checks as pending |
| Direction | Long translated labels and mixed RTL/LTR content; distinguish layout readiness from completed localization |

**Accept:** all applicable cases pass or have a specifically documented blocker.
Complete includes native behavior, screenshots, and data/state preservation—not only
compilation or a new checked roadmap heading.

### UI-14 — later platform and product UI

**Owner:** Mail, coordinated with the relevant feature/backend owners.

- [ ] Linux: adapt the shared desktop views after an interactive host, desktop IME,
  credentials, and system services exist; validate actual desktop input and scaling.
- [ ] Android: single-pane list/detail navigation, touch targets, keyboard insets,
  back behavior, lifecycle state restoration, and mobile composition after the app
  head and secure storage exist. Hosting package presence is only a prerequisite.
- [ ] Folders/search/attachments: add controls with explicit busy/empty/error states
  when the corresponding services exist; avoid nonfunctional navigation placeholders.
- [ ] Multiple accounts: persistent sender/account identity across rows, reader, and
  composer; an intentional account switch must not silently rebind an existing draft.
- [ ] Localization: move user-facing text into a NativeAOT-compatible resource path,
  then translate and validate formatting, pluralization, keyboard labels, and RTL.

**Accept:** each feature ships with its actual domain behavior and native acceptance,
not just a mockup or a project that compiles on another OS.

## Delivery slices

Each slice should be a reviewable change with its relevant acceptance evidence.
Do not combine the whole program into one UI rewrite.

| Slice | Deliverable | Exit condition |
| --- | --- | --- |
| 1 | UI-01 fixture/token baseline; start UI-09 discovery and UI-12 measurements | Reproducible visual/performance baseline and a native UIA reproduction |
| 2 | UI-02 refresh continuity | Selection/body/scroll remain stable across valid refreshes |
| 3 | UI-03 reader and reply actions | Read → reply works directly and preserves an existing draft |
| 4 | UI-04 responsive navigation | Compact and split modes preserve context through resize |
| 5 | UI-05 composer layout | Writing fills available space; actions, caret, and draft state remain stable |
| 6 | UI-06 setup, UI-07 appearance, UI-08 feedback in small independent changes | Clear setup, live themes, geometry restoration, accurate actionable states |
| 7 | UI-09/10 native acceptance and UI-11 preview polish | External accessibility/input pass; preview lifecycle is predictable |
| 8 | Measured UI-12 fixes and UI-13 acceptance | Evidence for the shipped package across the required matrix |

UI-09 and performance baseline work begin in slice 1; they should not first be
discovered at the end. Inline HTML waits for renderer containment independently
of the other visual improvements. UI-14 follows its separate product dependencies.

## Handoff and completion record

For each completed package, record: changed Mail/shared component, published package
version if applicable, behavior before/after, focused tests, native/manual evidence,
remaining limits, and any schema migration. Keep UI work IDs stable when splitting
implementation PRs. Existing audit A1–A8 and EX IDs are references, not replacement IDs.

Cross-reference: UI-09 → A1; UI-02 → A3; UI-03/04 → A4; UI-07 → A5;
UI-05/06 → A6; UI-13 → the UI portion of A7; UI-12 → A8. A2 renderer isolation
remains outside this plan and is a prerequisite for inline HTML/release readiness.
