# Broiler.Mail experience roadmap

Investigation: **30 September 2026**. Status: proposed work, not an implementation commitment.

For the current implementation/remaining-work split, see the
[2 October follow-up audit](remaining-improvements-2026-10-02.md). In particular,
two-line rows, adjustable panes, selectable body text, and C-04 form surfaces have
since been delivered; the findings below preserve the original investigation.
Use the [UI implementation roadmap](ui-implementation-roadmap.md) for the current
task sequence and acceptance criteria.

Broiler.Mail can feel substantially better without replacing its architecture. The highest-return changes are to correct rendering defaults, give messages a readable hierarchy, keep composition actions visible, and make state changes predictable. Then improve the shared controls that make every Broiler application smoother.

This review used a fresh Release build of the working tree, the native `--demo` app, source inspection, a probe against the actual consumed assemblies, and the existing automated suite. The checkout already contained uncommitted platform work. The baseline is therefore **HEAD `08e2bc6` plus that working tree**, not the commit alone. No application implementation was changed by this investigation.

Companion documents:

- [Screenshots, reproduction steps, measured results, and limits](ux-review-2026-09-30/README.md).
- [Roadmap for shared Broiler components](broiler-experience-components-roadmap.md).
- [Existing feature/release roadmap](roadmap.md), [cross-platform plan](cross-platform-roadmap.md), and [renderer security gate](html-renderer-security.md).

## 1. What the app feels like today

The current app is a functional engineering foundation. It already has useful safeguards: network work leaves the UI thread; newer message requests invalidate stale completions; a failed refresh retains the previous inbox; draft writes are serialized and recoverable; SMTP acceptance and Sent-copy status are kept separate. Those are assets to preserve.

The presentation still resembles a collection of configuration forms. Most text has similar weight and size. Content starts at pane edges. Many buttons stretch across the entire form. Inbox information is compressed into a single string, while the reader and account forms have large expanses of unstructured space. Routine use requires too much navigation and interpretation.

![The current native reader](ux-review-2026-09-30/04-message-plain-text.png)

### Findings and priorities

Priority means implementation order within this experience program. **P0** is a verified foundation defect or a release gate, **P1** improves the everyday experience, **P2** expands capability, and **P3** is optional differentiation. Effort estimates assume familiarity with the code and include focused verification, but exclude upstream release waiting time. They are planning ranges, not measured delivery forecasts.

| ID | Evidence and impact | Proposed action | Priority / rough effort | Status |
| --- | --- | --- | --- | --- |
| EX-01 | The consumed Graphics `preview.7` returns `false` for all three default render flags. Native screenshots have visibly jagged UI text. | Set explicit render options in both Mail windows; fix and regression-test Graphics defaults upstream. | P0 / 1–3 days plus upstream release | [x] Done |
| EX-02 | The 320-DIP list combines `Read/Unread · subject — sender`; long rows are clipped. Date is only in the reader. | Two-line rows with separate sender, subject, time, unread indicator, and optional snippet; adjustable pane width. | P1 / 5–10 days with control work | [ ] Proposed |
| EX-03 | Reader text and header touch the list divider; subject and metadata have equal emphasis. | Reading margins, clear subject hierarchy, compact sender details, selectable text, local reply actions. | P1 / 3–7 days, selection control may add effort | [ ] Proposed |
| EX-04 | Compose shows instructions and four recipient/subject fields before the body. Send/save/discard require scrolling. | A docked composition surface with persistent action bar and optional Cc/Bcc. | P1 / 5–8 days | [ ] Proposed |
| EX-05 | Reply requires selecting/reading in Inbox, opening Compose, then choosing Reply. | Reply/Reply all/Forward beside the message; focus To or body appropriately. | P1 / 2–4 days | [ ] Proposed |
| EX-06 | During a normal body fetch, the reader says to retry if loading fails while the footer says “Loading message…”. | Distinct loading, cancellation, failure, empty, and ready states in the affected pane. | P1 / 2–4 days | [x] Done |
| EX-07 | Successful Receive clears the selected message/body; failed Receive preserves the inbox. | Reconcile by message identity and retain selection, scroll anchor, and body when still valid. | P1 / 4–7 days | [ ] Proposed |
| EX-08 | Account is a long full-width form; Save, password operations, and the test are below the initial viewport. | Guided setup and grouped advanced settings with persistent save/test status. | P1 / 6–12 days | [ ] Proposed |
| EX-09 | Settings stores theme and initial dimensions; appearance changes apply after restart. A live theme controller exists in the consumed UI assembly. | Apply appearance immediately, follow system changes, remember useful geometry automatically. | P1 / 3–6 days | [ ] Proposed |
| EX-10 | UI Automation exposes the native window/chrome but no mail controls. The host implements no `IUiAccessibilityHost`. | UI semantics to native UIA provider bridge, then real assistive-technology acceptance. | P0 release gate / multiweek shared work | [ ] Proposed |
| EX-11 | HTML opens a second window; returning to text changes reading context. | First improve preview lifecycle/status; later offer an integrated reader using the established isolation boundary. | P1 ergonomics / 3–6 days; isolation separately estimated | [x] Done (preview streaming cap, cancellation, granular states) |
| EX-12 | Source reveals repeated full layout/text work, but no frame-time profile was captured. | Instrument, profile, then cache/invalidate selectively. | P1 / 2–4 days baseline, 1–3 weeks targeted optimization | [ ] Proposed |
| EX-13 | Documentation disagrees about whether HTML exists, which renderer it uses, and what “isolated” means. Demo text still says version 1. | One accurate capability/status description and current demo fixtures. | P1 / 1–2 days | [ ] Proposed |

### First technical fix: rendering quality

The probe used `Broiler.Graphics` **0.1.0-preview.7** from the freshly built Mail output:

```text
BRenderOptions.Default: Antialias=False, VSync=False, SubpixelText=False
new BRenderOptions():   Antialias=False, VSync=False, SubpixelText=False
new BRenderOptions(true, true, true): all True
BWindowOptions.RenderOptions: all False
```

`BRenderOptions` is a record struct with optional positional parameters. `Default => new()` zero-initializes it; the optional `true` arguments do not create an explicit parameterless constructor. This matches [C# structure initialization rules](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/struct#struct-initialization-and-default-values).

Both `WindowsMailWindow` and `HtmlPreviewWindow` construct `BWindowOptions` without overriding `RenderOptions`. The inspected Direct2D renderer chooses aliased text when `Antialias` is false. This is stronger evidence than simply judging a screenshot. It does **not** establish measured frame pacing or prove that every perceived rough edge comes from this setting.

Proposed first patch:

```csharp
RenderOptions = new BRenderOptions(
    Antialias: true,
    VSync: true,
    SubpixelText: true),
```

Use explicit settings in Mail while fixing `BRenderOptions.Default` upstream. Define the intended behavior of `new BRenderOptions()` too; `default(BRenderOptions)` will remain zero-valued. Compare grayscale/subpixel output on the actual surface types, including transparent surfaces. Validate 100%, 125%, 150%, and 200% scaling before choosing the final text policy. Keep screenshots of the same content and font as the baseline.

## 2. Proposed visual direction

Aim for a calm desktop mail client: a quiet navigation area, scannable messages, a generous reader, and one obvious primary action. Visual appeal should come from typography, alignment, good icons, and responsive feedback before decorative effects.

### Layout

- **Near-term shell:** keep the current single-account capability, put New message and Refresh where they are always visible, and move Account/Settings into secondary navigation. Do not add dead folder items merely to resemble a larger mail client.
- **Wider desktop:** adjustable message list and reader. Introduce a folder/account rail when the folder/multi-account work supplies real destinations. A useful initial list width is 340–400 DIPs, with a draggable splitter and persistence.
- **Narrow desktop:** switch between list and reader with a Back action rather than squeezing both. Trial a breakpoint around 800–900 DIPs and tune it against content and text scaling. This is a proposed threshold, not an observed capability.
- **Compose:** a dedicated surface, reachable from the reader and New message. Keep current single-draft semantics initially; add a draft switcher only after the storage model supports it.
- **Settings:** bounded form width, category groups, and a visible save/error area. Initial dimensions become an advanced option; normal use remembers window size, position, maximization, and split ratio with off-screen recovery.

### Starting design tokens

These are design proposals to test in Broiler.UI, not claims about existing APIs.

| Element | Proposed starting point |
| --- | --- |
| Spacing | 4-DIP base; 8 between related controls; 16 inside groups; 24 around reader content |
| Typography | System UI font; 14–16-DIP controls; 16–18-DIP message body; 22–26-DIP subject; clear medium/semibold hierarchy |
| Reading width | Approximately 65–85 characters for prose, with a wider option for logs/code and original formatting |
| Rows | Comfortable ~68–80 DIPs with two/three lines; compact ~44–52 DIPs with two lines |
| Corners | Existing 4/6-DIP control language; avoid a rounded card around every paragraph |
| Surfaces | Slightly tinted navigation/list background; reader on the main surface; subtle separators |
| Color | One Broiler accent for primary action and active selection; semantic warning/error/success roles; text/icon as well as color for state |
| Icons | Consistent 16/20-DIP vector family, aligned stroke weight; visible labels for important actions |
| Motion | Brief 100–160 ms hover/press transitions; 160–220 ms small pane transitions; instant behavior when reduced motion is requested |

Consistent spacing and differentiated text styles support grouping and hierarchy; Microsoft's [Windows layout guidance](https://learn.microsoft.com/en-us/windows/apps/design/basics/content-basics) is a useful reference. The exact sizes above remain Broiler design decisions.

Keep the native title bar initially. A custom title bar, translucency, shadows, and elaborate transitions should wait until hit testing, window snapping, contrast, and frame budgets are reliable. Sender initials can be generated locally; fetching contact avatars would add privacy, cache, and network decisions unrelated to basic polish.

## 3. Reading and inbox workstream

### A. Make the list scannable

Replace the composite string in `InboxView` with a mail row model/presenter:

1. Sender on the first line; received time aligned at the trailing edge.
2. Subject on the second line, emphasized for unread messages.
3. Optional bounded snippet in comfortable mode, only if already available; do not fetch every body merely to decorate the list.
4. Unread dot plus a readable semantic state; attachment/draft indicators only when accurate metadata exists.
5. Ellipsis and accessible full text for overflow. Keep stable message keys separate from presentation text.

Broiler.UI's list already paints only its visible range. Preserve that property while adding templated rows; do not replace it with hundreds of nested panels and call that an improvement. Fixed-height row variants can be the first implementation, with variable heights only if their value justifies the complexity.

**Done when:** sender, subject, and date remain distinguishable at the agreed minimum list width; switching density preserves the selected identity and scroll anchor; keyboard and pointer select the same message; a 500-row fixture does not render every row.

### B. Give the reader structure

Build a header with subject, sender, recipients/details disclosure, date, and local actions. Put the body below a quiet divider with proper padding. Make address copying and message text selection possible. The current plain-text reader is a `StandardLabel`; it is not a selection/copy surface.

Evaluate a read-only RichEdit/text-view adapter against the existing text editor before creating a new control. Verify literal ampersands, line endings, code indentation, long URLs, grapheme clusters, bidi text, selection across wrapped lines, and copying exactly the selected text. Do not lose the current text-fallback and truncation labels.

**Done when:** opening a message immediately establishes its identity; Reply is available there; text can be selected/copied with keyboard and mouse; large text stays readable without clipping; header details do not consume the entire small-window reader.

### C. Preserve context through asynchronous work

- During refresh, keep the current reader and list visible; add a small “Refreshing…” state by Refresh.
- On success, reconcile using account + folder + UIDVALIDITY + UID. Preserve the current message if its identity survives; clear it explicitly on deletion or mailbox identity change.
- When selecting a new uncached message, show its header immediately and a body-local loading state. Never show the previous body under the new header.
- Preserve the existing cancellation/generation mechanism. Rapid selection must not let an older completion overwrite the newest selection.
- Add a small, byte-bounded decoded-body cache before speculative prefetch. Key it by full message identity and clear/invalidate it on relevant account/mailbox changes.
- Consider one adjacent-message prefetch only after measurements, with cancellation, low concurrency, and a metered-network policy.
- Keep “Load older” as a clear bounded action initially. If incremental scrolling later triggers it, preserve anchor position and expose loading/failure/retry at the list end.

**Done when:** refresh does not unexpectedly jump back to an empty reader; cached re-selection is immediate; deleting a selected item never opens a different message under the old identity; cancellation and failure leave a clear recovery action.

### D. Useful empty and error states

Distinguish: account missing, connected but not refreshed, fetching, no messages, filtered list empty, body unavailable, offline with cached data, and an actual connection error. Each needs a short explanation and one relevant next step.

Use compact inline banners near the affected surface. Keep persistent failures visible until resolved; reserve transient notifications for completed low-risk actions. The footer can summarize sync status, but should not be the only place where a recoverable error is described. Optional diagnostic details may expose technical context without making every user read it.

## 4. Composition workstream

![The current composer after starting a reply](ux-review-2026-09-30/06-compose-reply.png)

### A. Make writing the main activity

- Dock a compact header and persistent footer around the editor. The footer contains primary Send, draft storage state, and secondary actions.
- Show From, To, and Subject initially. Reveal Cc/Bcc on demand, and always keep a populated hidden field visible or explicitly summarized.
- Give the body the remaining height and one clear scrolling owner. Remove nested page/editor scrolling where possible.
- Move “only plain text is retained” to contextual help; show a direct explanation when a paste loses formatting.
- Start a new message with focus in To; start a reply with focus at the intended insertion point in the body. Bring the previous draft forward rather than allowing a new action to overwrite it.
- Keep primary and destructive actions visually distinct. Retain easy explicit recovery, with undo or confirmation for destructive draft discard as a product choice.

### B. Address entry and validation

Begin with better labeled text fields and field-local validation. Recipient chips/autocomplete can follow as a dedicated accessible control, including paste of a list, display names with commas, keyboard navigation, edit/delete, invalid-address feedback, and Bcc-only drafts. Preserve raw incomplete text in autosave.

Show errors beside the field and focus the first relevant error after Check/Send. Do not validate aggressively while an address is still being typed. For empty subjects or a likely forgotten attachment, use a recoverable pre-send prompt only after those features exist.

### C. Preserve the excellent send-state model

Present draft storage, submission, and Sent-copy results as distinct concepts:

| State | User-facing treatment |
| --- | --- |
| Editing / saving | Small draft status near the action bar; no success toast on every keystroke |
| Save failed | Persistent banner with Retry save; maintain editable content and close protection |
| Sending | Visible progress and accurate action availability; no silent duplicate command |
| Accepted by server | Clear acknowledgement; do not claim delivery to the recipient |
| Definitely failed | Retain editable draft and explicit retry |
| Outcome unknown | Persistent explanation and safe recovery; no automatic resend |
| Sent-copy failed/unknown | Separate from SMTP result; never offer resend as a copy-recovery action |

Do not simplify these states into a single green “Sent” badge. A future Undo send requires a real pre-submission delay with a visible queue; it cannot retract an already accepted SMTP message.

### D. Keep typing responsive

`ComposerView.Capture` asks RichEdit for the full plain text on every change; `DraftJournal` already coalesces snapshots through one writer. Profile document extraction, allocations, serialization, disk writes, and UI notifications before changing this. If needed, batch extraction or add a short bounded autosave debounce while retaining immediate flush on Send and normal close. Any debounce changes the forced-termination loss window and needs an explicit product decision.

**Done when:** Send and save failure are visible at 1100×720 and 640×480 logical client sizes; reply is one action from the reader; a large draft remains editable without long UI stalls; restart, conflict, accepted, and unknown-outcome tests still pass.

## 5. Account setup and settings workstream

The account model is deliberately cautious, but the current UI makes the user manage too much sequencing.

### Guided first setup

1. **Identity:** email and display name, with a manual setup route.
2. **Receiving:** provider-approved IMAP endpoint, transport, username, and authentication.
3. **Sending:** optional SMTP endpoint and credentials; explain when it is not configured.
4. **Verify:** separate IMAP and SMTP results. A future SMTP test must authenticate without sending a message.
5. **Finish:** concise summary, Sent-copy policy, and an action to receive mail.

A single “Save and test” UI action may orchestrate the existing persistence/credential steps, but must respect partial failure and server-bound credential identity. Endpoint changes must still invalidate the corresponding credential binding. Never infer provider support from a guessed hostname. Provider presets and OAuth need verified configuration, support documentation, and acceptance coverage; they are separate feature work.

For subsequent edits, use grouped Identity, Incoming, Outgoing, and Advanced sections. Keep concise success/error indicators beside each connection. Explain disabled actions locally rather than leaving the user to discover a prerequisite far below the fold. Move rare details such as exact Sent folder paths into advanced settings until folder discovery exists.

### Appearance and preferences

- Apply Light/Dark immediately using the existing live theme controller; re-theme both main and preview sessions on their owning threads.
- Implement the system-settings host boundary for contrast, text scale, color scheme, and reduced motion. The current registry read only resolves light/dark at startup.
- Preserve semantic color roles when applying a theme. `StandardLabel.ApplyTheme` currently resets its foreground to the generic text color, so custom muted/status roles need explicit ownership.
- Provide comfortable/compact density and reading-font size with live preview.
- Remember window placement and pane ratio; recover if the previously used monitor is missing.
- Put diagnostics, log location, exact dependency versions, and version/support information in an About/Troubleshooting area.
- Reconcile visible English strings, localized dates, and future localization intentionally. Use resource-backed strings and test long translated labels rather than reducing font size to fit.

**Done when:** setup can be completed without hunting for Save/Test; theme changes preserve draft text, focus, and selection; high contrast is honored; all settings can be used with keyboard and accessible labels.

## 6. HTML preview workstream

The controlled demo opens and toggles HTML/plain text successfully. The preview is a separate native window, using Broiler.HTML in a background STA thread **inside the Mail process**. Its lifecycle is not a renderer sandbox. The existing [HTML security specification](html-renderer-security.md) remains authoritative.

### Near-term ergonomic changes

- Give the preview a reliable message title and a clear return-to-mail action; retain the originating selection and sensible focus after close.
- Use consistent reader spacing, zoom, selectable text where supported, and a stable text/HTML switch.
- Make status reflect actual work. Source inspection shows `LoadRemoteImagesAsync` labels images “loaded” before downloads finish and silently catches failures. Use loading, partial success, failure, cancellation, and retry states instead.
- Enforce a streaming byte cap while reading downloads; the current 5 MB check occurs after `ReadAsByteArrayAsync`. Apply image dimension/decoded-pixel limits and a total message budget too. Keep this coordinated with the existing security work, not as an excuse to widen resource access.
- Cancel resource work when the preview closes or changes message. Publish UI state on the preview dispatcher and reject stale completions.

### Integrated reading after the boundary is ready

Embed the visual result in the reading pane while keeping rendering in the restricted process specified by the security plan. An optional pop-out can remain. Shared app chrome should reflect the selected theme; do not blindly invert arbitrary email colors. Add a per-message original/light appearance option after contrast tests.

The current renderer creates a full bitmap, encodes PNG, and hands it back to Graphics, with a height clamp of 8192 pixels. This creates a memory/copy cost candidate and a potential long-message cutoff to test. Prefer a bounded, viewport-aware surface/tile transport after profiling and within the isolation design. Do not remove the height cap without replacing it with an explicit resource budget.

**Done when:** renderer failure leaves the reader usable; long messages have a defined non-silent limit or full scroll access; HTML/plain switching preserves the message identity; remote-image state is accurate; isolation tests satisfy the existing release gate.

## 7. Smoothness: measure before optimizing

The demo deliberately delays inbox fetch by 500 ms and body fetch by 300 ms. Those delays are fixture behavior, not observed network performance or proof of sluggish rendering. No instrumented frame/latency benchmark was completed in this review.

### Baseline instrumentation

Record operation start/end, input dispatch, UI dispatch queue delay, layout, render-list creation, native presentation, text-layout calls, allocations, decoded-image bytes, and draft flush duration. Use synthetic message IDs and content lengths rather than message text or credentials. Keep diagnostics local and opt-in.

Fix a reference machine/build/OS/DPI and collect warm/cold runs. Record at least p50/p95, sample count, and worst outliers. Exercise empty inbox, 50/500 loaded rows, a 32k-character reader, a large valid draft, and HTML with long tables/images. A future 10k-row stress case is a toolkit/cache milestone, not a claim that today's Mail loads 10k messages.

### Initial performance budgets to validate

| Interaction | Proposed target on a recorded reference machine |
| --- | --- |
| Pointer/keyboard feedback | Visible within 50 ms at p95, independent of network completion |
| Cached message selection | Reader content visible within 100 ms at p95 |
| Scroll/resize | Aim for 16.7 ms p95 frame work on a 60 Hz display; investigate >50 ms stalls |
| Editing | No UI-thread task above 50 ms in the representative typing/paste trace |
| Idle | No continuous redraw loop; CPU near idle after a quiet 30-second interval |
| Draft flush | Target <250 ms p95 for the agreed normal-draft fixture; surface failure and preserve close protection |
| Resources | Bounded cache bytes, decoded pixels, and live graphics handles; no sustained growth over repeated open/close cycles |

These are acceptance proposals, not achieved measurements. Establish separate budgets for lower-end hardware and other platforms.

### Likely optimization order

1. Correct render defaults and profile again.
2. Avoid unnecessary rebuilding of reader strings and control trees when only status changes.
3. Cache wrapped text layout keyed by text revision, width, font, locale/direction, and DPI. The inspected label rebuilds lines during measure and render.
4. Make UI invalidation meaningful: the inspected session measures and arranges all roots every frame. Introduce correctness tests before skipping layout.
5. Evaluate bounded DirectWrite format/layout and device-resource caches. Current source creates text formats/brushes for each text draw; device loss and disposal must remain correct.
6. Coalesce pointer/resize/frame requests and schedule animations only while active. Preserve the existing dispatcher order and do not introduce a permanent 60 Hz idle loop.
7. Optimize the HTML bitmap/PNG/upload path and draft extraction only when their traces warrant it.

## 8. Accessibility and input as a quality gate

The recorded automation tree contains the native window, an unnamed pane, title bar, and window buttons. It contains no Inbox rows, labels, edits, or Send action. Broiler.UI has semantic contracts, but the Mail Windows host does not publish them through a native accessibility bridge. Microsoft's [UI Automation provider guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-providersoverview) explains why custom-rendered controls need that bridge.

Build the shared provider with stable control IDs, names, roles, bounds, focus, enabled/read-only state, selection, invocation, value/text access, scrolling, and live status updates. Password values must not leak. Visible-row virtualization must support accessible navigation to off-screen content, not simply omit the rest of the list forever.

Keep the existing keyboard shortcuts and add discoverable command labels. Test native keyboard delivery and focus restoration through actual Windows input, not just injected application events. F5 and Ctrl+3 did not visibly navigate during this tool-driven session; mouse actions worked. This is an unresolved native-focus/automation observation, **not a confirmed general keyboard regression**.

Acceptance includes Narrator and another screen reader, focus visible in both themes, 200% text/DPI scenarios, no color-only unread/error information, reduced motion, German/dead-key input, CJK composition, emoji/grapheme editing, and RTL samples. Test list, reader, composer, account setup, and preview together.

## 9. Sequenced delivery plan

Suggested staffing assumption: one Mail engineer with periodic design review and separately scheduled upstream support. Milestone ranges overlap and are not additive promises. Shared accessibility and renderer containment are substantial independent projects.

| Milestone | Deliverable | Dependencies / exit criterion | Planning range |
| --- | --- | --- | --- |
| M0 — Establish quality baseline | Reproducible screenshot fixtures, accurate docs, renderer-default correction, baseline trace | Same synthetic fixture before/after; build/tests; no behavior regressions | 3–5 working days |
| M1 — Visual foundation | Spacing/type roles, live themes, button hierarchy, sensible empty/loading/error states | Published APIs verified; light/dark and scaling review; state copy is accurate | 1–2 weeks |
| M2 — Reading flow | Structured rows, splitter, padded/selectable reader, reader-local reply, context-preserving refresh | List/control work; identity/cancellation tests; narrow layout | 2–3 weeks |
| M3 — Writing and setup | Persistent composer actions, optional recipient fields, guided setup, local validation | Existing durable draft/send invariants retained; form/focus primitives | 2–3 weeks |
| M4 — Native quality | Profile-led text/layout improvements, accessibility bridge, input/IME completion | Shared UI/Graphics/Input/Native work; real assistive-technology validation | Several weeks; estimate after spike |
| M5 — Everyday mail | Folders, attachments, search, multiple drafts/accounts, cache/sync | Existing version 3–5 roadmap and migration work | Separate feature program |

Run renderer isolation as its own gated track alongside M1–M4. Do not report M4 or an HTML-enabled release complete until its relevant gates actually pass. If upstream scheduling is tight, M1 can proceed with small Mail-local presentation adapters while keeping their eventual removal explicit.

### A concrete first ten-workday slice

1. Day 1: capture a repeatable fixture set; preserve this review's evidence; confirm render-option fix in both windows.
2. Day 2: compare antialiasing and scaling; add the focused default-options regression test upstream; correct capability descriptions.
3. Days 3–4: add reader padding/type hierarchy and distinct loading/error states; reduce repeated explanatory copy.
4. Days 5–6: wire live theme changes and semantic color roles; validate main/preview consistency.
5. Days 7–8: restructure Compose to retain visible actions without changing send semantics.
6. Days 9–10: add reader-local reply and prototype the row/splitter API; run native flow, screenshot, and regression checks.

If upstream work is unavailable, substitute the row API prototype with trace collection; do not stall all Mail improvements behind a toolkit redesign.

## 10. Longer-term product extensions

These complement the existing version plan rather than silently moving features into a visual-polish release.

| Opportunity | User benefit | Prerequisites / sequencing |
| --- | --- | --- |
| Local filter of loaded mail | Quickly find a sender/subject in the current page set | Clearly label its scope; keep separate from full-mailbox search |
| Folders and archive/trash | Ordinary mailbox organization | Folder identity, mutations, undo/recovery policy, server reconciliation |
| Attachment receive/send | Makes the client useful for everyday work | MIME metadata, file dialogs, download budgets, safe filename handling, clear progress |
| Multiple drafts | Switching tasks without discarding work | Per-draft identity, migration, sender binding, draft navigation and close behavior |
| Multiple accounts | Clear separation and a trustworthy From choice | Existing version 3 work; separate cache/state/credentials; never mix reply identity |
| Offline cache and search | Fast startup/revisits and reading through outages | Versioned bounded store, mailbox invalidation, indexing, retention and cleanup controls |
| Background sync | Less manual refreshing | Cancellation/backoff, account status, duplicate avoidance, power/network policy |
| Notifications | Awareness without constant checking | Sync/cache foundation; focus/privacy choices; avoid exposing message text by default without a setting |
| Threaded conversations | Less repeated quoted text and easier context | Reliable message threading plus an unthreaded fallback; handle broken provider headers |
| Signatures, contacts, address completion | Less repetitive composing | Identity-aware settings and an accessible recipient control |
| Snooze, send later, rules | Focus and workflow automation | Durable scheduler/queue semantics, time zones, explicit outcomes, no uncertain auto-resend |
| Rich composition | Formatted outgoing mail | Sanitized document model, plain-text alternative, paste fidelity, MIME and provider testing |
| Linux/Android | Broader reach | Follow the existing platform roadmap; build a portable interaction model, not a squeezed desktop form |

Defer speculative AI inbox features, plugin architecture, elaborate account dashboards, and ornamental animation until reading/writing, accessibility, and reliability are convincing. They add little value to a sender that is clipped or a Send button the user cannot see.

## 11. Verification and decision rules

- Preserve the existing business-level suites and add targeted behavioral coverage for changed identity, refresh retention, field errors, theme changes, and close/save transitions.
- Use deterministic visual fixtures for states and DPI/theme combinations. A screenshot that looks good at one size is not a responsive-layout test.
- Native automation must include actual input, focus transitions, scrolling, IME, and accessibility-tree assertions. Existing headless smoke tests do not cover those.
- Validate light/dark, high contrast, 640×480 / 1100×720 / wide desktop, 100–200% scale, long addresses/subjects, empty lists, failures, and long draft/message bodies.
- Select an issue only when it has a user-visible outcome, an owner, evidence, and an acceptance condition. Separate source hypotheses from measured bottlenecks.
- Ship small vertical slices: render quality; reader hierarchy; composer visibility; live themes; row structure. Review each in the real app before broadening scope.

The immediate recommendation is to implement **EX-01, EX-03, EX-04, EX-06, and EX-09 first**, while starting the shared accessibility spike. Those changes should make the app visibly clearer and easier to use without waiting for folders, offline sync, or a new architecture.
