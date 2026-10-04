# Consolidated roadmap status — 4 October 2026

Audited implementation: `d7d78d4ef4edce17c933c39707dbe175b66b09a3` (clean checkout before this documentation update). This report reconciles the product, experience, shared-component, platform, and UI roadmaps, plus the 2 October A1–A8 backlog. It supersedes older **current-status** claims, while preserving their historical investigation and acceptance records.

**Subsequent implementation, 4 October:** UI-01 inbox density selection is now
delivered (Comfortable/Compact, saved and applied live). See the [UI-01 completion
record](ui-implementation-roadmap.md#ui-01--presentation-tokens-and-a-deterministic-ui-gallery).
The tables below preserve the audit baseline; density is no longer an open item.
The next bounded implementation candidate is UI-06's non-sending SMTP test.

Most of the reading, composing, setup, and appearance redesign is implemented. The largest remaining release gate is the **HTML renderer process boundary**. Native accessibility discovery now works; actual screen-reader speech and several physical input/display checks remain. Linux and Android application work, later mailbox features, and release signing are still open.

Status vocabulary:

- **Delivered:** the stated implementation is present with relevant evidence. This does not imply every platform or future feature is accepted.
- **Acceptance pending:** implementation exists, but a specified real-world check remains.
- **Partial:** both implementation and acceptance work remain.
- **Open:** the required application capability is missing.
- **Deferred:** explicitly user-owned or conditional future work.

The same work appears under several IDs. Do not add the rows together or derive a completion percentage from their checkboxes. In particular, shared **U-01** is the toolkit layout/text package; Mail **UI-01** is the presentation/gallery package.

## Evidence and limits

| Evidence | Result / scope |
| --- | --- |
| Fresh local Release solution tests, 4 October | **421 passed: 304 shared, 114 Windows, 3 Linux adapter; zero failures or skips.** Log: `artifacts/roadmap-audit-tests.log`. These Linux adapter tests do not open a Linux display. |
| Hosted CI at the audited commit | [Run 37117131754](https://github.com/Broiler-Platform/Broiler.Mail/actions/runs/37117131754), completed successfully on 3 October. Tests/API probes passed on `windows-2025`, `ubuntu-24.04`, and `windows-11-arm`; architecture-matched unsigned NativeAOT package/smoke jobs passed for `win-x64` and `win-arm64`. This closes the old Phase 0 hosted-matrix gap. |
| Recorded native visual acceptance | [UI acceptance record](ui-acceptance-2026-10-02.md): 16 fixtures × three sizes × two themes, **96/96 clean** on published UI preview.17. Published preview.16 also passed 150% text (32/32), 200% text (32/32), and the app high-contrast preset (64/64). These are prior recorded runs, not rerun during this audit. |
| Recorded native accessibility | [UI-09](ui-implementation-roadmap.md#ui-09--native-accessibility-from-discovery-to-text-editing): external clients reached controls/patterns in Debug and NativeAOT. UI-08 records native status notification events. Actual Narrator/NVDA speech remains pending. |
| Recorded performance | [Baseline and optimizations](ui-performance-baseline-2026-10-02.md): native x64, one monitor at 150%, repeatable workloads and allocation comparisons. UI frame-build timing excludes GPU/presentation and does not establish physical input latency. |
| Consumed dependencies | [Central pins](../Directory.Packages.props): UI preview.17; Hosting preview.5; Graphics preview.7; Native preview.6; Input preview.5; HTML Core/Image/Compat preview.9; Media.Image.Managed preview.14. This is the consumed baseline, not a fresh claim about the newest NuGet release. |

Source checks include the native bridge attachment, preview host/tile implementation, Linux entry point, service contracts, measurement harness, and CI workflow. No new live-provider, physical-device, screen-reader, or sandbox acceptance was performed. Shared-component implementation claims below distinguish existing roadmap records from APIs actually adopted by Mail; this is not a fresh audit of every sibling repository.

## Remaining work in practical delivery order

| Priority | Work still to do | Owner and dependency | Completion evidence |
| --- | --- | --- | --- |
| P0 release gate | Implement the restricted HTML renderer and broker: private versioned IPC, bounded pixels/assets, denied credentials/files/network/process access, trusted link decisions, cancellation, crash recovery, and OS-enforced limits. | Mail preview host plus platform security/renderer integration; A2, version 2, platform phase 2. Inline HTML depends on it. | Every applicable check in the [renderer security specification](html-renderer-security.md) passes against packaged adapters, including a hostile child independent of sanitization. |
| P0 accessibility gate | Run Narrator and another screen reader through inbox, reader, composer, setup, feedback, and preview. Resolve any speech, focus, disclosure, validation, or text-navigation defects. | Hosting/UI semantics plus Mail labels/state; UI-09/H-01. | Recorded speech and keyboard operation, including loading/errors, selection, password protection, and draft state. External UIA discovery is already fixed. |
| P1 native acceptance | Complete physical AltGr/dead-key/IME composition, cancellation, undo and autosave tests; precision wheel/touchpad; real 100/200% DPI, monitor movement, OS text/contrast/motion settings; interactive ARM64 UI. | UI-05/07/10/13; appropriate settings, input devices, and target machines. | Append actual environments/results to UI-13. App text-scale overrides and ARM64 package smoke do not close these checks. |
| P1 visible feature | Add compact/comfortable density with supported shared control metrics. | UI-01, shared UI if an API is missing. | Persisted choice, readable rows/forms, focus and hit targets at minimum size and 200% text. |
| P1 setup feature | Add a cancellable, bounded **non-sending SMTP connection/authentication test**, then expose its result in setup. | Core/Infrastructure service first, then UI-06. | Controlled TLS/auth/failure/cancellation tests; no message submission; credential identity rules preserved. |
| P1 measured performance | CPU-profile resize/selection reflow; add GPU/presentation and real input timing; profile long HTML/tile misses and 100/200% DPI; agree reference-machine budgets. | UI-12, Graphics diagnostics where needed. | Comparable measurements with correctness checks; budget assertions based on measured scope. |
| P1 NativeAOT assurance | Exercise persisted-data round trips, legacy credential keys, HTML/image paths, and controlled IMAP/SMTP fixtures in the published executable on x64/ARM64; replace the HTML substitution when upstream APIs permit. | A7, Mail CI and HTML.Image. | Native executable acceptance beyond four-tab smoke; warning-free publish; reviewed renderer-version changes and managed/native rendering equivalence. |
| P2 product/platform | Deliver multi-account and everyday mailbox services, then their UI; build actual Linux and Android applications. Add localization/RTL and release distribution/recovery work. | Product versions 3–7, cross-platform phases 1–4, UI-14. | The milestone-specific acceptance below. |

Two small recorded polish findings also remain: UIA bounds can extend outside a scroll viewport, and a focused read-only scroll view lacks a focus ring. Track these with Hosting/UI acceptance rather than repeating the earlier discovery investigation.

## Mail UI work packages: UI-01 through UI-14

| ID | Status now | What is still open |
| --- | --- | --- |
| UI-01 — presentation and gallery | **Partial** | Tokens, typography/system text scaling, and 16 deterministic fixtures are implemented. Add density selection. Long translated labels depend on UI-14 localization; do not count the entire gallery as missing because that combined checkbox is unchecked. |
| UI-02 — refresh continuity | **Acceptance pending** | Identity-based selection/body/scroll reconciliation and regressions are implemented. Verify native refresh while new mail arrives, including unchanged, moved, and vanished selections. |
| UI-03 — reader and reply | **Acceptance pending** | Header hierarchy, margins, selectable metadata/body, local reply commands, and existing-draft protection are implemented. Finish native click/focus and screen-reader acceptance. |
| UI-04 — responsive inbox | **Acceptance pending** | Compact/split layouts, readable sender/subject/date rows, wrapping toolbars, and 200% text fixes are implemented. Verify a real DPI/monitor transition while compact and preserve focus/context. |
| UI-05 — composer | **Acceptance pending** | Remaining-space body editor, compact header, optional Cc/Bcc, persistent actions, and feedback are implemented. Verify caret/selection/undo and actual IME composition across autosave/status updates and disclosure changes. |
| UI-06 — setup | **Partial** | Guided checklist, advanced settings, saved/unsaved credential state, and field validation are implemented. SMTP test service/UI is missing; IMAP testing already exists. Translated-label acceptance follows UI-14. |
| UI-07 — appearance/geometry | **Partial** | Live theme, system text size, open-preview updates, and debounced/versioned geometry persistence are implemented. Check actual Windows contrast colors and reduced-motion behavior, real monitor/DPI transitions, and RTL. The tested app high-contrast preset does not verify Windows custom contrast colors. |
| UI-08 — state and feedback | **Acceptance pending** | Loading/error/recovery policy, deduplicated announcements, and failure fixtures are implemented; native notification text is verified. Finish actual screen-reader speech with UI-09. |
| UI-09 — accessibility | **Acceptance pending** | External native discovery, invoke/selection/value/text patterns, labels, protected passwords, and notification events now work. Finish screen-reader speech, field-validation relationships, Cc/Bcc disclosure, paragraph navigation at blank separators, and the remaining ownership/lifetime acceptance. |
| UI-10 — input/focus | **Partial** | Central shortcuts, exact modifiers, shared traversal, posted Unicode, synthetic wheel tests, and the Hosting preview.5 IME deduplication fix are delivered. Physical AltGr/dead keys, full IME lifecycle/undo, precision/tilt scrolling, and DPI caret placement remain. Remove legacy Graphics event adapters only after an equivalent consumed neutral source is available. |
| UI-11 — HTML ergonomics | **Partial** | Lifecycle, selected-message identity, theme/text size, keyboard/UIA, scroll anchoring, resize, and tile scaling are implemented. Inline reader design/integration waits for renderer isolation. |
| UI-12 — performance | **Partial** | Repeatable harness, baseline, RichEdit allocation reductions, direct pixel upload, and byte/pixel cache limits are delivered. Resize/select CPU, GPU/presentation timing, real input latency, text-layout/tile-miss accounting, long-HTML workloads, broader DPI evidence, and agreed gates remain. |
| UI-13 — acceptance | **Partial** | NativeAOT gallery/UIA/keyboard/screenshot automation and published-package results exist. Finish actual display/OS-setting/device/speech/ARM64 interactive checks and later localization/RTL coverage. Repeat affected scenarios after subsequent UI/Hosting releases. |
| UI-14 — later UI | **Open** | Linux host adaptations; Android list/detail, touch, insets/back/lifecycle; folders/search/attachments; account/sender identity; NativeAOT-compatible localization resources and RTL layout. Implement the underlying services before adding their controls. |

The UI delivery slices are consequently: **slice 1 baseline delivered; slices 2–4 implemented with the remaining checks above; slice 5 awaits IME/editing acceptance; slice 6 awaits SMTP testing and appearance/speech checks; slice 7 awaits real native input/speech; slice 8 is partial measurement and matrix coverage.** Their implementation should not be restarted.

### What changed since the 2 October audit

The native UIA failure was caused by attaching both native bridges before their HWNDs existed. [WindowsMailWindow.OnCreated](../src/Broiler.Mail.Windows/Hosting/WindowsMailWindow.cs) now attaches them after creation; `NativeBridgeAttachmentTests` covers real handles, external `WM_GETOBJECT`, and single delivery of posted characters. The old view-inclusion-property theory was an investigation lead, not the established cause.

Hosting preview.5 also removes the wall-clock-dependent IME echo suppression that intermittently failed under load. The UI roadmap records 20 consecutive successful runs after removing the warm-up workaround. Real user IME composition remains a separate acceptance task.

[HtmlPreviewWindow](../src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs) now uploads `BBitmap.ToPixelBuffer()` directly, caps each tile at 8 × 1024 × 1024 pixels, and caps cached tiles at 256 MiB. The old PNG round trip and count-only memory bound are fixed. These in-process cache limits are distinct from the stricter proposed IPC tile/process ceilings in the security specification.

## Original experience IDs and milestones

The [30 September experience review](experience-roadmap.md) intentionally preserves its original findings. Its unchecked “Proposed” rows are not the current implementation state.

| ID | Current disposition | Remaining scope / current owner ID |
| --- | --- | --- |
| EX-01 — rendering defaults | **Delivered** | Explicit Mail rendering policy and recorded upstream correctness work; continue display acceptance under UI-13. |
| EX-02 — structured rows | **Delivered core; acceptance pending** | Rows and splitter are adopted; optional snippets remain optional, density remains UI-01, real DPI remains UI-04/13. |
| EX-03 — reader | **Acceptance pending** | Implemented under UI-03; native focus/speech checks remain. |
| EX-04 — composer | **Acceptance pending** | Implemented under UI-05; actual IME/editing continuity remains. |
| EX-05 — local replies | **Acceptance pending** | Implemented under UI-03; native interaction/speech remains. |
| EX-06 — loading/error states | **Delivered; speech pending** | State implementation is complete; speech shares UI-08/09 acceptance. |
| EX-07 — stable refresh | **Acceptance pending** | Implemented under UI-02; new-mail native check remains. |
| EX-08 — account setup | **Partial** | Guided setup delivered; non-sending SMTP test remains UI-06. |
| EX-09 — live preferences | **Partial** | Live appearance/geometry delivered; remaining OS-setting/RTL acceptance is UI-07. |
| EX-10 — accessibility | **Acceptance pending** | Native tree failure fixed; actual assistive-technology checks remain UI-09. |
| EX-11 — HTML flow | **Partial** | Preview ergonomics delivered; containment and inline reading remain A2/UI-11. Earlier “Done” referred to the preview slice, not the whole integrated-reader proposal. |
| EX-12 — smoothness | **Partial** | Measured optimizations exist; remaining profiling/coverage/budgets are UI-12. |
| EX-13 — docs and fixtures | **Delivered baseline; maintenance ongoing** | Current fixtures and capability documentation exist. This audit corrects stale package, UIA, and CI status pointers; maintain them as behavior changes. |

Experience milestones: **M0** baseline delivered; **M1** presentation implemented with OS/display acceptance remaining; **M2** reading implemented with native acceptance remaining; **M3** writing/setup implemented with IME acceptance and SMTP testing remaining; **M4** native quality partial; **M5** everyday mail open in versions 3–5. The initial ten-workday slice is substantially delivered; its old scheduling recommendations should no longer drive selection of the next task.

## Shared Broiler component roadmap

The [component plan](broiler-experience-components-roadmap.md) marks most implementation slices complete. Keep publication, Mail adoption, and broader cross-consumer/native acceptance separate.

| ID | Current disposition | Remaining integration or acceptance |
| --- | --- | --- |
| G-01 — render defaults | **Implementation recorded complete** | Mail supplies explicit options; no new Mail default fix indicated by this audit. |
| G-02 — text resources/metrics | **Implementation recorded complete** | Continue measured CPU/allocation work from UI-12; an upstream checkbox is not a Mail performance-budget pass. |
| G-03 — window/frame services | **Partial consumption** | Window/DPI integration exists; the consumed Graphics preview.7 does not supply the `FrameRendered` timing hook identified by the measurement report. Publish/adopt required diagnostics, then measure GPU/presentation. |
| U-01 — layout/text correctness | **Implementation recorded complete** | Current UI package carries later layout/text fixes too. Remaining Mail wrapper removal is a focused integration/cleanup question, not a repeat of the original U-01 project. |
| U-02 — tokens/live appearance | **Delivered in Mail** | Density, real OS contrast/reduced-motion acceptance, and localization/RTL are UI-01/07/14. |
| U-03 — focus/commands/semantics | **Delivered core** | Native screen-reader acceptance and the reported scroll-view focus-ring issue remain. |
| U-04 — scheduling/selective work | **Implementation recorded complete** | Idle baseline records zero frames. Profile remaining resize/selection computation and validate any further invalidation optimization. |
| C-01 — list presentation | **Delivered in Mail** | Responsive rows and structured fields are present; optional snippets/density and broader stress/native checks remain. |
| C-02 — splits/responsive panels | **Delivered in Mail** | Compact mode and persistent split geometry exist; real DPI transitions remain. |
| C-03 — selectable text | **Delivered in Mail** | Actual screen-reader paragraph behavior, IME editing, and reflow performance remain. |
| C-04 — forms/feedback | **Delivered in Mail** | Form surfaces, reveal fixes, persistent actions, validation and feedback exist; actual speech/input acceptance remains. SMTP testing is a Mail service gap. |
| H-01 — native accessibility | **Acceptance pending; discovery fixed** | Generated COM provider is consumed through Hosting. Narrator/second-reader acceptance and remaining semantics/lifetime checks remain. |
| H-02 — native input/scroll | **Delivered implementation; acceptance pending** | Published fixes are consumed. Physical input/DPI cases and eventual legacy-adapter replacement remain UI-10. |
| H-03 — shared hosting | **Delivered adoption** | Mail uses published Hosting preview.5, including the Android API probe. This does not implement Linux/Android Mail or independently verify a second application's adoption. |
| R-01 — bounded rendering | **Delivered current preview implementation** | Tiles/cache/scaling are implemented; long-HTML performance and sandbox pixel transport remain. |
| R-02 — resource limits/progress | **Delivered policy implementation** | Resource denial/limits remain valuable, but OS containment and broker-controlled fetch acceptance are still required. |
| R-03 — layout diagnostics | **Implementation recorded complete** | Extend reproducible performance and shared-consumer evidence; do not equate snapshot diagnostics with measured end-to-end latency. |

Upstream batches: **A** correctness implemented; **B** presentation implemented in Mail, second-demo acceptance not established here; **C** caches/timing partially evidenced, broader performance exit conditions open; **D** hosting/accessibility/input implemented in Mail, native speech/input and cross-consumer acceptance open; **E** bounded rich content implemented, but its explicit renderer-isolation exit condition is **still open**.

The shared quality gallery remains **partial**: Mail has 16 fixtures and a native acceptance script, but the proposal also calls for the same cases across Mail/Code/Writer/Browser and hosts/backends, plus 50/500/10,000-item, long-text, resize, and large-paste traces. This audit does not establish that wider gallery.

The older [component reuse review](component-reuse-review.md) has largely advanced through dispatch, literal labels, standard focus traversal, native declarations, viewport constraints, and Hosting adoption. `TabContent` is still used in the shell; `ViewportScrollView` now delegates width constraints to the standard control. Review remaining wrappers against current APIs and remove only demonstrated redundancy. Generic persistence, secret-store services, and HTML-to-text extraction remain conditional on a real second consumer; mail identity, credentials policy, transports, and view models remain Mail responsibilities.

## A1–A8 from the previous remaining-improvements audit

| ID | Current status | Remaining work |
| --- | --- | --- |
| A1 — native UIA discovery | **Original defect closed; acceptance pending** | Screen-reader speech and remaining UI-09 semantics. Do not reopen the HWND-discovery investigation without a regression. |
| A2 — renderer process | **Open** | Full security specification, broker, restricted child, and packaged containment evidence. |
| A3 — Receive continuity | **Acceptance pending** | UI-02 native new-mail check. |
| A4 — reader/reply | **Acceptance pending** | UI-03/04 native focus/speech and real DPI checks. |
| A5 — live preferences | **Partial** | UI-07 real system colors/motion/display/RTL acceptance. |
| A6 — compose/setup | **Partial** | UI-05 real IME/undo; UI-06 SMTP test service/UI. |
| A7 — NativeAOT assurance | **Partial** | x64/ARM64 publishing/smoke and x64 native UIA are established. Published-executable data/credential/transport/rendering acceptance and removal of the version-specific HTML substitution remain. |
| A8 — performance/memory | **Partial** | Baseline, allocation fixes, direct pixels and byte limits are delivered. Broader timing, CPU profile, long HTML and DPI matrix remain UI-12. |

## Product roadmap: versions 1–7

| Version | Status | Open scope |
| --- | --- | --- |
| 1 — settings/receive | **Implemented; user-deferred acceptance** | Live IMAP provider acceptance remains explicitly deferred to the user. Controlled fixtures and passing tests do not certify an external provider. |
| 2 — send/HTML | **Partial** | SMTP/reply/local draft/Sent-copy and preview features exist. Live SMTP/Sent-copy provider validation remains user-owned; renderer process isolation remains a release gate. |
| 3 — accounts | **Open** | Add/edit/disable/remove/switch multiple accounts; isolate credentials/settings/drafts/connections/messages; preserve sending/reply identity; define local removal/unsent-draft policy. Verify simultaneous accounts, offline one-account behavior, UID separation, and refresh during switching. |
| 4 — mailbox management | **Open** | Folder browsing; read/unread, stars, move/archive/trash and feasible undo; inbound/outbound attachments with limits/progress/safe names; clearly scoped loaded/server search; multiple drafts, signatures and provider folder mapping. Existing Sent-copy configuration is only a subset. |
| 5 — sync/offline | **Open beyond current draft recovery** | Versioned bounded cache, retention/offline folders, periodic/incremental/IDLE sync and backoff, deletion/flag/UIDVALIDITY reconciliation, offline reading and explicit pending-operation/conflict handling, private per-account notifications and quiet hours. |
| 6 — productivity | **Open** | Unified inbox, reference-based threading, contacts/recipient completion, saved searches/tags/templates/configurable shortcuts, opt-in rules/snooze, and rich HTML composition with plain-text/MIME interoperability. Existing fixed shortcuts are not user configuration. |
| 7 — coverage/release polish | **Partial foundation** | OAuth/provider matrix; Linux/Android applications; remaining speech/contrast/input/localization/RTL acceptance; signed distribution and upgrade/recovery/diagnostics; large-mailbox/startup/memory evidence. CI, NativeAOT packaging and part of UI/performance work are already delivered. |

Beyond-core ideas remain **deferred proposals**: `.eml` import/export, mailbox backup, invitations/contact synchronization, S/MIME/OpenPGP and key management, scheduled sending, advanced server search, JMAP/additional protocols, and a browser edition. The experience roadmap's local filter, folder/attachment/draft/account/cache/sync/notification/thread/signature/contact/rule/rich-composition proposals map to versions 3–6 above. None is implemented merely because the visual shell has improved.

## Cross-platform roadmap: every phase

| Phase | Status | Open scope |
| --- | --- | --- |
| 0 — dependencies/contracts/CI | **Delivered; exit gate now evidenced** | Current-head hosted tests and both Windows package jobs passed. Keep the matrix enforced; no new Phase 0 implementation is needed. |
| 1 — Linux X11/EGL | **Partial foundation** | [Linux Program](../src/Broiler.Mail.Linux/Program.cs) still offers diagnostics/help only. Connect the native window/event/render lifecycle to Mail, scheduling, focus/resize/close and draft flush; integrate desktop pointer/keyboard/IME/clipboard; XDG paths/permissions/recovery; native checks and publish/dependency documentation. |
| 2 — Linux secure services/package | **Open** | Secret Service identity/unlock/cancellation/session-only fallback; shared broker/restricted preview; denial/limits/crash acceptance also on Windows; clean-system x64/ARM64 tarballs and TLS/mail checks; separately validated AppImage/Flatpak. |
| 3 — Android foundation | **Open** | No Android Mail head exists. Add Activity/surface/render lifecycle, touch/clipboard/InputConnection/composition/selection/focus, Keystore capabilities and backup/key-loss handling, durable rotation/recreation/process-death recovery, build/device/emulator checks. |
| 4 — mobile UX/release | **Open** | Single-pane navigation, touch targets, text scale, back/IME insets; secure HTML; WorkManager sync/background coordination/notifications/permissions; protected signing and APK/AAB/checksum/install acceptance; explicit device/API/ABI coverage. |
| Later — Wayland/Vulkan | **Deferred integration/acceptance** | Verify current upstream prerequisites before adoption; presentation/ownership/input/clipboard/scaling, driver/device-loss and hardware/software coverage, native packaging. Keep the initially validated X11/EGL route. No Mail support claim is established. |

CI now proves Linux managed execution and architecture-matched Windows tests/native package smoke. It does **not** prove a Linux GUI, Android device execution, or interactive ARM64 visual/speech acceptance. Signing/release publication and clean-machine installation/upgrade checks remain separate from the successful unsigned CI artifacts.

## Next task recommendation

For the next bounded UI implementation, choose **UI-01 density selection**. For setup usefulness, choose the **UI-06 non-sending SMTP test** as a service-to-UI slice. For release readiness, prioritize **A2 renderer isolation** and complete **UI-09/13 native acceptance** alongside those changes. Preserve the existing gallery and measured baseline as regression evidence instead of starting another broad visual redesign.
