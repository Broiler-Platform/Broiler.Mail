# Broiler.Mail UI implementation roadmap

Created **2 October 2026**, with implementation records through **5 October**.
Status: implementation plan; unchecked work is not yet fully accepted, and combined
checkboxes can include already delivered subitems. Original baseline: commit `6bdcb83`.
See the [5 October status](roadmap-status-2026-10-05.md) for current cross-roadmap
status and the [5 October acceptance record](ui-acceptance-2026-10-05.md) for the native
runs; the [4 October consolidated audit](roadmap-status-2026-10-04.md) records the state
before the 4–5 October round. This document is the execution plan for UI work;
the [experience review](experience-roadmap.md) remains the original design rationale.

The 4–5 October records describe two Mail branches (pushed to `origin` on 5 October; no pull
request yet), stacked on `main`
(`757e81b`). Each record says which one it belongs to:

- **Published stack:** `claude/ui-13-acceptance`, built on the published Broiler.UI
  0.1.0-preview.17 and Broiler.Hosting 0.1.0-preview.5. Its code tip is `12ceded`: the full native
  runs used `4c36950`, and `12ceded` adds one composer fix (UI-08), checked with the full suite and
  a native recheck of the composer fixtures.
- **Adoption branch:** `claude/ui-09-upstream-adoption` (code verified at `e870135`, 38 commits
  above `4c36950`, since rebased onto the acceptance branch; see the
  [adoption record](ui-upstream-adoption-2026-10-05.md)). It pins Broiler.UI 0.1.0-preview.18,
  Broiler.Hosting 0.1.0-preview.7, and Broiler.Native 0.1.0-preview.7. Native preview.7 is on
  NuGet; UI preview.18 and Hosting preview.7 are not published yet. The branch was verified only
  with local packs: Broiler.UI `0.1.0-preview.18-local.7` from its local
  `claude/roadmap-integration` at `fd7657f`, and Broiler.Hosting `0.1.0-preview.7-local.5` from
  `c388a66`. Work marked "prepared upstream" is on those local Broiler.UI and Broiler.Hosting
  branches. Before the adoption branch can merge, the user publishes UI preview.18 and then
  Hosting preview.7, and the suite and the native checks are rerun against the published packages.

The revisions are the code that was built and tested; documentation commits added on top change
no code. The rebase onto `12ceded` adds the composer fix to the adoption branch's code; the rerun
on the published packages covers it.

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

No package below is closed: each has at least one acceptance item that has not passed,
waiting for hardware, the user, a package release, or (UI-14) later services. The
[5 October status](roadmap-status-2026-10-05.md#mail-ui-work-packages-ui-01-through-ui-14)
gives each package's status (Delivered, Acceptance pending, Partial, Open).
“Partial” means existing implementation is reused, not that the remaining acceptance
criteria have passed. P0 is an acceptance blocker; P1 improves the current app; P2
depends on larger product/platform work. The state column says where each part lives:
the published stack, the adoption branch (upstream-ready, local packs only), or open.

| ID | Priority | Work | State on 5 October 2026 | Dependencies |
| --- | --- | --- | --- | --- |
| UI-01 | P1 | Shared presentation tokens and fixture gallery | Published stack: tokens, text scaling, density, and a 22-fixture gallery. Adoption branch: selection, state, accent-text, and scrollbar roles for contrast palettes. Open: long-label fixture (UI-14) | None |
| UI-02 | P1 | Refresh continuity and stable selection | Published stack: implemented; native new-mail check 3/4 (UIA return focus open). Adoption branch: 4/4, stable row IDs. Open: real DPI | None |
| UI-03 | P1 | Reader hierarchy and local reply commands | Published stack: implemented, with header row rules and pinned reply commands. Open: screen-reader check (UI-09) | UI-01, UI-02 |
| UI-04 | P1 | Responsive inbox and shell navigation | Published stack: compact mode, 200 % text, narrow-row dates, readable split minimums, wrapping toolbars, simulated DPI changes. Open: real DPI change and monitor moves | UI-01, UI-02, UI-03 |
| UI-05 | P1 | Writing-focused composer | Published stack: layout, undo and posted-message IME checks, problems first, refused field marked. Open: real IME | UI-01; integrate with UI-03 commands |
| UI-06 | P1 | Guided account setup and concise settings | Published stack: setup checklist and non-sending SMTP sign-in test. Open: live provider check (user) | UI-01 |
| UI-07 | P1 | Live appearance and geometry persistence | Published stack: live theme, text size (preview zoom included), geometry, Hosting caption, reduced-motion policy. Adoption branch: system contrast palette, checked with synthetic Windows palettes. Open: real contrast theme, monitors, RTL | UI-01 |
| UI-08 | P1 | Consistent state, feedback, and recovery UX | Published stack: done, including transient check result, cross-surface focus test, kept list problems, session-limit notice, composer footer pointer to a refused draft. Open: screen-reader check with UI-09 | Apply to UI-02 through UI-07 |
| UI-09 | P0 | Native accessibility and semantic integration | Published stack: external-client acceptance passes. Adoption branch: disclosure, validation, row names, bounds, runtime IDs mapped (Probe-Uia 6/6). Open: package release, real screen-reader check | Start immediately; verify every delivered surface |
| UI-10 | P1 | Keyboard, IME, scrolling, and focus fidelity | Published stack: shortcuts, traversal, posted-input fidelity tests, password IME off. Adoption branch: inline IME, Alt-chord, and tilt-wheel fixes. Open: physical input checks | Coordinate with UI-04, UI-05, UI-09 |
| UI-11 | P1 | HTML preview ergonomics | Published stack: lifecycle, identity, theme, keyboard, UIA, scaling, and page zoom 50–300 %. Open: inline view (sandbox) | UI-01, UI-08; inline embedding also needs sandbox |
| UI-12 | P1 | Measured rendering and memory performance | Published stack: phase timers, long-HTML and zoom workloads, proposed budgets; 5 October measurements recorded. Open: reference machine, GPU time, text-layout counts, real DPI | Capture baseline first; repeat after affected changes |
| UI-13 | P1 | Native visual and interaction acceptance | Published stack: 132/132 matrix, reader, 200 % text, and preset contrast runs clean. Adoption branch: full matrix, reader, and 200 % text; the preset and four Windows contrast palettes on 5 fixtures (10/10 each), with local packs. Open: hardware and screen-reader rows | Continuous; final gate for UI-01 through UI-12 |
| UI-14 | P2 | Platform and later-feature UI adaptations | Planned; localization skipped this round by the user's decision; the rest waits for services | Product/platform services and UI-13 foundation |

### UI-01 — presentation tokens and a deterministic UI gallery

**Owner:** Mail view composition; reusable roles in Broiler.UI.

- [x] Inventory current token usage and remove per-view color/font divergence through
  shared roles: heading, body, secondary text, separator, focus, selection, and feedback.
- [x] Establish the spacing/type hierarchy above in the existing demo or an isolated
  fixture harness. Use synthetic messages only, with fixed dates for reproducible images.
- [x] Include long subject/address, empty inbox, 500 messages, plain/HTML-only mail,
  large draft, failed save, send outcome unknown, and narrow window.
- [ ] Include long translated labels (needs the UI-14 string layer).
- [x] Add compact/comfortable density only where the consumed controls support it;
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
  list keep their room (refined on 4–5 October: the header ends between rows, see UI-03; the notice
  keeps Retry in view, see UI-08). Demo options `--text-scale <100-225>` and `--contrast high` let the acceptance
  pass check enlarged text and the high-contrast palette without changing system settings.
- Evidence: Broiler.UI `ThemeTypographyTests` (7 cases); Mail `AppearanceTests` (text size for every
  choice; a live change enlarges the shell, including the subject), `ResponsiveInboxTests` (200 %
  text keeps the header and notice within their share), and the demo option tests. Acceptance runs on
  a NativeAOT build with Broiler.UI packed locally: 150 % text 32/32 and 200 % text 32/32 clean (the
  first 200 % run found cut-off tab names, overlapping rows, a header over the footer, and a notice
  that left the list no room, all fixed above); high contrast 64/64 clean. Repeated on the published
  preview.16: the full pass 96/96, text 150 % and 200 % 32/32 each, high contrast 64/64, all clean.
- Open: long translated labels need a string layer first (UI-14).

**4 October — inbox row spacing:** Settings → Appearance now offers Comfortable (the
existing default) and Compact. Saving applies the choice to the existing inbox list
through the published `StandardListView.Density` API; text size, reader selection,
selected message and body are retained, and the previous first visible row stays
in view where the scroll range permits. This preference changes inbox rows only.
Unsaved choices and failed saves leave the current spacing in place. The generated
JSON serializer stores the enum by name; settings without the property retain the
comfortable default, and unknown/numeric values are rejected without overwriting
the file. Layout persistence retains the preference.

Validation: `InboxDensityTests` covers the settings control/save path, failed saves,
live reflow at 640 and 1100 DIPs with 100%/200% text, reading context, theme changes,
restart/persistence, and invalid values. All 429 solution tests pass (312 shared,
114 Windows, 3 Linux adapter); Windows x64 NativeAOT publish and four-tab smoke pass.

Native screenshot review at 200% text exposed fixed-height combo boxes clipping
their selected text (including the existing Theme field). `AppearanceController`
now sizes Mail's combo fields and popup rows from the applied font through their
existing sizing properties. This small compatibility adjustment can be removed when
the shared control measures its font automatically; it does not change row density
or reduce text size.

Native follow-up: `Accept-UI.ps1` (Windows PowerShell) on the published x64 binary,
`save-error` Settings fixture at 640×480 and 1100×720, light/dark, 200% text on a
150% display: **4/4 runs without automated findings**. Screenshots reviewed; the
visible combo text fits after the adjustment. At minimum size the form scrolls and
keyboard focus reveals its controls. Evidence: `artifacts/density-acceptance-final/`.
These checks cover Settings layout, UIA and posted Tab input; compact inbox reflow
and save/restart behavior are covered by the integration tests above.

**Progress (4–5 October 2026, fixtures, spacing, and contrast roles):**

- Published stack: the gallery has 22 fixtures, the 16 earlier ones plus `smtp-test-failed` and
  `smtp-test-passed` (UI-06), `receive-canceled`, `load-error`, and `draft-invalid` (UI-08), and
  `new-mail` (UI-02), each reached through ordinary commands. `large-inbox` now ends at the
  500-message session limit (its demo mailbox holds 600 messages; the newest row is "Sample
  message 600"), and `html-only` ends with a table and an unbroken address. The fixture item is
  ticked; long translated labels are split off and wait for the string layer (UI-14).
- Published stack, spacing found in the screenshot reviews: the footer is inset 12 DIP left and
  right (the tab names' text inset) with 4 DIP above and below; the list's Retry row and the
  reader's Reply, Retry loading, and Back to inbox rows are inset like toolbar commands; a 1-DIP
  divider in the `Border` color separates the reader header from the message text.
- Prepared upstream; verified with local packs; Mail adoption on `claude/ui-09-upstream-adoption`:
  Broiler.UI adds `SelectionText`/`SelectionTextMuted` and `IsHighContrast` (ADR 0029),
  `StateFill`/`StateText` for hovered, checked, and pressed states (ADR 0029), `AccentText`
  (ADR 0031; Dark `#7AB7FF`, Light `#0A61BE`, at least 4.5:1 on Surface, SurfaceAlt, and
  AccentSoft in every preset), and scrollbar track and thumb roles (ADR 0033). On the adoption
  branch `MailMessageItemPresenter` uses `UiListItemRenderContext.WithItem`, so selected rows keep
  the selection's text colors and Dark's selected unread dot is AccentText (6.27:1 instead of
  2.76:1). Mail's combo-box sizing workaround is removed there, because `StandardComboBox` sizes
  the box, its rows, and its arrow slot from the font; the published stack keeps the workaround.
  Visual defects the reviews found in the toolkit are fixed on the same branches: the tab view
  clips its page inside the frame, rings only the selected header, and marks it with a 2-DIP bar
  (ADR 0031); the list strokes its frame after the scrollbar, draws the unread dot at 3:1 on its
  fill, and keeps its focus ring visible across an opaque thumb; form fields keep room for their
  focus ring, and feedback starts 4 DIP below the action strip (ADR 0034).
- Not changed, for the user to decide (preset look): Light and Dark scrollbar thumbs (1.9–2.4:1),
  the splitter grip (2.3–2.6:1), and unfocused input borders (1.3–1.4:1) stay below 3:1. All
  decisions left for the user are listed in the
  [5 October status](roadmap-status-2026-10-05.md#decisions-for-the-user).
- Open: long translated labels (UI-14); the preset contrast decisions above; screenshot baselines
  after the upstream packages are published.

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
  New-row anchoring under real input and DPI has only been tested headlessly so far (native
  new-mail check on 4–5 October, below).

**Progress (4–5 October 2026, new mail and the native refresh check):**

- Published stack: new fixture `new-mail` (Receive, open message 55, Receive again). Its demo
  server (`DemoMailbox`) adds three newer messages on every later receive and marks the open
  message read, so its row changes from Unread to Read while the body and reader position are
  kept. The acceptance-only `--server-change vanish|outside|renumber` makes the next receive
  delete the open message, push it below the newest page, or change UIDVALIDITY.
- Native check [`scripts/Accept-Refresh.ps1`](../scripts/Accept-Refresh.ps1) (NativeAOT,
  1100×720 light, 150 % display). Kept: three rows arrived above the scrolled view; the selected
  row kept its identity and screen position and became Read; the partly scrolled first row kept
  its offset; the reader kept its subject, text, text selection, and pixels after a wheel had
  moved about 15 % of them. Vanish: the reader closed with "no longer in the inbox". Outside: the
  message stayed open and the status suggests Load older. Renumber: the message closed. Reply by
  click focused the body, and a click on the Inbox tab returned focus to Reply. Review made the
  status, subject, wheel, and row-open checks able to fail.
- Wording: "Server read/unread flags are unchanged." read as a false claim once another client had
  changed a flag. The status now says "Reading does not mark messages as read on the server."
- Final runs, 5 October: 3 of 4 clean on the published stack. The remaining finding,
  RETURN_FOCUS_AUTOMATION, is the UI Automation path only. Debugger breakpoints in the published
  app showed that UIAutomationCore calls the tab provider's SetFocus before Select, and Hosting
  preview.5's tab SetFocus selects the tab and then focuses the tab view. Return focus is
  therefore accepted for pointer input on the published stack. The same run reports the Inbox tab
  item as an equal 412-px share of the strip, not its header.
- Prepared upstream; verified with local packs; Mail adoption on `claude/ui-09-upstream-adoption`:
  with Hosting preview.7 the refresh run is 4 of 4 clean, the Inbox tab reports its header's
  bounds (95 px), and row runtime IDs stay with their message (Probe-Uia: 47 of 50 kept across a
  refresh). Broiler.UI ADR 0029 anchors on the nearest surviving row when the top row goes; that
  case (the outside burst) is not separately checked in Mail.
- Evidence: `NewMailFixtureTests` (25) and the native runs above.
- Open: on the published stack, the anchor row leaving the page keeps the numeric offset. A DPI
  change outside a move loop is remembered only at close or a window state change. Real DPI
  changes during a refresh (UI-13 hardware).

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

**Progress (4–5 October 2026, reader header and commands):**

Found in the native screenshot reviews and fixed on the published stack:

- The header still takes up to 45 % of the reader and scrolls the rest, but it now ends at a
  sensible place. It shows the whole header if the message text still keeps six lines; otherwise
  it grows to the bottom of the next row of buttons under the same condition; otherwise it ends
  above the row its share would cut, or between two lines of the subject, From/To, or the date.
  Without message text (body failed or canceled) it may take the whole reader, so the problem and
  **Retry loading** stay reachable. A header that grows to show Reply's row keeps a 4-DIP gap
  above the divider.
- Beside the list, Reply, Reply all, and Forward are pinned below the header where they fit on
  one row and still leave the subject's first line and six text lines; otherwise they end the
  header, as in the compact reader. Before, a 700×480 window left the subject 25 DIP and the
  text a third of a line.
- A 1-DIP divider separates the header from the text; the Reply, Retry loading, and Back to inbox
  rows use the toolbar's padding.
- The date line wraps only after "Received" or after its separator: the date, its time, and the
  separator are joined by non-breaking spaces, and the read state ("Unread on server") is one
  phrase. Before, a wrapped line could start with "·" or split "10:00" from "AM".
- Without a selected message the date line and the empty HTML-preview row collapse, so the
  heading is the header's only row. A received empty inbox reads "The inbox is empty." with "Use
  Receive mail to check for new messages."; while a receive runs, the empty reader no longer
  asks for Receive mail.
- Evidence: `ResponsiveInboxTests.TheReaderHeaderEndsBetweenItsRows` and
  `BesideTheListTheCommandsEndTheHeaderWhereTheyWouldWrapAndKeepTheirFocus`;
  `DemoGalleryTests.Reader_Header_Ends_Between_Its_Rows`, which uses the app's DirectWrite metrics
  and matched the header heights UI Automation read from the published app; a sweep of 5,300
  reader sizes (five text scales, five kinds of message, two widths, heights 480–900). Native
  `-OpenReader` runs: 20 of 20 without findings in the final run (UI-13).
- Adoption branch (local packs): the reader's "Message text" and "Sender and recipients" editors
  draw a focus ring, and "Message header" is a toolkit keyboard stop (UI-09).
- Open: screen-reader focus (H-01). At 640×480 with 200 % text the compact reader keeps about 2.35
  lines of message text. Beside the list at 200 % text, Open HTML preview stays in the scrolling
  header above the pinned Reply, while the compact reader shows Reply first; aligning them needs
  the preview row restructured. The header can still end between the two lines of the date.
  Cosmetic, found by the fifth visual review (UI-13): in the compact reader at 640×480, when the
  header is capped below the Reply strip (`html-only`, `long-html`), 1 physical pixel of the next,
  hidden row's top border shows just above the divider, so the divider looks doubled. The cap ends
  exactly where that row starts; ending it 1 DIP above would change layout invariants pinned by
  the header tests named above and the 5,300-size sweep, so it is left for a follow-up.

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
`TabContent` (kept on the published stack; removed on the adoption branch, see the 4–5 October
record).

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
  changes while compact remain a hardware check. (Shorter dates added 4 October, below.)

**Progress (4–5 October 2026, narrow rows, split widths, and simulated DPI):**

- Published stack, narrow rows: when the date does not fit beside the sender's first four
  characters and "...", the row steps down. This year: "Sep 27", then the culture's numeric
  month/day, keeping the period where the language writes "27. September" ("27.09."). Older
  years: the short date, then the year. Today: the time. If even the shortest form does not fit,
  the row leaves the date out; the full date stays in the row's semantic name and in the reader.
  Patterns are derived once per culture. Rows narrower than 280 DIP did occur: the split's old
  180-DIP minimum and the 35 % default produced them.
- Published stack, split: the pane minimums are now the readable widths (list 280, reader
  400 DIP; before 180 and 220). A narrow window that clamps the split no longer overwrites the
  saved ratio, which returns when the window widens. The compact switch is unchanged (below
  680 DIP) and does not count the 8-DIP splitter, so from 680 to 687 DIP the panes are up to
  8 DIP under their readable widths; moving the threshold needs the user's sign-off.
- Published stack, simulated DPI changes: `WindowsMailWindow.SimulateDpiChange` sends an
  in-process `WM_DPICHANGED` through the same handlers as a real change. `DpiTransitionTests` runs
  compact mode with a message open and a draft through 1.5 → 2.0 → 1.0 → 2.0 → 1.0 and checks the
  focus target, selected message, list anchor, reader scroll, both text selections, and that
  compact or split follows the DIP width. While a scale is simulated, the minimum size uses
  Windows' real frame and the maximum track size is lifted, so the test also holds on a 1024×768
  desktop. Demo `--scale <100-300>` (main window only) opens at the requested DIP size.
- `TabContent` stays on the published stack, for a new reason: Broiler.UI preview.17's tab view
  arranges hidden tabs at an empty rectangle, which returned a hidden form's status area to its
  top. `ShellLayoutTests.ATabIsAsItWasLeftAfterAnotherTabWasShown` fails without the wrapper.
  Broiler.UI ADR 0030 (prepared upstream) keeps a hidden tab's arrangement, and the adoption branch
  removes `TabContent`.
- Found headless: after the compact reader opened, the inbox subtree could stay arrange-invalid
  under a valid tab view, so a later scroll moved the scrollbar but not the text. Native wheel runs
  on preview.17 showed that it is not visible on these paths (opening the reader re-measures every
  ancestor). Broiler.UI ADR 0030 now carries arrange invalidation to the root (prepared upstream).
- Native UIA row names on the published stack come from the row text and lack the received date;
  on the adoption branch rows are named by the presenter with the full date (UI-09).
- Evidence: `MessageRowTests` (en-US and de-DE forms, 126 narrow-row cases, the narrowest list at
  225 %), `ResponsiveInboxTests` (700/780 and 680/684/687 DIP), `DpiTransitionTests` (3),
  `WindowGeometryTests` (+2).
- Open: real DPI changes while compact and moves between monitors (UI-13 hardware).

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
  640×480 the header plus minimum body still needs outer scrolling. (Undo and posted-message IME
  checks added 4 October, below; a real IME remains.)

**Progress (4–5 October 2026, undo, IME, and composer problems):**

- Published stack, undo:
  `ComposerLayoutTests.TypingKeepsItsUndoHistoryThroughStatusAutosaveAccountInboxAndDisclosureUpdates`
  types through the editor while autosave, a check, an account change, an inbox refresh, and
  Cc/Bcc toggling run, then shows that Ctrl+Z and Ctrl+Y undo and redo only the typing. No
  production change was needed.
- Published stack, IME with posted messages (no real IME): `ComposerImeTests` (2) show that a
  composition in the body survives a held autosave, Check draft, an account change, and an inbox
  receive; its commit is typed once and saved, and a canceled composition leaves the draft as it was.
- Published stack, problems first: the capped Status and errors area now lists errors, then
  warnings, then the rest, and a new error or warning scrolls the area to its top without moving
  focus. At 200 % text it had shown only "Sending is not available in this mode." above the error.
- Published stack, refused recipient field: after Check draft or Send refuses a recipient,
  `ComposerViewModel.InvalidField` names the field (To, Cc, or Bcc) and `ComposerView` marks it
  through `FormField.SetError`, as Account and Settings do; while the user is on the form, Cc and
  Bcc are shown if needed and the field takes focus. Refusals of the whole draft (subject, body,
  too many recipients) name no field, and any newer status clears the mark. On Broiler.UI
  preview.17 the `FormField` group reports Invalid with the error in its name. With preview.18
  (adoption branch, local packs) the edit itself reports Invalid and its description starts with
  the error; Probe-Uia reads
  IsDataValidForForm False and FullDescription "Error: Enter valid email addresses separated by
  commas." on To.
- Prepared upstream; verified with local packs; Mail adoption on `claude/ui-09-upstream-adoption`:
  `FormViewport` keeps the focused field in view when its viewport shrinks (Broiler.UI ADR 0030);
  feedback starts 4 DIP below the action strip and a field's focus ring keeps 1 DIP of room inside
  the form (ADR 0034). Mail's own Tab reveal keeps that room too
  (`KeyboardShortcutTests.TabAndShiftTabScrollAFieldInWithRoomForItsRing`, adoption branch).
- Checklist: the caret/undo/IME item stays open until a real IME is checked (UI-10).
- Open: a real IME. On the published stack at 640×480 with 200 % text, a line shown below the
  composer's buttons (for example the check confirmation) shrinks the form's view from 232 to
  136 DIP and can hide the body being typed in until the line goes; the ADR 0030 fix arrives with
  Broiler.UI preview.18.

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
- [x] Add SMTP-test UI only once a non-sending connection/authentication service exists.
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
- Open: an SMTP connection test still needs a non-sending connection/authentication service
  (done 4 October, below).

**Progress (4–5 October 2026, non-sending SMTP sign-in test):**

- Published stack: the Account tab can test the SMTP sign-in without sending anything
  ([decision 0006](decisions/0006-non-sending-smtp-test.md)). A separate Core contract,
  `IOutgoingConnectionTester`, kept apart from `IMailSender`, is implemented by
  `SmtpConnectionTester`. It checks everything it can before using the network, reads only the
  SMTP credential slot, connects with implicit TLS or required STARTTLS within a 20 s deadline,
  and runs AUTH and a best-effort QUIT (at most 2 s). It never sends MAIL, RCPT, DATA, BDAT, VRFY,
  EXPN, ETRN, RSET, or NOOP, never retries, and reports a caller cancel as canceled in every
  phase. `MailConnectionException` gained a `MailConnectionFailure` kind; IMAP is unchanged.
- **Test SMTP sign-in** sits beside the SMTP password buttons and is enabled once the SMTP
  password is saved. Its result stands only on the checklist's outgoing line ("Done — Outgoing
  sign-in tested; no message was sent."); `NextStep` never depends on it. Each protocol's result
  resets only with its own server details or password. Cancel test and Escape stop either test,
  and focus returns to the button of the test that ran, scrolled into view; a test that finds no
  saved SMTP password moves focus to the SMTP password field.
- Bugs found in review and fixed: an unanswered TLS handshake was reported as a certificate
  problem (now a timeout); a test pressed with unsaved edits was recorded as failed while the
  checklist said Done, so a test that cannot start is now refused with what to do first ("The
  test did not start."), for **Test connection** as well; the demo credential store returned a
  placeholder secret, replaced by the presence-only `ICredentialStore.ContainsAsync`.
- Evidence: `SmtpConnectionTests` (31, against a loopback server whose strict mode fails on any
  submission command), `SmtpSetupTests` (10),
  `FeedbackPolicyTests.AnSmtpTestAnnouncesProgressAndTheResultOnceAndNeverTakesFocus`,
  `CredentialWorkflowTests.SavedPasswordsAreCheckedForPresenceWithoutReadingTheSecret`,
  `Version2AcceptanceTests.SmtpSignInTestThroughTheAccountFormSendsNothing` (TLS and STARTTLS,
  pinned certificate, counting sender), and the `smtp-test-failed` and `smtp-test-passed`
  fixtures. NativeAOT Accept-UI subsets had no findings and the demo process made no remote
  connections; both fixtures are in the final 132-run matrix (UI-13).
- Checklist: the SMTP-test item is ticked; the live provider check stays with the user.
- Open: the live provider check in the [SMTP checklist](version-2-smtp-checklist.md) (TLS on 465
  and STARTTLS on 587, nothing delivered, no Sent item); a UIA notification count for the SMTP
  result; the screen-reader pass (H-01). The IMAP receiver still reports a handshake cut off by
  its deadline as a TLS verification failure.

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
  because the handle does not exist earlier). This was a Mail-local helper, `WindowsTitleBar`,
  marked to move into Broiler.Hosting.Windows. Since 4 October Mail calls Broiler.Hosting.Windows'
  `WindowsTitleBar.ApplyDarkMode` (Hosting preview.5) and its own copy is deleted (below).
- Evidence: `AppearanceTests` (8 cases: precedence for each preference, high contrast and reduced
  motion, and a live shell where an OS change, an unsaved selection, a saved choice, and a later OS
  change produce exactly three re-themes while the composer text and selection survive). Native
  Debug run: started dark, chose Light in Settings with the keyboard, saved; the whole window, the
  open message, and the caption switched to light without a restart.
- Upstream limits found in Broiler.Hosting.Windows `WindowsTheme`: `TextScale` is always reported
  as 1.0, and high contrast maps to the preset palette rather than the user's actual system colors.
  Both were fixed, with `WindowsTitleBar`, on the Broiler.Hosting branch
  `claude/windows-theme-system-settings`, since merged (Broiler.Hosting#1) and contained in the
  Hosting preview.5 that Mail consumes. Mail's local caption helper was removed on 4 October. On
  the published stack Mail still resolves high contrast to the Broiler.UI preset palette; the
  adoption branch uses the system's contrast colors (4–5 October, below).
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
  uses the document's CSS sizes at the display scale, and the preview offers no zoom yet; done
  4 October through the preview zoom, UI-11); actual Windows contrast colors and RTL layout.

**Progress (4–5 October 2026, caption, reduced motion, restore, and system contrast colors):**

Published stack:

- The caption comes from Broiler.Hosting.Windows' `WindowsTitleBar.ApplyDarkMode` (Hosting
  preview.5) at all five call sites; Mail's copy is deleted. The behavior is the same (DWMWA 20,
  the zero-handle and build-19041 guards). `WindowCaptionTests` (2) read the caption back from DWM
  on real hidden windows (main window and preview, dark at once and following a saved theme);
  removing any one of the five calls fails a test.
- Reduced motion: nothing in Mail animates or interpolates. Mail has no animation scheduler,
  transition, progress bar, or caret blink; in Broiler.UI preview.17 no standard control creates
  the only animation path; the one time-driven change, a success confirmation removed after 6 s,
  is not motion. The system setting reaches the session theme with zero animation durations
  (`AppearanceTests.NothingInTheShellMovesOnItsOwnAndReducedMotionReachesTheSessionTheme`).
- Restore with mixed DPI (found by code reading): a window restored onto a monitor whose scale
  differs from the system DPI opened at the wrong DIP size, with a system-DPI frame, and the
  remembered size drifted on every start. During `WM_CREATE`, before the window is shown, Mail now
  sets the planned DIP client size at the window's real scale and keeps its real frame.
  Unit-tested over three starts at 100 % and 200 % beside a 150 % primary, plus the
  simulated-scale start; not observed on two monitors.
- An open HTML preview follows the system text size through its zoom (UI-11).

Prepared upstream; verified with local packs; Mail adoption on `claude/ui-09-upstream-adoption`:

- In Windows high contrast, `AppearancePolicy` takes the palette from an injected resolver that
  calls Hosting's `WindowsTheme.CreateHighContrastTheme` with the system's colors. That palette
  maps the selection text to HighlightText (7.0–8.0:1 in the Windows 11 contrast themes), sets
  `IsHighContrast`, applies the text scale, draws hovered, checked, and pressed states in the
  Highlight/HighlightText pair, falls back to WindowText for accent text that would not read, and
  draws scrollbars in WindowText on Window. The palette is resolved again on `WM_SETTINGCHANGE`,
  `WM_THEMECHANGED`, and `WM_SYSCOLORCHANGE`. `--contrast high` keeps the Broiler.UI preset;
  `--contrast aquatic|desert|dusk|night-sky` use the Windows 11 contrast themes' colors
  (`WindowsSystemColors`, which Hosting's tests check against the theme files Windows ships), and
  `Accept-UI.ps1 -Contrast` runs them.
- Readable focus: Broiler.UI ADR 0032 draws a button's focus ring in a color that stands out on
  the fill it draws in each state. Mail's tests check the drawn fill, label, and ring at rest,
  hovered, and held down, also on Hosting's contrast palettes; scripted captures of Save account and
  Test connection, focused and hovered, show a visible ring in Dark, Dusk, and Night sky. An
  interim Mail workaround for default-button rings was retired in the same branch.
- Evidence: final runs with `-Contrast` aquatic, desert, dusk, night-sky, and high, 10 of 10
  without findings each (UI-13).
- Checklist: the precedence item stays open. The policy itself exists since 2–3 October (high
  contrast wins over an explicit Light or Dark choice; the system text size applies under every
  choice), but "actual system colors and readable focus" have only been checked with synthetic
  Windows palettes. The reduced-motion/RTL item stays open for RTL.
- Known gaps of the system palette, documented in the Hosting README (the fixes belong in
  Broiler.UI): a pressed secondary button, a pressed spin arrow, and a hovered unchecked toggle
  look as at rest; marks drawn in the accent may not show in a
  custom theme whose highlight is close to its window color.
- Open: a real Windows contrast theme and a switch between two of them; OS text size and reduced
  motion changed on a real session; monitor removal and moves between monitors with different
  DPI; RTL layout (UI-14).

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
| Inbox list | Before the first receive: "Receive mail to load your inbox." Loaded and empty: "The inbox is empty." While receiving: progress above the still-visible list. Failed: the reason plus a note that the rows are from the last successful receive, with **Retry receiving** beside it (since 4 October in a row below the notice). Canceled: information, not an error, with Retry. |
| Message | Loading: "Loading message body…". Failed or canceled: the reason under the header with **Retry loading**; the earlier "Use Read message to retry" instruction is gone. Choosing another message replaces the earlier problem (since 4 October only a message problem; a list problem stays until the next page operation). |
| Composer | Done in UI-05: results, warnings, and errors inline; routine information only in the footer; autosave quiet on the sender line. |
| Account | Done in UI-06: each step states its result; a failed connection test keeps its reason beside the step. |
| Footer | Points to the pane when an inline problem exists ("Details and Retry are beside the list"; since 4 October "… above the list", where they are), instead of repeating the full explanation. |

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
  single cross-surface test yet (added 4 October, below).

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
  "Draft fields are valid." check result still stays until the next edit (transient since 4 October,
  below).

**Progress (2 October 2026, states, focus, and fixtures):**

Each surface's states and the actions valid in them:

| Surface | State | Shown | Valid actions |
| --- | --- | --- | --- |
| Inbox list | Idle (never received) | "Receive mail to load your inbox." | Receive mail |
| | Loading | Progress above the list; earlier rows stay | Cancel |
| | Ready / empty | Rows / "The inbox is empty." | Receive mail, Load older, select |
| | Failed / canceled | Reason (error) or "canceled" (information) beside the kept rows (since 4 October above them, with Retry in a row below the reason) | Retry receiving, Receive mail |
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
| Cancel becomes unavailable | From Cancel to Receive mail or the list (inbox), or to Test connection (account); since 4 October to the test that ran (Test connection or Test SMTP sign-in), scrolled into view |
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

**Progress (4–5 October 2026, feedback, focus, and list problems):**

All on the published stack.

- Transient check result: "Draft fields are valid. No mail was sent." is announced once and
  cleared silently after `SaveViewModel.SuccessDisplayTime` (6 s), like the Account and Settings
  confirmations. Another check restarts the time, a newer status cancels it, and a failed check
  stays until the next edit.
- Focus after a command disables itself (`FocusNavigation.KeepFocusUsable`, once the operation has
  finished and only while focus is on that command): Discard draft goes to New message, an accepted
  Send to Save draft, Load older on the last page to the message list (or to Back to inbox while
  the compact reader is shown alone), otherwise to the next tab stop in Tab order. The target is
  scrolled into view and must be a tab stop. The composer's own New message, Reply, Reply all, and
  Forward move focus once, straight to where writing starts. Results that arrive later still never
  take focus.
- Cross-surface focus test: `FeedbackPolicyTests.FocusLandsOnAUsableControlAfterEveryOutcome`
  runs 16 outcomes at 1100×720 and at 640×480 with 200 % text across Settings, Account,
  Composer, and Inbox (32 cases). In each, focus is on a visible tab stop of the current tab, the
  tab did not change, and Tab continues from it.
- List problems: a failed or canceled receive and a failed Load older now stay while a message is
  selected, read, reloaded, or fails to load; only the next page operation clears them. A Load
  older failure says "Older messages could not be loaded." with **Retry loading older**; a
  canceled one says "Loading older messages was canceled." The Retry row is pinned below the
  notice, inset like the toolbar, and keeps its label while a message loads. At large text only
  the explanation scrolls; it ends between lines and shows at least one whole line.
- Footer pointers: "Details and Retry are above the list." While the compact reader hides the list:
  "Mail could not be received. Use Back to inbox to see the details and Retry." (canceled: "Canceled.
  Use Back to inbox to retry."). For a message problem in either layout: "The message could not be
  loaded. Details and Retry are below its date."; in the compact list: "… Open it to see the details
  and Retry." A compact list that only selected a message says "Message selected. Open it to read."
- Composer footer (`12ceded`, found by the fifth visual review): after Check draft or Send refuses
  a draft, and after a failed discard or a send that could not start, the Compose footer says "The
  draft has a problem. Details are below the buttons.", as the Account and Settings footers point
  to theirs. Before, it kept the routine draft line. An unsaved draft keeps its own pointer, "The
  draft is not saved. Details are below the buttons."
- Session limit: at 500 messages Load older had been disabled with nothing on screen saying why,
  and the `large-inbox` fixture never reached the limit. An information notice above the list now
  says "Session limit reached (500 messages). Older ones cannot be loaded in this session. Receive
  mail to start again." It stays while messages are read, follows a failed or canceled receive's
  explanation, is announced once after "500 messages loaded.", and the compact reader's footer
  points to it ("Session limit reached. Use Back to inbox to see the details.").
- `InboxViewModel` reports `ListProblem` and `MessageProblem` separately (`InboxProblem(Text,
  IsCancellation)`), with `CanRetryIn`/`RetryAsync` per scope and `SessionLimitNotice`; `Problem`,
  `ProblemScope`, and `ProblemIsCancellation` report the newer problem.
- New fixtures `receive-canceled`, `load-error`, and `draft-invalid` (UI-01).

States added to the 2 October tables:

| Surface | State | Shown | Valid actions |
| --- | --- | --- | --- |
| Inbox list | At the session limit | Information notice above the list; after a failed or canceled receive it follows that explanation | Receive mail or Retry receiving; select and read (Load older unavailable) |
| | Failed / canceled while a message is read | The list's reason stays above the list, with Retry in a row below it | Retry receiving or Retry loading older |
| Account | SMTP sign-in test refused / testing / failed / passed | "The test did not start." with what to do first; progress; the reason on the outgoing line; "Done — Outgoing sign-in tested; no message was sent." | Test SMTP sign-in once the SMTP password is saved; Cancel test while testing |

Announced additionally: the passed check result (its removal is silent), and at the session limit
"N messages loaded." followed once by the limit notice; moving through messages there is silent.

- Evidence: `FeedbackPolicyTests` (check confirmation and failure, the focus theory, focus while an
  operation runs, Load older wording, Tab order,
  `ReachingTheSessionLimitIsAnnouncedOnceAfterTheCount`), `InboxStateTests` (list problem kept
  while reading, the empty-inbox reader, the Retry label while loading,
  `TheSessionLimitStaysExplainedAboveTheListWhileAMessageIsRead`),
  `ResponsiveInboxTests.TheInboxNoticeEndsBetweenItsLinesAndKeepsRetryInView` and
  `TheCompactReadersFooterPointsToTheSessionLimitWhileTheListIsHidden`, and the `DemoGalleryTests`
  notice cases with DirectWrite metrics. The composer footer:
  `FeedbackPolicyTests.AFailedDraftCheckStaysAndAnEarlierConfirmationNeverClearsANewerStatus`
  asserts it and fails without the fix; the full suite passed at `12ceded` (893); and a native
  recheck of the six composer fixtures (`draft-invalid`, `send-rejected`, `draft-conflict`,
  `send-unknown`, `sent-copy-failed`, `large-draft`) at 640×480 and 1100×720, light and dark, on a
  NativeAOT publish of `12ceded` had 24 of 24 runs without automated findings
  (`final4-published-compose`, UI-13).
- Open: a screen-reader pass for the transient confirmation, the new focus moves, and the
  announcement order at the session limit (H-01). At 640×480 with 200 % text, `receive-error` and
  `load-error` leave the list about three quarters of a row so that the explanation's first line
  and Retry stay on screen.

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
  Cc/Bcc disclosure (mapped upstream on 4–5 October and verified on the adoption branch, below). A
  paragraph move from the first paragraph lands on the blank separator line, which a screen reader
  reads as blank; acceptable, but worth confirming with a real reader.

**Progress (4–5 October 2026, published stack):**

- The footer status is a literal label (`UseMnemonic = false`), so a status with `&` is shown and
  read as written (`FeedbackPolicyTests.TheFooterShowsAStatusWithAnAmpersandAsWritten`).
- The composer marks a refused recipient field (UI-05); on preview.17 the `FormField` group
  reports Invalid with the error in its name.
- With the published Hosting preview.5, three findings stay: selecting a tab through UI Automation
  leaves focus on the tab item (RETURN_FOCUS_AUTOMATION, UI-02), tab items report an equal share of
  the strip as their bounds, and row runtime IDs are index-based, so after new mail the same ID can
  name another message. Native row names come from the row text and lack the received date.

**Progress (4–5 October 2026, prepared upstream; verified with local packs; Mail adoption on
`claude/ui-09-upstream-adoption`):**

- Broiler.UI (ADR 0028): a Collapsed state and `IUiExpandable` (FormSection toggles, combo boxes,
  menus, tree rows); `Discloses` and `Controls` (the content a toggle opens); `DescribedBy`,
  `ErrorMessage`, and `Description`, so the control inside a `FormField` reports Invalid and
  Required and its description starts with the error; `StructureChanged`, raised once per element
  per input or frame; clip-aware `GetVisibleBounds` with Offscreen, list-row geometry, and
  `GetTabHeaderBounds`; a scroll-view focus ring with the opt-in `FocusWhenScrollable` keyboard
  stop. ADR 0032 hands focus on when a focused scroll stop stops scrolling. Bug found and fixed: a
  LabeledBy, DescribedBy, or ErrorMessage relation that led back to the element crashed the
  process with a stack overflow (the LabeledBy case predates the branch).
- Broiler.Hosting: ExpandCollapse, ControllerFor, IsDataValidForForm and IsRequiredForForm,
  DescribedBy, and FullDescription; BoundingRectangle is the visible part, with IsOffscreen; list
  rows are named by the presenter, including the full received date; tab items report their
  header; runtime IDs are allocated per bridge and kept by item id; removed elements fail with
  ElementNotAvailable and `WM_DESTROY` disconnects every provider; StructureChanged is coalesced
  on the parent's peer (with a queued dispatcher); SetFocus on a row or tab focuses its container
  without selecting, and Select does what a click does; rows and tabs report themselves valid for
  forms. An external UIA2 probe against a test host passed 23 of 23 checks under JIT and NativeAOT.
- Mail: "Inbox notice", "Message header", and "Status and errors" are `FocusWhenScrollable` stops
  with accessible names, replacing Mail's own stop rule, and hand focus on when they stop scrolling
  (the Account status area hands it to Test connection). The reader's editors draw a focus ring.
  Disclosure toggles read "Show keyboard shortcuts" and "Show Sent-copy settings" (capital S, to
  match the other Sent-copy strings), and their content is named, so ControllerFor names "Cc and
  Bcc fields". Both windows with a bridge run `StandardQueuedUiDispatcher`, pinned by a test. New
  scripts: `scripts/Probe-Uia.ps1` reads disclosure state, field validity, row names and validity,
  and runtime IDs from outside; `scripts/Record-Uia.ps1` records focus, notification, selection,
  and state events through one COM client, as input for H-01. The native-check scripts take
  `-Packages`, so summaries name the local packs a build used.
- Evidence, final run on `e870135` (UI local.7, Hosting local.5, NativeAOT): Probe-Uia 6 of 6 — the
  Cc/Bcc toggle Collapsed, then Expanded with ControllerFor "Cc and Bcc fields"; To and Email
  address Invalid with the error as FullDescription and DescribedBy; 50 rows named like "Unread,
  From: Broiler team <hello@example.test>, Subject: Welcome to Broiler.Mail, Received: 9/28/2026
  10:00 AM"; rows and the four tabs valid and not required; 47 of 50 runtime IDs kept across a
  refresh. Accept-Refresh 4 of 4 (RETURN_FOCUS_AUTOMATION clean). Record-Uia: a COM Select gives
  ElementSelected and then FocusChanged on the new row.
- Measured limit, for the user's decision: for a UIA2 client (System.Windows.Automation),
  UIAutomationCore calls SetFocus before Select, so that client first hears FocusChanged on the
  previously selected row. Making SetFocus on an unfocusable row or tab a no-op would avoid this
  but reverses the documented SetFocus behavior.
- Checklist: the COM/runtime-ID, announcement/validation, and screen-reader items stay open. Their
  mapping parts exist only with the unpublished packages, and announcements as spoken need H-01.
- Open: publishing Broiler.UI preview.18 and Hosting preview.7 and rerunning the checks; H-01 with
  Narrator and NVDA, including whether the old-row focus or row validity is spoken.

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
  so Ctrl and AltGr chords were checked headlessly only; posted-message evidence for the wheel,
  dead keys, and IME added 4 October, below). `WindowsInputBridgeTests.EndToEnd_RichEdit_HandlesImeCompositionAndCommit`
  failed once in 13 full runs: Hosting suppresses the duplicate `WM_CHAR` after an IME commit only
  within 500 ms of wall time, which a loaded test run can exceed.
- Broiler.Hosting 0.1.0-preview.5 consumed (3 October): the IME commit's `WM_CHAR` copies are
  now dropped by order rather than by a wall-clock window that started before the commit was
  dispatched (Broiler.Hosting#4), which fixes the intermittent `EndToEnd_RichEdit_HandlesImeCompositionAndCommit`
  failure above (it also failed once on windows-2025 CI). Mail's warm-up workaround in that test is
  removed; 20 consecutive runs pass.
- Broiler.UI 0.1.0-preview.17 consumed (3 October): Shift+wheel scrolls scroll views sideways, a
  horizontal wheel scrolls rich text sideways, and a list that cannot scroll further leaves the wheel
  to its container (Broiler.UI#76). The main window has no horizontally scrolling content: the
  reader wraps even an unbroken URL at 225 % text (checked 4 October, below), so the change does
  not apply to the reader. Since 4 October the HTML preview scrolls wide content sideways (UI-11).
  The UI-12 `scroll` workload on the NativeAOT build: 300 of 300 wheel steps painted, no errors. A
  physical tilt wheel or precision touchpad is still unchecked.

**Progress (4–5 October 2026, posted-input fidelity, published stack):**

- Native input is now tested against the real render window of a hidden `WindowsMailWindow`
  (`HiddenMailWindow` fixture), with messages posted as Windows delivers them, so the tests cover
  Hosting's subclass, Mail's keyboard filter, and the controls.
- Precision wheel: deltas of 30 scroll the message list and the reader by a quarter notch each, and
  four equal one notch. Horizontal wheel: each `WM_MOUSEHWHEEL` becomes exactly one horizontal
  event over the element under the pointer; over the reader at 225 % text it moves nothing and
  stays unhandled, because the reader wraps. Dead keys type exactly one composed character in To
  and in the body; surrogate pairs typed unit by unit arrive once.
- IME placement: the composition window is placed in physical pixels at 100, 150, and 200 % and
  follows the caret of the focused To or body field through the render window's own input context.
- Bug found and fixed: password fields took the IME, and the default IME window could show the
  password in plain text at the caret. Mail turns the IME off while a password field has the caret.
- Bug found and fixed: when new mail pushed the selected row out of view, Tab focused the list with
  nothing visible to show it (Accept-UI, `new-mail` at 640×480 with 200 % text). The shared reveal
  now scrolls the selected row into view first.
- Found upstream: a real IME can show the composition twice, inline and in the IME's own window
  (confirmed at the message level, not seen on screen); Hosting types `WM_SYSCHAR` as text, so
  Alt+F types "f" and Alt+Space types a space instead of opening the window menu; a tilt wheel
  scrolls the wrong way in Broiler.UI's scroll view, rich edit, and format code view.
- Legacy Graphics input adapter: kept. Broiler.Input preview.5 has neutral Windows translators, but
  Mail does not consume them and no consumed host feeds them.
- Evidence: `NativeInputFidelityTests` (6), `ComposerImeTests` (2), `WindowsTextInputTests` (8,
  including three scales and two password cases). Dropping or doubling horizontal wheels, placing
  the IME through the frame handle, and removing the password rule each fail a test. The Windows
  test classes that build a session or show a window now run serially in a "UI theme" collection:
  without it, the stacked branches failed about one Windows run in three.

**Progress (4–5 October 2026, prepared upstream; verified with local packs; Mail adoption on
`claude/ui-09-upstream-adoption`):**

- Broiler.Hosting: `DrawsCompositionInline` (default true) keeps an inline composition out of the
  IME's own window and no longer expects `WM_CHAR` copies of the commit. Review found that the old
  copy suppression then dropped the user's next keystroke when it matched the commit's first
  character; fixed. `WM_SYSCHAR` goes on to `DefWindowProc` instead of being typed.
- Broiler.UI (ADR 0030): a wheel tilted right scrolls right; Shift+wheel keeps its direction in
  both shapes hosts send; a tilt is left to an outer scroller when a control cannot scroll
  sideways; toolbar arrows, Home, and End land only where Tab can; the tab strip's arrow keys
  switch tabs only while the strip has focus.
- Mail: the IME is off whenever the focus draws no composition (lists, buttons, password fields);
  a regression test pins that `WM_SYSCHAR` (Alt+F, Alt+Space) types nothing into the composer;
  `ComposerImeTests` follow the inline-composition contract.
- Checklist: the input-verification and exactly-once/legacy-adapter items stay open; their
  remaining parts need physical devices or a neutral input source.
- Open: a real CJK IME (inline composition without a second window, candidate list, commit typed
  once, undo); AltGr and dead keys on physical layouts; a precision touchpad and a physical tilt
  wheel; caret placement across a real DPI change. Upstream: a Windows `IUiTextInputHost` in
  Hosting (per-focus IME positioning and enabling), a way to mark Alt chords handled (an unclaimed
  chord beeps), and a neutral Windows input source so Mail can drop the legacy adapter.

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
  starts at the top. The preview has no zoom of its own; it follows the display scale (zoom came
  later: since 4 October the preview opens at the system text size and has its own zoom, below).
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
- Open: an inline reader view (blocked by renderer containment, as before); a preview zoom control
  (done 4 October, below).

**Progress (4 October 2026, page zoom at the system text size, published stack):**

- The preview has a page zoom from 50 to 300 % (the usual steps, plus the system text size when it
  is not a step). The document is laid out at the viewport's width over the zoom, in CSS pixels,
  and drawn that much larger, so text grows and still wraps to the window. One control also scales
  the plain-text view. Nothing is saved: every preview opens at the default.
- The preview opens at the system text size (UI-07). While the reader has not zoomed, the zoom
  follows a text-size change; an explicit zoom stays, and only the reset target moves.
- Controls: Ctrl+= / Ctrl+Plus, Ctrl+-, and Ctrl+0 (main keys and number pad; AltGr does not zoom)
  and Ctrl+wheel, plus a wrapping header toolbar with Zoom out, a reset button showing the level
  ("200 %, reset zoom to 150 %"), and Zoom in. "Zoom N %." is announced once per change, but not
  while the reset button has focus, because its name already says the level.
- Reading position: the block at the top of the viewport stays there through a zoom; a hidden HTML
  view and the plain text keep their place.
- Wide content: a page wider than the window is drawn in window-wide columns, only those in view,
  at the display scale; content that cannot wrap (an unbroken address) scrolls sideways instead of
  being clipped. The height budget grows with the zoom.
- Bug fixed, input freeze in the NativeAOT build: Broiler.HTML rasters tiles with `Parallel.For`;
  on the preview's STA thread that wait pumped window messages, and with a UI Automation
  notification listener attached a zoom left the window in that wait for good. The process now
  rasters on one thread (`TileParallelReplay.MaxDegreeOfParallelism = 1` and
  `BROILER_RASTER_THREADS=1`), at about twice the raster time per tile (110–120 ms against
  54–65 ms on `long-html` at 200 %). Also fixed: a reflow that made the document shorter jumped the
  reader to the end, and a theme applied before the preview window existed was dropped.
- [Decision 0005](decisions/0005-isolated-html-preview.md) records the tile bounds, the
  single-thread raster with its process-wide scope and cost, and the zoom.
- Evidence: `PreviewZoomTests` (22), `HtmlPreviewZoomTests` (28, including real-window
  cases), `ScrollableMessageTextZoomTests` (2),
  `HtmlPreviewIsolationTests.TilesAreRasteredByTheirOwnThreadAlone`; 35 of 36 scripted reversions
  of the production changes failed the test aimed at them (the 36th cannot fail alone). NativeAOT
  at 150 %: zoom steps by UIA Invoke and Ctrl+wheel to 300 %, one UIA notification per change,
  Shift+wheel sideways scrolling, and columns continuous across their edges.
- The 2 October note that a 520-pixel window reflows without a horizontal scrollbar still holds
  for `long-html`; `html-only` now scrolls sideways there for its unbroken address, by design.
- Open: Broiler.HTML needs a sequential raster option that does not block an STA thread (it would
  restore the parallel tile speed); tables are squeezed below their content width at large zooms
  (`table{max-width:100%}`), so cells overlap; on the published stack a tilt wheel scrolls the
  preview the wrong way and toolbar arrows land on unavailable buttons (fixed upstream, UI-10);
  physical layouts, touchpad pinch, and screen-reader speech of the zoom; Accept-UI does not open
  the preview; the inline reader view.

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
  DPI scales. (Tile misses, long HTML, and simulated scales added 4 October, below.)
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

**Progress (4–5 October 2026, harness additions and measurements, published stack):**

- `--detail` splits each frame into drain, measure, arrange, and render-list phases, times each
  input's dispatch, and times the host's render+present call (CPU wall time on the UI thread,
  including the vsync wait; not GPU time). Without it, frames are timed as before, and reports stay
  comparable with 2 October.
- New workloads on the HTML preview: `long-html` (wheel to the end of the long newsletter and back)
  and `preview-zoom` (steps through the zoom levels). They report tile hits, misses, re-rasters
  after eviction, evictions, discards, raster and upload time, peak cache bytes, and which frames
  drew a tile.
- `Measure-UI.ps1` gained `-Workloads`, `-Scales` (simulated, and labelled so), `-Detail`,
  `-Budgets`, `-Strict`, and `-Evaluate`. The proposed budgets are in `scripts/ui-budgets.json`:
  frame build p95 ≤ 8 ms, input to frame p95 ≤ 16.7 ms, no idle frames, allocation p50 ≤ 256 KB
  for steady interactions, and no unpainted steps. They are report-only and tied to no reference
  machine; `-Strict` fails a result over budget or without data. Method:
  [performance baseline, harness additions](ui-performance-baseline-2026-10-02.md#harness-additions-4-october-2026).
- Measurements on 5 October, with tables and method in the
  [5 October performance record](ui-performance-2026-10-05.md): NativeAOT, 1100×720 light, system
  150 %, three repeats each, on an otherwise idle machine. Measured builds: `main` (`757e81b`), the
  published branch at `f7fdb0b`, and the adoption branch at `afb0926` with UI local.6 and Hosting
  local.4 (since rewritten away by a rebase). The verified revisions `4c36950`, `12ceded`, and
  `e870135` were not measured again.
- Idle drew no frames in any build. In every build, select and resize stay over the proposed 8 ms
  build budget at p95 and allocate over 256 KB per frame. With `--detail`, resize spends most of
  its build in measure (p50 8.3 of 13.8 ms). Simulated 100 % and 200 % scales gave the same build
  times as 150 % for scroll, select, and resize; they are not real DPI results.
- Regressions, none profiled ([observations](ui-performance-2026-10-05.md#observations)): the
  adoption build is 0.3–0.4 ms slower at p50 in scroll, theme, select (already over budget), and
  splitter and allocates 5–7 KB more per frame; the published branch's resize is 0.8 ms slower
  than `main`.
- HTML preview, published and adoption alike: `long-html` tiles raster at p50 about 174 ms (the
  single-thread raster, UI-11), so frames that draw a tile take about 180–200 ms, while cached
  frames build in about 0.13 ms. A zoom step takes about 344 ms.
- Checklist: unchanged. Text-layout calls, targets on a named reference machine, and real DPI
  scales are still missing.
- Open: text-layout call counts (a public text-measurer hook in Broiler.Graphics or layout counters
  in Broiler.UI); GPU and present-to-screen timing (Broiler.Graphics' `FrameRendered` is only on its
  unpublished main, which also carries unreviewed changes); agreeing the budgets on a named
  reference machine and whether `-Strict` becomes a gate (user decisions); input through the native
  message path; real DPI scales; select and resize allocation; the raster speed (UI-11).

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

**Progress (4–5 October 2026, full matrix, reader, text size, contrast palettes, and visual
review):** recorded in [ui-acceptance-2026-10-05.md](ui-acceptance-2026-10-05.md).

- Scripts (published stack): `Accept-UI.ps1` now waits for the top-level window that has the
  render child and kills its demo process in a `finally` block. Before, the console-subsystem exe's
  `MainWindowHandle` could resolve to its console window, abort the matrix, and leave the demo
  running. `-OpenReader` opens the reader in the 640×480 runs, checks it (READER_* and HEADER_CUT
  findings), and saves `<run>-reader.png`. `Accept-Refresh.ps1` checks refresh continuity (UI-02).
  On the adoption branch: `-Contrast high|aquatic|desert|dusk|night-sky`, `-Packages` labels, and
  `Probe-Uia.ps1` and `Record-Uia.ps1` (UI-09).
- Visual review: five agent-driven, pixel-level reviews of 132, 304, 266, 570, and 226
  screenshots; every reported defect got a second, independent check before it counted. Mail
  defects found and fixed on the published stack: footer inset; list notice and Retry pinned and
  inset; footer pointers; list problems kept while reading; composer problems first; the reader
  divider and Reply row; the empty-inbox reader text; the session-limit notice; Tab revealing the
  selected row; the compact header ending at row boundaries; date-line wrapping; the refused
  composer field marked; the composer footer pointing to a refused draft's details (UI-03, UI-05,
  UI-08, UI-10). Toolkit defects (contrast roles, focus rings, the tab strip, form ring room, list
  frame and unread dot) were fixed on the upstream branches (UI-01, UI-05, UI-07, UI-09).
- The fifth review was a targeted look at the `final3` captures of both stacks (the full,
  reader, and 200 % folders of each, and the five contrast-palette folders). It confirmed every
  change of the previous round: the empty-inbox header spacing, the gap between the capped compact
  header's Reply strip and the divider, the date line wrapping after "·", the session-limit
  wording, "Details and Retry are below its date.", and the refused To field marked with focus;
  on the adoption stack, the non-scrolling notice as no Tab stop, the field ring kept apart from
  the scrollbar thumb, and the upstream fixes in the contrast palettes. It found three
  low-severity cosmetic defects: the composer footer did not point to a refused draft's details
  (fixed in `12ceded`, UI-08), a doubled-looking divider under a capped compact header (open,
  Mail, UI-03), and the tab focus ring flush against the client frame in the contrast palettes
  (open, Broiler.UI, below).
- Tests: the published stack has 893 (618 shared, 272 Windows, 3 Linux) and the adoption branch
  934 (640 shared, 291 Windows, 3 Linux) with local packs, each passing twice; the published stack
  passed again at `12ceded`; every one of the adoption branch's 38 commits above `4c36950` also
  passes. `main` had 429. Known flaky
  under heavy machine load:
  `MeasurementTests.A_Detailed_Preview_Times_The_Dispatch_Phases_And_Paint_Of_A_Measured_Input`
  (2 of 40 runs, both while other builds ran).
- Final native runs, 5 October (NativeAOT win-x64, one 3840×2160 monitor at 150 %, Windows 11
  Enterprise 26200, SDK 10.0.401). Counts are runs without automated findings; the 22 fixtures run
  at three sizes in both themes for the full matrix.

  | Run (`final3`) | Published stack (`4c36950`; UI preview.17, Hosting preview.5) | Adoption branch (`e870135`; UI local.7, Hosting local.5) |
  | --- | --- | --- |
  | Full matrix | 132/132 | 132/132 |
  | Compact reader (`-OpenReader`, 640×480) | 20/20 | 20/20 |
  | 200 % text | 44/44 | 44/44 |
  | Broiler.UI high-contrast preset | 88/88 | 10/10 (`-Contrast high`) |
  | Windows 11 contrast palettes, synthetic (aquatic, desert, dusk, night-sky) | not available | 10/10 each |
  | Accept-Refresh | 3/4 (RETURN_FOCUS_AUTOMATION) | 4/4 |
  | Probe-Uia | not available | 6/6 |

  After these runs, `12ceded` (the composer footer, UI-08) was rechecked on the published stack:
  the six composer fixtures at 640×480 and 1100×720, light and dark, on a NativeAOT publish of
  `12ceded`, 24 of 24 without automated findings (`final4-published-compose`).

- Open, needing hardware or the user: real screen readers (Narrator and NVDA, H-01); a real CJK
  IME; AltGr and dead keys on physical layouts; a precision touchpad and a tilt wheel; real 100 %
  and 200 % DPI, monitor moves and disconnects; real Windows contrast themes, OS text size, and
  reduced motion; ARM64; RTL and long translated labels (UI-14). The steps are in the
  [manual acceptance checklist](ui-manual-acceptance-checklist.md); results go into the acceptance
  record as it describes.
- Also open: the user publishes Broiler.UI preview.18 and Hosting preview.7; then the adoption
  branch is rebased onto `12ceded`, the suite, Accept-UI (default, dusk, aquatic), Accept-Refresh,
  and Probe-Uia are rerun on it, the screenshot baselines are refreshed, and the
  README (contrast options, `Probe-Uia.ps1` and `Record-Uia.ps1`, Accept-UI `-Contrast` and
  `-Packages`, package versions), this roadmap, and the status are updated for the adoption
  branch.
- Follow-ups from the fifth review, cosmetic and not started: in Mail, the doubled-looking divider
  under a capped compact header (UI-03); in Broiler.UI, the tab focus ring on the selected header
  (ADR 0031, adoption packs only), which in the contrast palettes sits flush against the window's
  client frame, so in `high` and some palettes the ring and the frame read as one thick line.

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

**Progress (4–5 October 2026):** no UI-14 work. The user decided to skip localization this
round, so the string layer, long translated labels (UI-01), and the RTL checks (UI-07, UI-13)
stay open. Linux, Android, folders/search/attachments, and multiple accounts still wait for their
services.

## Delivery slices

Each slice should be a reviewable change with its relevant acceptance evidence.
Do not combine the whole program into one UI rewrite.

| Slice | Deliverable | Exit condition | Exit status, 5 October 2026 |
| --- | --- | --- | --- |
| 1 | UI-01 fixture/token baseline; start UI-09 discovery and UI-12 measurements | Reproducible visual/performance baseline and a native UIA reproduction | Delivered: 22-fixture gallery, token record, measured baseline, UIA reproduction and fix. Long-label fixture waits for UI-14 |
| 2 | UI-02 refresh continuity | Selection/body/scroll remain stable across valid refreshes | Implemented and checked natively on the published stack (Accept-Refresh 3/4); the UIA return-focus path needs Hosting preview.7 (adoption branch 4/4); real DPI and speech pending |
| 3 | UI-03 reader and reply actions | Read → reply works directly and preserves an existing draft | Acceptance pending: implemented and checked natively; screen-reader check pending (H-01) |
| 4 | UI-04 responsive navigation | Compact and split modes preserve context through resize | Acceptance pending: resize and simulated DPI changes pass; real DPI changes and monitor moves pending |
| 5 | UI-05 composer layout | Writing fills available space; actions, caret, and draft state remain stable | Acceptance pending: layout, undo, and posted-message IME pass; real IME pending |
| 6 | UI-06 setup, UI-07 appearance, UI-08 feedback in small independent changes | Clear setup, live themes, geometry restoration, accurate actionable states | Partial: the SMTP sign-in test is delivered on the published stack; Windows contrast colors only on the adoption branch (synthetic palettes); live SMTP provider, real contrast-theme, OS-setting, and speech checks pending |
| 7 | UI-09/10 native acceptance and UI-11 preview polish | External accessibility/input pass; preview lifecycle is predictable | Partial: external-client and posted-input pass on the published stack; semantics mapping and input fixes upstream-ready (adoption branch, local packs); preview with zoom done. Screen reader and physical input pending |
| 8 | Measured UI-12 fixes and UI-13 acceptance | Evidence for the shipped package across the required matrix | Partial: matrix clean on published packages and on local packs; measurements recorded, budgets and reference machine undecided; hardware rows pending |

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
