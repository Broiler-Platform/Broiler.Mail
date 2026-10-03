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
| UI-01 | P1 | Shared presentation tokens and fixture gallery | Tokens recorded; type scale and system text size done; long-label fixture waits for UI-14 | None |
| UI-02 | P1 | Refresh continuity and stable selection | Implemented; native new-mail check pending | None |
| UI-03 | P1 | Reader hierarchy and local reply commands | Implemented; screen-reader check pending (UI-09) | UI-01, UI-02 |
| UI-04 | P1 | Responsive inbox and shell navigation | Done: compact mode, 200 % text, readable rows, wrapping toolbars; real DPI change while compact pending | UI-01, UI-02, UI-03 |
| UI-05 | P1 | Writing-focused composer | Layout implemented; IME/undo check pending | UI-01; integrate with UI-03 commands |
| UI-06 | P1 | Guided account setup and concise settings | Setup checklist implemented; SMTP test waits for a service | UI-01 |
| UI-07 | P1 | Live appearance and geometry persistence | Live theme, geometry, and system text size, including open preview windows; real contrast colors and RTL open | UI-01 |
| UI-08 | P1 | Consistent state, feedback, and recovery UX | Done, including native announcements (Hosting preview.4); screen-reader check with UI-09 | Apply to UI-02 through UI-07 |
| UI-09 | P0 | Native accessibility and semantic integration | External-client acceptance passes (Debug and NativeAOT); real screen-reader check pending | Start immediately; verify every delivered surface |
| UI-10 | P1 | Keyboard, IME, scrolling, and focus fidelity | Shortcut table, reply shortcuts, and traversal done; IME, wheel, and DPI caret checks open | Coordinate with UI-04, UI-05, UI-09 |
| UI-11 | P1 | HTML preview ergonomics | Lifecycle, identity, theme, keyboard, UIA, scroll, and scaling done; inline view open | UI-01, UI-08; inline embedding also needs sandbox |
| UI-12 | P1 | Measured rendering and memory performance | Baseline recorded; rich-edit allocations and preview tiles optimized; resize CPU, GPU time, DPI matrix open | Capture baseline first; repeat after affected changes |
| UI-13 | P1 | Native visual and interaction acceptance | First pass recorded (96/96 automated runs clean); settings- and hardware-dependent rows pending | Continuous; final gate for UI-01 through UI-12 |
| UI-14 | P2 | Platform and later-feature UI adaptations | Planned | Product/platform services and UI-13 foundation |

### UI-01 — presentation tokens and a deterministic UI gallery

**Owner:** Mail view composition; reusable roles in Broiler.UI.

- [x] Inventory current token usage and remove per-view color/font divergence through
  shared roles: heading, body, secondary text, separator, focus, selection, and feedback.
- [x] Establish the spacing/type hierarchy above in the existing demo or an isolated
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

**Progress (2 October 2026):** the deterministic fixture harness exists; token
inventory and the spacing/type hierarchy are still open.

- `Broiler.Mail.Windows.exe --demo <scenario> [--theme light|dark|system] [--size WxH]`
  opens one of nine prepared fixtures (`--help` lists them): `inbox`, `empty`,
  `long-message`, `large-inbox`, `large-draft`, `receive-error`, `save-error`,
  `send-unknown`, `html-only`. Plain `--demo` keeps the original interactive demo.
- [`DemoScenarioDriver`](../src/Broiler.Mail.Windows/DemoScenarioDriver.cs) reaches each
  state through the ordinary view-model commands (receive, load older, select, save),
  not injected state. Drafts are recovered from a seeded in-memory store.
- Dates use [`MessageDateFormatter`](../src/Broiler.Mail.Application/Views/MessageDateFormatter.cs)
  with a fixed clock, UTC+2 zone, and en-US culture; account and draft IDs are fixed.
- [`DemoGalleryTests`](../tests/Broiler.Mail.Windows.Tests/DemoGalleryTests.cs) drive every
  scenario through a queued dispatcher, assert the resulting state, and render a frame
  at 640×480. Argument parsing is covered, including a fixed scenario-name parsing bug.
- Not yet covered: actual high contrast, enlarged text scale, long translated labels,
  and a separate fixture for a canceled operation or conflicting draft.

**Progress (3 October 2026, tokens, type scale, and text size):**

The token choices, recorded once. Mail sets no colors or fonts of its own; every role below is a
Broiler.UI theme token, so light, dark, high contrast, and the system text size all follow from the
theme:

| Role | Theme token | Where Mail uses it |
| --- | --- | --- |
| Surface title | `FontTitle` (24 DIP, semibold) via `StandardTextStyle.Title` | Reader subject |
| Section heading | `FontSubtitle` (20 DIP) via `StandardTextStyle.Subtitle` | `FormSection` headings in Account and Settings |
| Body | `FontBody` (16 DIP, the size controls already drew) | All controls and labels |
| Secondary text | Label role `Muted` (`TextMuted`) | Reader date line, composer hint, preview truncation notice |
| Caption | `FontCaption` (13 DIP) | HTML preview cut-off banner |
| Separator | `Border` / `BorderStrong` | Splitter, toolbars, list, and form frames (Broiler.UI controls) |
| Focus | `FocusRing` via `StandardControlPaint.DrawFocusRing` | All controls; the HTML preview's links and document |
| Selection | List `SelectedBackground` (`AccentSoft`; outlined in high contrast) | Message list |
| Feedback | `InlineFeedback` kinds (`Info`, `Success`, `Warning`, `Danger`) | Every form and the inbox |
| Spacing | 4/8/12/16/24/32 (`SpacingXs` to `SpacingXxl`); reading margins from `ReadingColumn` | Views and forms |

- Inventory result: per-view divergence was small. Labels set `Foreground = StandardControlPaint.Text`
  (redundant with their role, removed), the reader subject captured `FontTitle` at construction (now a
  text style that follows theme changes), and the preview's cut-off banner used a fixed "Segoe UI 11"
  (now the caption font). The banner's colors and the HTML page canvas stay fixed light colors on
  purpose: they sit on the document's white page.
- **Broiler.UI gap, fixed upstream (Broiler.UI#75, released in 0.1.0-preview.16):** the theme had a type scale
  that no control used (body 13, while controls drew 16), and nothing applied the system text size,
  although Broiler.Hosting reads it. Now: the type scale is ranked around the body size controls
  draw; `StandardThemeTokens.WithTextScale` scales every font, and `Select(UiSystemSettings)` applies
  the system's; text controls and labels start from and follow the theme's fonts until the app sets
  its own (`StandardThemeFonts`, `StandardLabel.TextStyle`); list rows (font-aware
  `GetItemHeight`) and tab headers (`EffectiveHeaderHeight`) grow with the font. Default sizes are
  unchanged.
- **Mail:** `AppearancePolicy` applies the system text size for every theme choice, so changing the
  Windows text size reflows the running app. At large text the reader header and the list notice are
  capped (45 % and 40 % of their pane, `BoundedScrollArea`) and scroll, so the message text and the
  list keep their room. Demo options `--text-scale <100-225>` and `--contrast high` let the acceptance
  pass check enlarged text and the high-contrast palette without changing system settings.
- Evidence: Broiler.UI `ThemeTypographyTests` (7 cases); Mail `AppearanceTests` (text size for every
  choice; a live change enlarges the shell, including the subject), `ResponsiveInboxTests` (200 %
  text keeps the header and notice within their share), and the demo option tests. Acceptance runs on
  a NativeAOT build with Broiler.UI packed locally: 150 % text 32/32 and 200 % text 32/32 clean (the
  first 200 % run found cut-off tab names, overlapping rows, a header over the footer, and a notice
  that left the list no room, all fixed above); high contrast 64/64 clean. Repeated on the published
  preview.16: the full pass 96/96, text 150 % and 200 % 32/32 each, high contrast 64/64, all clean.
- Open: long translated labels need a string layer first (UI-14); a density choice is not offered
  (the list supports it, nothing else needs it yet).

Baseline observations from native captures of the Debug build (100% DPI, light 1100×720
and dark 640×480). They feed the packages named; they are not acceptance results:

| Fixture | Observation | Package |
| --- | --- | --- |
| `long-message`, all reading | Subject, sender, and received date share one label with equal weight (resolved by UI-03) | UI-03 |
| `large-inbox` at 640×480 | The sender truncates to a few characters, while the date keeps its full width (resolved at this size by UI-04 compact mode) | UI-04 |
| `large-draft` at 1100×720 | Instruction rows and Cc/Bcc push Subject and the body editor below the fold (resolved by UI-05) | UI-05 |
| `send-unknown` at 640×480 | Feedback has its own scrollbar; To is clipped; the footer reports draft retention instead of the unknown send | UI-05, UI-08 |
| Composer fixtures | The same status appears in both inline feedback and the shell footer (resolved for the composer by UI-05) | UI-08 |
| Dark fixtures | The native title bar stays light (resolved by UI-07) | UI-07 |
| `long-message` | Mixed RTL lines and wrapping of unbroken URLs render; emoji are monochrome, and the ZWJ sequence is not a single glyph | UI-13 (record only) |

### UI-02 — refresh without losing the reader's place

**Owner:** Mail view model and view state. Maps to A3 / EX-07.

- [x] Reconcile refreshed summaries by full message key, including account/mailbox
  identity and UIDVALIDITY. Update metadata without treating a retained body as a new body.
- [x] Preserve selected identity, list anchor plus within-row offset, body scroll,
  and text selection when the underlying content remains unchanged.
- [x] Preserve an open HTML preview for the same valid body; close it for an actual
  account/message change or invalidation.
- [x] Distinguish a message missing from the fetched page from confirmed deletion.
  Keep already loaded context within existing bounds or explain that it is outside
  the refreshed page; do not invent a server deletion from page absence.
- [x] Define empty inbox, changed UIDVALIDITY, account switch, canceled refresh,
  failed refresh, and late completion transitions. Retain the existing generation guard.

**Files:** [InboxViewModel](../src/Broiler.Mail.Application/ViewModels/InboxViewModel.cs),
[InboxView](../src/Broiler.Mail.Application/Views/InboxView.cs),
[ScrollableMessageText](../src/Broiler.Mail.Application/Preview/ScrollableMessageText.cs).

**Accept:** successful and failed refresh preserve valid reading context; new/reordered
rows do not cause a jump; identity changes cannot display another account's content.
Use state-transition tests plus a native scroll/selection check. Add upstream anchor
support if the published ListView cannot express stable restoration.

**Progress (2 October 2026):** implemented in Mail without upstream changes.

- `InboxViewModel.KeepReading` reconciles the open message with a refreshed newest page by
  its full key (account, mailbox, UIDVALIDITY, UID). If the message is on the page, its summary
  is updated (for example its read flag) while the `Body` instance is kept, so the view does not
  close the HTML preview or scroll the reader back to the top.
- If it is missing, the outcome depends on what the page proves. The newest page is contiguous,
  so a UID at or above the oldest fetched UID, a page that covers the whole mailbox, or an empty
  inbox means it is no longer in the inbox: the reader closes and says so. A lower UID is only
  outside the page: the message stays open, can be read again, and the status suggests Load older.
  A different UIDVALIDITY closes the message as renumbered. None of these claims a deletion.
- The view enables the published `ListView.EnableScrollAnchoring`: when scrolled, the first
  visible row and its offset stay in place as rows arrive; at the top, new mail is visible. The
  reader text is only rewritten when it changes, which keeps the text selection.
- Unchanged: account switch clears the reader; cancel and failed refresh keep everything; the
  generation guard rejects late completions.
- Evidence: `RefreshContinuityTests` (eight cases; the view test fails if either anchoring or
  the unchanged-text guard is removed) and the updated `InboxWorkflowTests`. A native check of
  `--demo inbox` scrolled the reader, refreshed with F5, and kept the message and scroll offset.
  New-row anchoring under real input and DPI has only been tested headlessly so far.

### UI-03 — a readable message surface with nearby reply actions

**Owner:** Mail. Maps to A4 / EX-03 / EX-05.

- [x] Replace the single combined header label with a wrapping subject heading,
  sender/address details, quieter timestamp, and optional expandable metadata.
- [x] Make addresses and header values selectable/copyable without converting `&`
  into mnemonics. Preserve a usable narrow layout for long unbroken addresses.
- [x] Apply reading margins and a bounded text column using existing layout controls.
- [x] Put Reply, Reply all, and Forward near the message. Route both reader and
  composer buttons through the same application command behavior.
- [x] If a draft already exists, preserve it and reveal the existing composition with
  an explanation. Do not replace it or create a discard/send side effect from navigation.
- [x] On successful New/Forward, focus To; on Reply/Reply all, focus the body.
  Return navigation restores the prior reader selection and focus target.

**Files:** `InboxView`, [MailShellView](../src/Broiler.Mail.Application/Views/MailShellView.cs),
[ComposerView](../src/Broiler.Mail.Application/Views/ComposerView.cs),
[ComposerViewModel](../src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs).
Extract a reader header/command binding only if that makes ownership clearer; it is
an implementation choice, not a required new shared control.

**Accept:** read → reply takes one action, reply recipients/threading stay unchanged,
an existing draft survives, and keyboard/screen-reader focus lands in the intended field.

**Progress (2 October 2026):**

- The reader header is now a subject heading (theme title font, wrapping, literal `&`), a
  read-only, selectable details editor (From, then To and Cc once the body's headers have
  loaded), and a muted "Received … · Read/Unread on server" line. Collapsed details are
  skipped by Tab. Expandable full metadata was not added; nothing currently needs it.
- [`ReadingColumn`](../src/Broiler.Mail.Application/Views/ReadingColumn.cs) gives the header
  and the message text the same left edge: 24-DIP margins, or 12 DIP when the pane is narrower
  than 480 DIP, and a 720-DIP (about 80-character) line limit. The scroll view still spans the pane.
- [`CompositionCommands`](../src/Broiler.Mail.Application/ViewModels/CompositionCommands.cs) is
  the single path for New, Reply, Reply all, and Forward, used by the reader and the composer.
  The reader's actions stay enabled while a draft exists. Choosing one keeps that draft, opens
  Compose, and the composer's status explains that the draft was retained.
- The shell opens Compose and focuses To (new message or forward) or the body (reply, or a retained
  draft), revealing it in the form's scroll view. Returning to Inbox restores focus to the reader
  control used before composing. The selection is unchanged.
- Evidence: `ReaderReplyTests` (9 cases) and the existing composer reply/threading tests. Native
  Debug captures of `long-message` at 1600×900 and `inbox`/`html-only` at 640×480 dark. The reply
  click itself has only been checked headlessly; screen-reader focus belongs to UI-09.
- Moved to other packages: at 640×480 the header takes much of the reading pane (UI-04 compact
  mode). Reply keyboard shortcuts belong to UI-10.

### UI-04 — responsive inbox and shell

**Owner:** Mail; generic responsive layout/anchor APIs in Broiler.UI when needed.

- [x] Preserve the wide split view and user's splitter choice. Switch to one pane
  when both minimum readable widths cannot fit; retain the wide split ratio separately.
- [x] Provide a labeled Back to inbox action in compact reader mode and restore the
  list anchor and selected row. Browser-style back behavior must not discard a draft.
- [x] Keep two-line rows readable: ellipsize the less important field first, avoid
  date/sender overlap, and expose full information through selection and semantics.
- [x] Wrap/overflow toolbars predictably. Keep existing tabs and shortcuts initially;
  avoid a navigation redesign unrelated to the reader improvement.
- [x] Preserve tab, focus, and view state across resize and DPI transitions. Do not
  destroy and recreate editors merely because a breakpoint changed.

**Files:** `InboxView`, `MailMessageItemPresenter`, `MailShellView`,
[TabContent](../src/Broiler.Mail.Application/Views/TabContent.cs).

**Accept:** minimum-size, wide, and 200% text-scale views have reachable actions,
no overlapping rows, and no lost draft/selection on repeated resize.

**Progress (2 October 2026):**

- [`AdaptiveInboxLayout`](../src/Broiler.Mail.Application/Views/AdaptiveInboxLayout.cs) shows
  one pane when the inbox is narrower than a readable list (280 DIP) plus a readable reader (400
  DIP: heading, three reply actions, and margins). The decision uses the inbox's own width, not
  a window or device breakpoint. It collapses and restores panes of the existing split container,
  so no control is recreated, and splitter events during the switch never overwrite the saved ratio.
- Compact list mode: arrow keys only move the selection (the body still loads); a click, Enter,
  or Read message opens the reader. A click is recognized because the list leaves the pointer
  release unhandled, while keyboard selection never reaches the layout; `ListView` offers no
  selection-source information, so this needed no upstream change.
- Compact reader mode: a labeled Back to inbox action above the subject; Escape also goes back
  when nothing is loading (otherwise it still cancels first). Back returns focus to the list with
  its scroll offset and selection unchanged and never touches the composer.
- A message read side by side stays open when the window narrows. If focus would be left in a
  hidden pane, it moves to the visible one (message text or list). If the open message disappears
  on refresh, the compact layout returns to the list.
- Evidence: `ResponsiveInboxTests` (6 cases) cover each of these, including three wide/compact
  resize round trips. Native Debug run at 640×480 (150% DPI): the list uses the full width, a row
  click opens the reader, and Escape restores the list with the row selected.
- Changed tests: the version-1 acceptance shell check now expects the full-width list in compact
  mode. New view tests that start drafts use a queued `TestQueueDispatcher`: with
  `ImmediateUiDispatcher`, draft autosave refreshed the composer from a pool thread during the
  test's own refresh and could clear the subject. The app itself uses the queued window dispatcher.
- Open: between 680 and about 800 DIP the side-by-side list is still narrow (35% split), so senders
  truncate; the remaining row item (shorter metadata before sender/subject) is unchanged. Not yet
  checked: 200% text scale, real DPI changes while compact, and the toolbar's framed background
  around the Back and reply actions (a UI-01 token question).

**Progress (3 October 2026, rows and toolbars):**

- Rows drop the less important part first. The sender shows in full while it fits beside the date;
  otherwise only its display name, which is then shortened if needed (`RowSender`, `SenderName`:
  a single `Name <address>` or `"Quoted, Name" <address>`; a bare address, several addresses, or
  a group stay as they are). At 200 % text the row shows "Broiler team" instead of "Broiler tea...".
- Dates from this year use the culture's month-day pattern with the abbreviated month ("Sep 27",
  "27. Sept."), so the date takes less of line 1; today's messages keep the time, older years the
  short date.
- Full information stays available: selecting a row shows the full sender in the reader, and the
  row's accessible name carries the full sender and the full received date and time
  ("Received: 9/27/2026 9:30 AM") instead of the shortened list date.
- Toolbars already used `UiToolbarOverflow.Wrap`; it is now verified. In every tab at 640x480
  (normal and 200 % text) and 1100x720 (200 % text), each toolbar stays inside the window, keeps its
  actions in reading order (same row to the right, or the next row), and never cuts an action
  below its desired width; at the minimum size with 200 % text at least one toolbar wraps.
- Evidence: `MessageRowTests` (sender name parsing, full sender when wide, name only when narrow,
  no sender/date overlap at 280 DIP and doubled text, accessible name, abbreviated month in two
  cultures) and `ToolbarWrapTests`. Native NativeAOT runs of five inbox fixtures: 20 of 20 clean at
  normal text and 10 of 10 at 200 %, screenshots reviewed.
- Open: the date is never shortened, so in a row narrower than the list's 280 DIP minimum it could
  run past the edge (Broiler.UI's two-line presenter clamps it right of the sender); real DPI
  changes while compact remain a hardware check.

### UI-05 — make writing occupy the composer

**Owner:** Mail; reusable sizing/focus behavior in Broiler.UI.Forms/RichEdit.

- [x] Let the body consume the remaining viewport height rather than always requesting
  a 300-DIP editor under an instruction-heavy stack.
- [x] Use a compact sender line and concise recipient/subject fields. Move secondary
  instructions into contextual help without hiding the sender or send outcome.
- [x] Retain collapsed Cc/Bcc summaries and automatically reveal populated/recovered
  fields. Collapsing a section must preserve its values and move focus safely.
- [x] Keep Send, Check draft, Save draft, and Discard reachable at every supported
  size. Keep submission, Sent-copy, and draft-storage feedback distinct.
- [x] Make the body own normal editing scroll; allow outer scrolling when enlarged
  header fields genuinely exceed the viewport. Avoid two scrollbars moving the same area.
- [ ] Preserve caret/selection/undo and IME composition while status messages update.
  Ordinary refresh must not reload the entire draft into the editor.
- [x] Show durable autosave state unobtrusively; keep failed/conflicting saves visible
  and actionable. Navigation/closing must retain the current save guard.

**Files:** `ComposerView`, `ComposerViewModel`, `ConfigurationForm`; reuse the
[C-04 contracts](c04-forms/README.md).

**Accept:** sustained typing, paste, Cc/Bcc expansion, background autosave, errors,
resize, and tab changes preserve text and caret. Unknown SMTP acceptance never turns
into an ordinary retry or an automatic resend. No network test is needed for layout;
use injected outcome states plus existing submission regression tests.

**Progress (2 October 2026):**

- [`FillLastStack`](../src/Broiler.Mail.Application/Views/FillLastStack.cs) gives the body
  everything below the header fields, with a 160-DIP minimum. The form viewport already arranges
  content at least as tall as itself, so the outer scrollbar appears only when the header fields plus
  that minimum do not fit; otherwise the body's own scrollbar is the only one.
- Removed the two permanent instruction rows and the "Selected message" line. Address format and
  "plain text only" moved into field placeholders. A muted hint appears only without a draft
  (how to reply, or why a message cannot be replied to). The New/Reply/Reply all/Forward row
  is hidden while a draft exists, because none of them can apply, and returns after Discard.
- The sender line now also carries the routine autosave state ("Saving draft…", "Draft saved
  locally."); only a storage failure uses inline feedback. "Configure outgoing mail" became
  `ComposerViewModel.SendUnavailableReason`, shown only when it actually prevents sending.
- Duplicate status removed: informational composer messages go to the shell footer only; check
  results, warnings, and errors stay inline beside the draft. Editing clears a stale result instead
  of repeating "Draft edited." on every keystroke. The first account assignment no longer replaces
  "Recovered your saved draft." with the retention notice, which now appears only when the
  account actually changes. Expanded Cc/Bcc no longer repeats its summary.
- Evidence: `ComposerLayoutTests` (8 cases: fill at 1100×720, all actions visible at 640×480,
  selection kept through status/account/autosave updates, inline vs footer status, the starting row,
  Cc/Bcc summary, the send hint, and the recovery message). Native Debug captures of `large-draft`
  at 1100×720 (body fills, no outer scrollbar with Cc/Bcc expanded) and `send-unknown` at 640×480
  dark (the warning is the only inline message).
- Open: IME composition and undo across status updates have not been exercised (UI-10). At
  640×480 the header plus minimum body still needs outer scrolling.

### UI-06 — account setup and settings that explain the next step

**Owner:** Mail; C-04 controls already supply the structure. Maps to A6 / EX-08.

- [x] Add a first-account flow with explicit stages: identity/server details,
  save profile, credentials, connection test, optional outgoing setup, ready to receive.
- [x] Keep a direct advanced-edit mode for existing accounts. Do not automatically
  guess provider settings or silently alter transport/security choices.
- [x] Make unsaved profile changes, credential binding, and test readiness clear.
  Keep password values out of summaries, notifications, screenshots, and diagnostics.
- [x] Keep per-field validation and focus/reveal behavior. Show actionable failure
  text at the relevant section instead of duplicating the same paragraph everywhere.
- [ ] Add SMTP-test UI only once a non-sending connection/authentication service exists.
  Separate receiving success from sending configuration; never send a trial message implicitly.
- [x] Simplify appearance/geometry wording when UI-07 removes the restart requirement.

**Files:** [AccountProfileView](../src/Broiler.Mail.Application/Views/AccountProfileView.cs),
[AccountProfileViewModel](../src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs),
[SettingsView](../src/Broiler.Mail.Application/Views/SettingsView.cs).

**Accept:** keyboard-only first setup has a clear next action, back navigation retains
nonsecret edits, failed validation reveals the field, and existing accounts keep their
saved configuration and credential-binding behavior.

**Progress (2 October 2026):**

- The Account tab starts with a setup checklist rather than separate wizard pages, so existing
  accounts keep the same direct form. Each line states one step's result: account details
  (including unsaved changes), the IMAP password, the connection test, and optional outgoing mail.
  One button performs or reveals the next step: save the details, move focus to the password field
  (it never types a secret), run the test, or, when everything is done, open the Inbox and receive.
  When ready, the checklist shrinks to a single line. It is not collapsible, so its button is the
  first Tab stop.
- `AccountProfileViewModel` gained `NextStep`, `HasUnsavedChanges` (the form compared with the saved
  profile, kept current as fields change), `HasPassword`/`HasSmtpPassword`, and
  `ConnectionCheck`/`ConnectionFailure`. Password presence is read from the credential store as a
  yes/no only; the secret is dropped at once and never reaches labels, status, or diagnostics. An
  unavailable store reads as unknown, not missing. Saving changed server details resets the test
  and re-checks the password binding; saving or forgetting a password resets the test; a canceled
  test is recorded as not run, a failure keeps its reason beside the step.
- The IMAP password section now follows the incoming server, before optional outgoing mail.
  Repeated instruction paragraphs were shortened. No provider settings are guessed and no
  transport or security choice changes silently.
- Evidence: `AccountSetupTests` (6 cases: the complete first-account path, detection of a stored
  password without exposing it, unsaved changes, a failed and a canceled test, new server details
  invalidating the password, and Open Inbox receiving). Native first-run capture with an empty data
  directory. Changed tests: two now find the IMAP password by its label instead of its position;
  the shell keyboard test expects the next-step button before the first field; two tests that
  treated any `Changed` without busy as "save finished" now wait for the save itself.
- Open: an SMTP connection test still needs a non-sending connection/authentication service.

### UI-07 — live appearance, accessible scaling, and window restoration

**Owner:** Mail application preferences; Hosting system settings; UI token propagation.

- [x] Subscribe once to saved preferences and OS appearance changes. Apply the theme
  to existing controls; follow OS color mode only when System is selected.
- [ ] Give high contrast and system text scaling an explicit precedence policy,
  including when a user chose Light/Dark. Verify actual system colors and readable focus.
- [x] Update reader, composer, popups, and existing preview windows consistently.
  If paint tokens are process-global, coordinate updates across window threads.
- [ ] Respect reduced motion and verify RTL layout separately from simply translating
  labels. Keep message content direction independent from shell direction.
- [x] Persist window size/placement with debounced writes; preserve restored bounds
  while maximized. Clamp restoration to an available monitor after monitor/DPI changes.
- [x] Migrate settings compatibly; preserve splitter state and tolerate old files.

**Files:** [SettingsViewModel](../src/Broiler.Mail.Application/ViewModels/SettingsViewModel.cs),
[WindowsMailWindow](../src/Broiler.Mail.Windows/Hosting/WindowsMailWindow.cs),
[WindowsUiHost](../src/Broiler.Mail.Windows/Hosting/WindowsUiHost.cs),
[ApplicationSettings](../src/Broiler.Mail.Core/Settings/ApplicationSettings.cs),
[Program](../src/Broiler.Mail.Windows/Program.cs).

**Accept:** theme changes require no restart, explicit color preference survives OS
changes, enlarged text is usable, focus/caret survive restyling, and a removed monitor
cannot strand the app off-screen. Settings errors must not interrupt typing.

**Progress (2 October 2026), live theme:**

- [`AppearancePolicy`](../src/Broiler.Mail.Application/Views/AppearancePolicy.cs) is the single
  precedence rule, used at startup and while running: an active system high-contrast mode wins
  (it is an accessibility setting); otherwise an explicit Light or Dark choice wins over the OS
  scheme; System follows the OS. Reduced motion and density always come from the system.
- [`AppearanceController`](../src/Broiler.Mail.Application/Views/AppearanceController.cs) applies it
  through the published `StandardThemeController`, which re-themes existing controls in place, so
  text, caret, selection, and scroll survive. It reacts to the host's system-settings changes and
  to saved settings only; choosing a theme without saving changes nothing.
- Muted labels (reader date line, composer hint) now use the muted label role instead of a color
  captured at construction, so they follow theme changes. The settings and footer text no longer
  promise a restart for the theme; the initial window size still applies on the next start.
- The native caption follows the palette (`DWMWA_USE_IMMERSIVE_DARK_MODE`, applied after `Show`
  because the handle does not exist earlier). This is a Mail-local helper,
  [`WindowsTitleBar`](../src/Broiler.Mail.Windows/Hosting/WindowsTitleBar.cs), marked to move into
  Broiler.Hosting.Windows.
- Evidence: `AppearanceTests` (8 cases: precedence for each preference, high contrast and reduced
  motion, and a live shell where an OS change, an unsaved selection, a saved choice, and a later OS
  change produce exactly three re-themes while the composer text and selection survive). Native
  Debug run: started dark, chose Light in Settings with the keyboard, saved; the whole window, the
  open message, and the caption switched to light without a restart.
- Upstream limits found in Broiler.Hosting.Windows `WindowsTheme`: `TextScale` is always reported
  as 1.0, and high contrast maps to the preset palette rather than the user's actual system colors.
  Both are fixed, with `WindowsTitleBar`, on the unreleased Broiler.Hosting branch
  `claude/windows-theme-system-settings`. Until a package containing it is consumed, Mail keeps
  its local caption helper and the preset high-contrast palette.
- Open: existing HTML preview windows run their own session and are not re-themed yet (done 3 October, below); RTL layout.

**Progress (2 October 2026), window geometry:**

- `ApplicationSettings.Window` (a `WindowPlacement`) stores the normal outer bounds in physical
  screen pixels, for comparing with monitor work areas, the client size in DIPs, for creating the
  window, and whether it was maximized. The property is optional, so files written before it
  existed load unchanged with the same schema version; validation bounds it to the virtual-screen
  range and the existing size limits. The unused `InboxSplitterFraction` now actually restores the
  inbox split and is saved with the window.
- `SettingsViewModel.RememberLayoutAsync` saves quietly: no status, validation, or busy change, so
  resizing never interrupts typing; writes are serialized and store the newest settings; failures
  are kept in `LayoutSaveError`; an unreadable settings file is never overwritten. An explicit save
  keeps the remembered layout, but saving a new initial size clears it so that size is used.
- The window saves once per move or resize (`WM_EXITSIZEMOVE`), on maximize or restore, and on close
  (awaited before the window closes). While maximized or minimized the last normal bounds are kept.
- On start, [`WindowGeometry`](../src/Broiler.Mail.Windows/Hosting/WindowGeometry.cs) keeps the
  position only if at least 120×16 pixels of the title bar lie in a monitor work area (a caption
  under the top edge is pulled down); otherwise the window moves onto the work area it overlaps
  most, or is centered on the primary one. Upstream note: Broiler.Graphics clamps option
  coordinates to at least one pixel, so positions on monitors left of or above the primary one are
  applied with `SetWindowPos` after `Show`.
- Evidence: `WindowGeometryTests` (8 cases, including negative coordinates, a removed monitor, and
  oversize windows) and `WindowLayoutPersistenceTests` (5 cases: quiet round trip, explicit-save
  interaction, an old file without the property, an unreadable file, an invalid layout, and the
  restored split). Native run with an isolated data directory at 150% DPI: moved to 200,150
  1400×900 and closed; reopened exactly there; maximized, closed, reopened maximized with the
  normal bounds kept; a placement edited to −9000,−9000 reopened centered on the primary monitor.
- Not yet checked: a real second monitor being disconnected, and moving between monitors with
  different DPI before closing.

**Progress (3 October 2026), preview windows and precedence:**

- An open HTML preview follows theme and text-size changes. It runs its own session on its own
  thread, so the main window passes each applied theme to `WindowsHtmlPreviewHost.ApplyTheme`,
  which posts it to the preview's thread: the preview's session theme and controls (header, buttons,
  plain text) change, its surface and caption follow, and the HTML page keeps its own white canvas.
  The preview re-themes only its own session: the main window has already set the process-wide
  palette, and an older theme still queued on the preview thread must not overwrite a newer one.
- Reader, composer, and in-window popups were already consistent: they are controls of the main
  session and re-themed by `AppearanceController`.
- Precedence, as `AppearancePolicy` now states it: an active system high-contrast mode wins over
  an explicit Light or Dark choice; the system text size applies under every choice and in high
  contrast; reduced motion and density always come from the system.
- Evidence: `HtmlPreviewKeyboardTests.AnOpenPreviewFollowsAThemeAndTextSizeChangeOnItsOwnThread`
  (a real window: a dark, 150 % theme applied from another thread reaches the session, the button
  and status fonts, and the surface; the process-wide palette is untouched). Native run of
  `--demo html-only --theme light`: opened the preview, chose Dark in Settings and saved; the
  open preview's caption, header, and button turned dark without reopening.
- Open: the HTML document's own text does not follow the system text size (the snapshot renderer
  uses the document's CSS sizes at the display scale, and the preview offers no zoom yet); actual
  Windows contrast colors and RTL layout.

### UI-08 — coherent loading, empty, error, and recovery states

**Owner:** Mail view-state presentation. Reuse `InlineFeedback`; retain domain outcomes.

- [x] Define each surface's idle/empty/loading/ready/canceled/failed state and its
  valid actions. Keep usable content visible during non-destructive background work.
- [x] Put the explanation and retry/cancel action beside the affected pane. Keep the
  shell footer concise rather than repeating every section's full feedback text.
- [x] Deduplicate announcements: background autosave must not continually interrupt
  a screen reader. Errors and submission outcome changes still need announcements.
- [x] Separate transient success from persistent warnings. Critical unsaved/unknown
  states stay visible until resolved; decorative success may disappear without losing context.
- [x] Specify focus after validation, cancel, retry, disclosure collapse, and recovery.
  Async completion must not steal focus from typing or move the active tab unexpectedly.

**Accept:** fixture coverage includes empty inbox, refresh failure with old data,
body failure, invalid setup, canceled test, failed/conflicting autosave, rejected send,
unknown send, and failed Sent copy. Every state has accurate text and valid actions.

**Progress (2 October 2026):**

| Surface | States and placement after this work |
| --- | --- |
| Inbox list | Before the first receive: "Receive mail to load your inbox." Loaded and empty: "The inbox is empty." While receiving: progress above the still-visible list. Failed: the reason plus a note that the rows are from the last successful receive, with **Retry receiving** beside it. Canceled: information, not an error, with Retry. |
| Message | Loading: "Loading message body…". Failed or canceled: the reason under the header with **Retry loading**; the earlier "Use Read message to retry" instruction is gone. Choosing another message replaces the earlier problem. |
| Composer | Done in UI-05: results, warnings, and errors inline; routine information only in the footer; autosave quiet on the sender line. |
| Account | Done in UI-06: each step states its result; a failed connection test keeps its reason beside the step. |
| Footer | Points to the pane when an inline problem exists ("Details and Retry are beside the list"), instead of repeating the full explanation. |

- `InboxViewModel` reports `Problem`, `ProblemScope` (list or message), `ProblemIsCancellation`,
  `CanRetry`/`RetryAsync` (repeating the same page or the selected message), `HasLoaded`,
  `IsLoadingList`, and `IsLoadingMessage`. The existing `Status` text is unchanged for compatibility.
- Focus: a Retry that disappears after success moves focus to the list or message text; a Cancel
  button that becomes disabled moves focus to Receive mail (or the list).
- A new `body-error` gallery fixture shows a failed message with its Retry.
- Evidence: `InboxStateTests` (5 cases: empty before and after receiving, failed refresh with kept
  rows, Retry and focus, failed message and Retry, cancel as information with focus repair, and a
  new selection replacing a message problem), the `body-error` gallery test, and native captures of
  `receive-error` and `body-error`.
- Open: focus after validation and disclosure collapse was handled in UI-03 to UI-06 but has no
  single cross-surface test yet.

**Progress (2 October 2026, announcements and transient success):**

What a screen reader is told, per surface. Every announcement comes from `InlineFeedback` (when its
text or kind changes) or, for a finished receive, from the inbox view.

| Surface | Announced | Silent |
| --- | --- | --- |
| Inbox list | Receiving progress; "N messages loaded." when it finishes; a failure or cancellation | Moving through messages and loading their bodies; refreshes that change nothing visible |
| Composer | Sending progress; the submission outcome; the Sent copy outcome; the server's reason for a rejection; validation and storage errors | Typing, autosave (the sender line and footer), and the routine "result saved" line |
| Account, Settings | Saving or testing progress; the result | The confirmation going away |

- Fixed duplicates found by the new tests: while sending, the composer announced "Submitting
  message…" as well as the submission line's "Sending…"; after a failed send it announced
  "Updating draft…" and a second error, "Submission result saved. The draft is retained." The busy
  line now appears only for an unsubmitted draft (discarding), and the closing line is a problem only
  when the result was not stored or the server gave a reason; otherwise it is footer information.
- Transient success: an Account or Settings confirmation (for example "Settings saved.") clears after
  `SaveViewModel.SuccessDisplayTime` (6 s), restarting if another result arrives. What was saved
  stays visible elsewhere: the applied settings, or the account checklist. Failures, cancellations,
  progress, and every composer outcome (submission, unknown send, Sent copy) stay until resolved. The
  footer then returns to its hint; a ready account now says "This account is ready to receive mail."
  instead of asking for details again.
- Native: the Hosting provider raised a live-region event without a `LiveSetting`, which Narrator
  ignores, and a client reading the event got the source element's name, not the announced text.
  Broiler.Hosting branch `claude/status-notifications` raises a UIA notification that carries the
  text, one activity per source element (a newer status replaces a queued one; errors are
  `ImportantMostRecent`), and reports `LiveSetting` Polite or Assertive on status elements. Checked
  with that branch packed locally (`0.1.0-preview.4-local.1`): an external UIA client received
  "Progress: Receiving newest messages…" and "50 messages loaded." in the `inbox` demo, and the
  error on the same activity, as `ImportantMostRecent`, in `receive-error`. Published as
  Broiler.Hosting 0.1.0-preview.4 (Broiler.Hosting#3); Mail consumes it, and the same check against
  the published package gives the same notifications.
- Evidence: `FeedbackPolicyTests` (typing with autosave announces nothing; receiving announces
  progress and the result, and moving through messages is silent; a failure is announced; each send
  outcome once, including a server reason; confirmations go away, restart, and are not announced
  when they do; failures stay; the ready-account footer).
- Open: a real screen-reader pass (H-01), now that Mail consumes the Hosting release; the composer's
  "Draft fields are valid." check result still stays until the next edit.

**Progress (2 October 2026, states, focus, and fixtures):**

Each surface's states and the actions valid in them:

| Surface | State | Shown | Valid actions |
| --- | --- | --- | --- |
| Inbox list | Idle (never received) | "Receive mail to load your inbox." | Receive mail |
| | Loading | Progress above the list; earlier rows stay | Cancel |
| | Ready / empty | Rows / "The inbox is empty." | Receive mail, Load older, select |
| | Failed / canceled | Reason (error) or "canceled" (information) beside the kept rows | Retry receiving, Receive mail |
| Message | Loading / ready | "Loading message body…" / the text | Read message; Reply, Reply all, Forward when ready |
| | Failed / canceled | Reason under the header | Retry loading, choose another message |
| Composer | No draft | Start actions | New message; reply actions with a loaded message |
| | Editing | The draft; autosave on the sender line | Send (when available), Check draft, Save draft, Discard draft |
| | Sending | Submission progress | None |
| | Failed (rejected) | Outcome plus the server's reason | Edit, Send again, Discard |
| | Unknown / accepted | Outcome; a Sent copy outcome if requested | Save draft, Discard draft (no Send, no edit, no Check) |
| | Storage failed or conflicting | The storage error; the edits stay open | Save draft (retry), keep editing |
| Account | Unsaved / invalid | Checklist step; the field error beside the field | Save account |
| | Testing / canceled / failed / passed | Progress; "canceled" as information; the reason beside the step; Ready | Cancel while testing; Test connection otherwise |
| Settings | Saving / failed / saved | Progress; the field error; a confirmation that goes away | Save settings |

Focus, for every surface (results that arrive later never take focus or the tab):

| Event | Focus |
| --- | --- |
| Validation failure | Moves to the field, scrolled into view, only while the form is on screen and focus is in it, on a container holding it, or nowhere (`FocusNavigation.MayTakeFocus`); otherwise the field is only marked |
| Cancel becomes unavailable | From Cancel to Receive mail or the list (inbox), or to Test connection (account) |
| Retry disappears after success | To the list or the message text |
| Section collapses with focus inside | To the section's toggle (Broiler.UI `FormSection`) |
| Recovered draft at start | The Compose tab opens on it |
| Background completion (receive, autosave, save, test) | Unchanged; the active tab is unchanged |

- Found and fixed: a settings or account save that failed validation after the user had switched
  tabs moved focus to the hidden field. Now `ConfigurationForm.BindFeedback` asks `MayTakeFocus` first.
- Footers no longer repeat an Account, Settings, or composer storage problem: they name it ("Not
  saved.", "Connection test failed.", "The draft is not saved.") and add "Details are below the
  buttons." (`SaveViewModel.StatusSummary`). **Check draft** is offered only while the draft can change.
- Fixtures for the acceptance list: the existing `empty`, `receive-error`, `body-error`, and
  `send-unknown`, plus five new ones driven through user commands: `invalid-setup` (email address with
  a display name, typed into the field), `test-canceled`, `draft-conflict` (autosave refused by another
  instance), `send-rejected` (synthetic server reason; the demo sender still never reports
  acceptance), and `sent-copy-failed` (a recovered accepted record, like `send-unknown`). Native
  captures (light 1100×720, dark 900×600) show no clipping. `draft-conflict` refuses to close, as
  intended, because its draft cannot be saved.
- Evidence: `DemoGalleryTests` (5 new cases checking text and valid actions) and `FeedbackPolicyTests`
  (a result after moving on keeps focus and tab; validation takes focus from the Save button or the
  tab strip; collapsing copies moves focus to the toggle; footer pointer; no Check on an accepted draft).

### UI-09 — native accessibility, from discovery to text editing

**Owner:** Broiler.Hosting.Windows provider; Broiler.UI semantics; Mail integration.
Maps to A1 / EX-10 / H-01. This is a release acceptance blocker.

- [x] First reproduce the published app's empty control tree using an external client.
  Trace subclass attachment, render-HWND `WM_GETOBJECT`, host provider, and return codes.
- [ ] Verify native COM interface discovery, vtables, marshalling ownership, fragment
  navigation, UI-thread dispatch, disposal, and runtime IDs. Compare Raw/Control/Content
  views; missing view flags alone have not been established as the cause.
- [x] Expose stable names/roles/labels and correct supported patterns for the current
  controls. Keep collapsed/removed controls out of navigation; retain virtual row identity.
- [x] Add the text/range and selection behavior needed to read and edit messages with
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

**Progress (2 October 2026):**

- Reproduced with an external System.Windows.Automation client (`--demo`, Debug and NativeAOT
  builds alike): the window exposed one empty render pane.
- **Root cause, in Mail:** `WindowsMailWindow` constructed `WindowsAutomationBridge` and
  `WindowsInputBridge` in its constructor, before Broiler.Graphics creates the native windows at
  `Show`. Both received zero handles, so neither subclassed anything: `WM_GETOBJECT` reached
  `DefWindowProc`, and the input bridge's IME, surrogate, and precision-wheel handling never ran
  (text still arrived through the window's own `WM_CHAR` path). Earlier theories (COM view flags,
  marshalling) were not the cause. The bridges and the dark caption are now created in
  `OnCreated`, which runs during `WM_CREATE` once both windows exist.
- **After the fix, external client, Debug and published NativeAOT (win-x64, no AOT warnings):**
  220 elements; tabs with Invoke/SelectionItem; Receive mail invoked without coordinates; the
  message list with 50 named rows ("Unread · subject — sender") selected through SelectionItem; the
  reader text readable through Value; the password field reports `IsPassword` and an empty value.
- Regression test: `NativeBridgeAttachmentTests` shows a hidden real window, checks that the
  automation bridge is attached to the render window, that a cross-thread `WM_GETOBJECT` returns a
  provider, and that one posted character reaches the focused To field exactly once. It fails
  when the bridges receive zero handles.
- **Upstream fixes consumed (2 October 2026):** Broiler.UI 0.1.0-preview.12 (accessible names,
  `LabeledBy` from `UiLabel.Target`, `IsHiddenFromAccessibility` for inactive tab content; Broiler.UI
  ADR 0027) and Broiler.Hosting 0.1.0-preview.3 (name/LabeledBy/HelpText/Value mapping, layout
  nodes outside the Control view, hidden-content filtering, Text pattern, change events). Mail
  names the message list ("Messages") and the read-only reader editors ("Message text", "Sender
  and recipients").
- **External client, Debug and published NativeAOT (win-x64, no AOT warnings), identical results:**
  - Control view has no layout panes or class names: 20 elements before receiving, 75 with a
    message open, 28 on Compose, 58 on Account. Only the selected tab's content is exposed (Send
    is absent on Inbox; the reader is absent on Compose).
  - Receive mail invoked by name; the "Messages" list has 50 named rows; row selection through
    SelectionItem opens the message.
  - The reader supports Value and Text: document range, word and paragraph units, `FindText`, and
    selecting a found range (the selection reads back "Hello").
  - Edits are named by their labels with the text as Value and the placeholder as HelpText
    (To: LabeledBy "To", HelpText "name@example.com, another@example.com"; Email address: Value
    "reader@example.test"). The password field reports `IsPassword`, an empty Value, and no Text
    pattern.
- **Still open (H-01):** focus, selection, and busy/error/status announcements as heard by a real
  screen reader (Narrator or NVDA); validation-to-field mapping and expanded/collapsed state of the
  Cc/Bcc disclosure. A paragraph move from the first paragraph lands on the blank separator line,
  which a screen reader reads as blank; acceptable, but worth confirming with a real reader.

### UI-10 — keyboard, native text input, and scroll behavior

**Owner:** Mail commands/focus policy; Hosting/Input native event fidelity.

- [x] Review the hard-coded focusable-control list and use semantic/control traversal
  where supported. New controls should not silently fall out of Tab navigation.
- [x] Preserve Ctrl+1–4, Ctrl+Tab, F5, Escape, Enter-on-message, and standard edit keys.
  Define new reply/back shortcuts once at the shell command layer and display them consistently.
- [x] Keep splitter/overflow/disclosure actions reachable by keyboard. Reveal focused
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

**Progress (2 October 2026):**

- `MailShortcuts` is the single shortcut table. `MailKeyboardNavigation` matches key presses
  against it, the Settings tab lists it in a collapsed "Keyboard shortcuts" section, and the README
  and demo text follow it. New: Ctrl+N (new message), Ctrl+R / Ctrl+Shift+R / Ctrl+F (reply, reply
  all, forward; consumed even when unavailable, so the chord never reaches an editor), and
  Alt+Left (back from the narrow-window reader; passes through otherwise).
- **Bug fixed:** shortcuts only checked that Ctrl was held. AltGr arrives as Ctrl+Alt, so AltGr+2
  and AltGr+3 (² and ³ on a German layout) switched tabs instead of typing. Modifiers must now
  match exactly, side-specific flags count, and Windows-key chords are left to the system.
- **Bug fixed:** the adaptive inbox collapses a pane by arranging it to an empty rectangle while it
  stays `Visible`, so Tab could focus invisible list or reader controls in a narrow window.
- Tab traversal now uses `CanFocus` and `IsTabStop` instead of a list of control types, skips
  collapsed, hidden-by-container, and arranged-away subtrees, orders by `TabIndex` with a stable
  sort, and includes the splitter (keyboard-resizable, but it does not report itself focusable)
  between its panes. A scroll view of read-only content stays a stop so the keyboard can scroll it.
- Tests: `KeyboardShortcutTests` (table uniqueness, AltGr and Windows-key chords, the reply
  shortcuts and their focus targets, consumed unavailable replies, Tab stops in wide and narrow
  layouts, Alt+Left, Settings list).
- **Native, demo build:** Tab order list → splitter → reader details → Reply → Reply all → Forward
  → message text → tabs → toolbar; on the splitter Right×3 moves it 35 % → 41 % and Home to its
  minimum. In a 640×480 window Tab cycles only the visible controls, all with non-zero bounds.
  Posted `WM_CHAR` into the To field: a, é, ², @, a surrogate pair (😀), Ctrl+R and Ctrl+A control
  characters, b → exactly "aé²@😀b".
- **Broiler.UI 0.1.0-preview.13 consumed:** the splitter is focusable while enabled, split
  containers keep their children in visual order, collapsed panes are hidden from accessibility, and
  `StandardFocusScope` skips hidden content with a stable order (Broiler.UI#71). Mail's splitter,
  visual-order, and empty-bounds workarounds are gone; only the read-only scroll view stays a Mail
  policy. Native checks repeated with the same results, and a narrow window now also hides the
  collapsed reader and splitter from UI Automation clients.
- Open: IME composition with a real IME, precision and horizontal wheel, caret position across a
  DPI change, and dead keys typed on a real layout (posted messages cannot carry modifier state,
  so Ctrl and AltGr chords were checked headlessly only). `WindowsInputBridgeTests.EndToEnd_RichEdit_HandlesImeCompositionAndCommit`
  failed once in 13 full runs: Hosting suppresses the duplicate `WM_CHAR` after an IME commit only
  within 500 ms of wall time, which a loaded test run can exceed.
- Broiler.UI 0.1.0-preview.17 consumed (3 October): Shift+wheel scrolls scroll views sideways, a
  horizontal wheel scrolls rich text sideways, and a list that cannot scroll further leaves the wheel
  to its container (Broiler.UI#76). Mail has no horizontally scrolling content today, so the change
  matters for the reader at large text and future wide content. The UI-12 `scroll` workload on the
  NativeAOT build: 300 of 300 wheel steps painted, no errors. A physical tilt wheel or precision
  touchpad is still unchecked.

### UI-11 — predictable HTML preview presentation

**Owner:** Mail preview UX; shared HTML rendering APIs when required.

- [x] Keep a clear message identity, Back to text/Close action, and remote-image state.
  Preserve useful error information without exposing internal implementation details in the UI.
- [x] Distinguish initial load, canceled load, partial/resource-limited content, blocked
  remote resources, failure, and ready. Keep retry explicit and prevent automatic crash loops.
- [x] Preserve zoom/scroll during harmless status updates; invalidate them deliberately
  when the message, document geometry, or DPI changes.
- [x] Align typography, theme, focus, shortcuts, and scaling with the shell. Keep
  external navigation user-initiated and subject to the existing URL policy.
- [ ] After the renderer process boundary passes its acceptance gate, design an inline
  text/HTML toggle in the reader with stable message identity and plain-text fallback.

**Files:** [HtmlMessagePreview](../src/Broiler.Mail.Application/Preview/HtmlMessagePreview.cs),
[HtmlPreviewWindow](../src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs),
[WindowsHtmlPreviewHost](../src/Broiler.Mail.Windows/Preview/WindowsHtmlPreviewHost.cs).

**Accept:** selection change/cancel/close cannot reopen an obsolete preview; large or
failed documents leave text usable; resource approval belongs to the current document.
Inline embedding remains blocked by the separate renderer containment gate.

**Progress (2 October 2026):**

- `IHtmlPreviewHost` reports each message's preview phase (`Open`, `Closed`, `Unavailable`,
  `Canceled`) through `Changed`, keyed by message, and exposes `Current`. The reader's action
  follows it: Opening… while the window starts, **Close HTML preview** while it is open, and the
  outcome as status ("HTML preview closed. The text preview remains here."). Changes for another
  message are ignored, a rebuilt reader whose preview is open offers Close, and a replaced reader
  unsubscribes. Before, the status kept saying "opened" after the window closed, and the
  re-enabled button answered "Close the existing HTML preview before opening another."
- `WindowsHtmlPreviewHost` keeps one window at a time: a request for another message closes the
  open window and waits for its thread before opening the next; closing or replacing a preview that
  is still opening cancels it before its window appears.
- **Bug fixed:** the window title never named the message. The host called `SetTitle` before the
  native window existed (handles are 0 until it is shown), so every preview was titled
  "Broiler.Mail — HTML preview". The title ("Broiler.Mail — HTML snapshot — subject") is now
  passed at construction.
- The preview window follows the shell theme: dark caption (DWM immersive dark mode) and the theme
  surface behind its header, where dark-theme text had been drawn on the white window. The HTML
  document keeps a white page canvas, as mail is authored for one. Escape closes the window unless a
  control used the key. A document longer than the render budget shows a header notice that the
  preview is shortened and Show plain text has all of it.
- Tests: `HtmlPreviewActionTests` (phase following, stale opening results, failures before a
  window, other-message changes, rebuilt reader, unsubscribe, messages without HTML).
- **Native, demo build, dark theme:** open → title names the message, caption dark, reader offers
  Close with the open status; Escape in the preview and Close HTML preview each close it and the
  reader reports closed; selecting another message closes the preview; returning shows a fresh
  Open action. Header and plain-text view readable in the dark theme.
- Open (at the time): zoom and scroll preservation, keyboard scrolling and a Tab path inside the
  preview, UI Automation for the preview window, the truncation notice on a real long document, and
  replacing an open preview with another message's.

**Progress (2 October 2026, keyboard, screen readers, and scaling):**

- **Bug fixed, display scaling:** every preview tile after the first was painted from the wrong place
  at any display scale other than 100 %. Broiler.HTML takes the scroll offset in layout units and
  applies the zoom itself; the preview multiplied the offset by the scale as well, so at 150 % a long
  message's text ran out two-thirds of the way down and the rest of the preview was blank (at 200 %
  it ran out halfway). Messages shorter than one 1,024-DIP tile, like the old demo, were unaffected.
- **Bug fixed, reflow:** making the preview narrower did not reflow the document. The width the view
  set on the document did not invalidate its cached layout, so the old width stayed and a horizontal
  scrollbar appeared.
- **Keyboard:** the document is focused when the window opens, so arrows, Page Up/Down, Home/End,
  Space and Shift+Space scroll it at once. Tab and Shift+Tab cycle through the header buttons, the
  document, and each link (or the plain text), using the shell's own tab-stop order
  (`MailKeyboardNavigation.TabStops`). A focused link scrolls into view and opens with Enter; links
  still open only on a user action and through the existing URL policy. Hiding the HTML or the text
  moves focus from the hidden view to its replacement; a failure moves it to the text. Escape still
  closes the window. Focus rings frame the focused link and, for the document, the viewport.
- **Screen readers:** the preview window now has the UI Automation bridge. Its tree is the status
  text, the buttons, an "HTML message" group, and one Hyperlink per link the policy would open, named
  by the link's text (taken from the sanitized HTML; the address if it has none) and invokable.
  Broiler.HTML reports only the first line of a link that wraps, so its target covers that line;
  clicks on later lines still open it.
- **Reading position:** a status change (for example the remote-image result) keeps the scroll
  offset; a reflow at another width keeps the reader at the same relative place; a new document
  starts at the top. The preview has no zoom of its own; it follows the display scale.
- **Shell alignment:** the header uses the reader's `ReadingColumn` margins and line length.
- **Fixture:** `long-html` selects a newsletter taller than the 32,768-DIP render budget with a link
  per section, followed by a short HTML message.
- Tests: `HtmlPreviewKeyboardTests` (named hyperlink targets for openable links only, Enter opens,
  a wrapped link is one target, keyboard scrolling, Tab stops and reveal, reading position across a
  status change, reflow and a new document, the window's start focus, Tab cycle, and view switching,
  and tile content at 100, 150, and 200 %, which fails with the old offset), and the `long-html`
  gallery case.
- **Native, 150 %, light and dark (Debug; the UIA and Tab checks also on a NativeAOT publish, which
  has no trimming or AOT warnings):** UIA shows the group and the `example.test` hyperlink with Invoke;
  focus starts on the document and Tab goes document → link → Show plain text → document; narrowing the
  window to 520 pixels reflows without a horizontal scrollbar. In `long-html` the shortened-preview
  notice is in the header, 272 hyperlinks are exposed (those within the budget), End shows section
  272 directly above the cut banner (before the fix it showed the document's last section over
  blank space), selecting the next message closes the preview, and opening that one is titled
  "Short HTML note".
- Open: an inline reader view (blocked by renderer containment, as before); a preview zoom control.

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
- [x] Add tile pixel/byte bounds in addition to the existing count limit; higher DPI
  and width must not create an effectively unbounded memory allowance.
- [ ] Verify idle rendering stops, active input remains responsive, and performance
  changes preserve invalidations, selection, text accuracy, and resource disposal.

**Accept:** retain comparable before/after measurements and reproducible scenarios.
Do not substitute screenshot inspection or unit-test counts for latency/memory evidence.

**Progress (2 October 2026):**

- Harness: `--demo <scenario> --measure <workload> [--report <file>]` runs a fixed workload on a
  prepared fixture and records, per frame, the UI-thread build time (layout and render list),
  managed allocation, and input-to-frame latency, plus startup to first frame and to interactive,
  working set, and GC counts. Workloads: idle, scroll (500-row list, wheel), select, type (with
  autosave), theme, resize, splitter. `scripts/Measure-UI.ps1` publishes NativeAOT, runs each
  workload three times, and summarizes.
- First baseline, with method, reference machine, and proposed targets:
  [ui-performance-baseline-2026-10-02.md](ui-performance-baseline-2026-10-02.md). Idle draws no
  frames. Resize is the expensive case (UI side 14–16 ms per step, 1.7 MB per frame); select and
  splitter allocate 1.6 MB and 0.9 MB per frame while building in 5–9 ms.
- Not measured yet: GPU rendering and presentation (Broiler.Graphics `FrameRendered` arrives only in
  its next release), native input delivery, text-layout calls, HTML tile misses, long HTML, and other
  DPI scales.
- Optimization 1 (Broiler.UI 0.1.0-preview.14, Broiler.UI#73; Mail consumes it, and a run against the
  published package reproduced the local-pack numbers):
  allocation sampling (`AllocationSampler`, `BROILER_MAIL_SAMPLE_ALLOCATIONS=1`) traced most frame
  allocation to the rich-text editor rebuilding fonts per run, boxing run enumerators per character,
  and re-shaping visible lines every frame. With fonts resolved once per style, index loops, and line
  segments kept across frames: allocation per frame drops 64–80 % (select 1,620 → 310 KB, resize
  1,718 → 408 KB, scroll 285 → 102 KB), scroll and theme frames build 33–69 % faster, and resize and
  select are now compute-bound (text re-wrapping).
- Optimization 2 (Mail, HTML preview): tiles went to the renderer as PNG, encoded and decoded again
  at 314 ms per typical tile (557 ms at 2896×2896) against 1–2 ms for a pixel copy; they now go as
  pixel buffers, so a new tile no longer stalls the preview. Tiles are capped at 8 M pixels (lower
  scale only above the cap) and the cache at 256 MB by bytes as well as count; before, 7680 DIPs at
  300 % allowed about 4.5 GB. Tests: tile sizing at four width/scale pairs and a 4000-DIP, 250 %
  document scrolled end to end within the byte budget.

### UI-13 — published-app acceptance and evidence

**Owner:** Mail integration/CI. This closes the UI release milestone, not just a build.

- [x] Maintain the matrix below as results tied to a revision, package versions,
  architecture, SDK, monitor/DPI, and test method.
- [x] Keep focused behavior tests for state transitions, data preservation, and native
  regressions. Use visual review for spacing/hierarchy rather than brittle tests of every pixel.
- [x] Exercise the packaged NativeAOT executable with an isolated demo/test profile;
  headless `--smoke-test` remains a useful but limited composition check.
- [x] Check screenshots for clipping/overlap and native interaction for focus/input.
  Synthetic examples must cover long strings, Unicode, errors, and populated forms.
- [x] Record unmet platform checks explicitly. A test in a sibling source checkout
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

**Progress (2 October 2026):** the first pass is recorded in
[ui-acceptance-2026-10-02.md](ui-acceptance-2026-10-02.md).

- `scripts/Accept-UI.ps1` publishes NativeAOT and runs every gallery fixture at 640×480, 1100×720,
  and 1920×1080 in both themes (96 runs): screenshot, UI Automation checks (unnamed controls,
  clipping, overlap), a Tab walk, exit code, and stderr, tied to the revision, packages, SDK, OS, and
  display scale.
- Result: 96 of 96 runs without automated findings after the fixes; screenshots reviewed.
- Found and fixed in Mail: an invisible, unannounced tab stop on every form's feedback area (now a stop
  only while it scrolls, and named), and misaligned `--help` columns.
- Found and fixed in Broiler.UI (Broiler.UI#74, published as 0.1.0-preview.15): a stale-layout band
  above the compact inbox list, caused by measure invalidation stopping early, and field errors left
  under the action bar by `FormSurface.Reveal`.
- Second pass after Mail moved to Broiler.UI preview.15 and Broiler.Hosting preview.4: again 96 of 96
  runs without automated findings, and the screenshots no longer show either Broiler.UI defect.
- Pending with documented blockers: 100 % and 200 % scale and monitor moves, text scale, high
  contrast, reduced motion, physical mouse/touchpad, AltGr and IME, screen-reader speech, ARM64
  hardware, and right-to-left layout (UI-14).

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
