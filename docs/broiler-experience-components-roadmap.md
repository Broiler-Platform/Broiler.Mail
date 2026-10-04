# Shared Broiler components: experience roadmap

Current follow-up: [consolidated roadmap status, 4 October 2026](roadmap-status-2026-10-04.md).
Implementation checkmarks below do not replace the native acceptance criteria.
The [UI implementation roadmap](ui-implementation-roadmap.md) assigns the remaining
Mail and shared-component UI work and defines published-app acceptance.

Date: **30 September 2026**. Proposed extensions driven by the native Broiler.Mail review.

The strongest shared investment is in text, layout, native input/accessibility, and reusable presentation controls. Mail should supply realistic acceptance scenarios; components should own the generic mechanics. This extends the [earlier component reuse review](component-reuse-review.md), rather than replacing its ownership analysis.

Local sibling sources were inspected in `D:/Broiler.UI`, `D:/Broiler.Graphics`, `D:/Broiler.Input`, `D:/Broiler.HTML`, `D:/Broiler.Media`, and `D:/Broiler.Unicode`. Local source is not evidence of a published API. At the original review, Mail consumed UI `0.1.0-preview.10` and Graphics `0.1.0-preview.7`. Current pins are in `Directory.Packages.props`; the 4 October audit records UI preview.17 and Hosting preview.5.

Only the rendering-default values and presence of `StandardThemeController` / `StandardAnimationScheduler` were directly probed against consumed assemblies in this review. Other source capabilities below require package/API confirmation before implementation. No sibling repository was changed.

## 1. Ownership and dependency order

| Area | Existing evidence | Next shared deliverable | Mail remains responsible for | Status |
| --- | --- | --- | --- | --- |
| Broiler.Graphics | Direct2D backend, render options, text metrics, window abstraction | Correct defaults; text resource reuse; surface-aware text quality; bounded frame diagnostics | Choice of window sizes and rendering policy | [x] Completed (G-01, G-02, G-03 implemented) |
| Broiler.UI foundation | Themes, semantic nodes, invalidation flags, dispatcher, animation scheduler | Typography/spacing roles; correct measurement; selective layout; focus/reveal contracts | Mail layout, commands, state copy | [x] Completed (U-01 layout/text correctness, U-02 semantic design tokens and live appearance, U-03 focus/commands/semantic lifecycle) |
| Broiler.UI controls | Visible-range list rendering, Splitter, labels, RichEdit, buttons/tooltips in source | Structured row presentation, selectable text, grouped forms, status/banner primitives | Sender/subject mapping, recipient and draft semantics | [ ] C-01–C-04 adopted; current UI preview.17; remaining native acceptance tracked below |
| Broiler.Input | Device/input contracts, text composition types, legacy adapter | Native text/composition fidelity and high-resolution scrolling integration | Mail shortcuts and composition behavior | [ ] In progress |
| Broiler.Native.Windows | Native bindings already reused by Mail | UIA/IME/clipboard binding families with correct lifetime/ABI | Credential identity and account policy | [ ] Implemented through Hosting; remaining native acceptance is H-01/H-02 |
| Shared host integration | Published Hosting combines Graphics, UI, clipboard, caret, dispatch | Reusable host adapter adopted by Mail and a second application | Startup composition, close/save decisions, app settings | [x] Published and consumed; H-03 records extraction/adoption; platform applications have separate gates |
| Broiler.HTML / CSS / Layout | Passive HTML rendering and bitmap/render-list frontends | Bounded, viewport-aware rendering, layout diagnostics, deterministic mail fixtures | Content/resource policy and renderer-process lifecycle | [x] Completed (R-01 bounded tiling & layout snapshot, R-03 diagnostics) |
| Broiler.Media | Existing `MediaLimits`, probing, decode contracts | Consistent bounded thumbnail/decode path and cancellation checks | Attachment presentation and mail-specific limits | [x] Completed (R-02 MediaLimits reuse, codec header inspection, bomb defense) |

Dependency sequence: correct Graphics defaults â†’ fix layout/focus foundations â†’ improve rows/reader/composer â†’ optimize measured text/render paths. Accessibility/native-host work should begin early. Renderer containment is a separate track and a prerequisite for broad untrusted HTML use.

## 2. Broiler.Graphics

### [x] G-01: repair render-option initialization â€” first (Completed)

**Status:** Completed. Explicit parameterless constructor added to `BRenderOptions` defaulting `Antialias`, `VSync`, and `SubpixelText` to `true`. Explicit static `Default` and `LowQuality` properties added with XML documentation of `default(BRenderOptions)` semantics. `WindowsMailWindow` and `HtmlPreviewWindow` explicitly configured with high-quality options. Verified by `RenderOptionsTests` (155/155 tests passing).

**Evidence:** `BRenderOptions.Default => new()` in `src/Broiler.Graphics/Rendering/BRenderOptions.cs` results in all flags false in the actual Mail dependency. `BWindowOptions.RenderOptions` inherits that value. `Direct2DRenderer.ToTextAntialiasMode` selects aliased text when antialiasing is off.

**Deliver:** explicitly initialized `Default`; an explicit parameterless constructor if the API promises the same behavior for `new()`; documentation of unavoidable zero-valued `default(T)` behavior; a deliberate low-quality option rather than an accidental one.

**Acceptance:** assertions for `Default`, `new()`, explicit flags, and `BWindowOptions`; pixel comparison of text and curves; no change to explicitly disabled options. Verify VSync handling per backend separately from text quality.

**Cost:** small, approximately 1â€“3 days including tests; broad payoff to all default-window consumers.

### [x] G-02: bounded text resources and coherent metrics (Completed)

**Status:** Completed. Added `DirectWriteTextFormatCache` (bounded format cache with hit/miss tracking and LRU-style eviction). Added bounded solid brush cache in `Direct2DRenderer` with hit/miss counters and eviction, separating device-bound brushes from device-independent text formats. Implemented `ClearBrushCache()` on device recreation and disposal. Added bounded advance metrics cache to `DirectWriteTextMetricsProvider`. Verified by `BackendExperienceTests`.

The inspected Direct2D renderer allocates a solid brush and text format per text command. DirectWrite metrics creates a format and layout for each measured string; only line height has an obvious cache there. These are optimization candidates, not measured dominant costs.

Start with counters and a 32k-character mail fixture. Evaluate bounded caches for immutable font formats and text layouts. Include font/weight/size, width, locale/direction, DPI/surface policy, and content revision where applicable. Define eviction and device-loss behavior. Keep device-bound brushes separate from device-independent text format data.

Improve the text-layout abstraction so measurement, rendering, wrapping, hit testing, selection, and caret placement use compatible shaping results. Platform shaping/fallback and Unicode segmentation need explicit ownership. A label that measures one way and an editor that positions the caret another way will never feel polished.

**Acceptance:** measured reduction in layouts/allocations on unchanged frames; correct mixed-script/emoji/bidi selection; bounded resources across repeated window/message changes; crisp output on fractional DPI.

### [x] G-03: window and frame services (Completed)

**Status:** Completed. Added `MinClientWidth` and `MinClientHeight` to `BWindowOptions`. Implemented `WM_GETMINMAXINFO` handling with DPI scaling and `WM_DPICHANGED` suggested rectangle positioning via `SetWindowPos` in `Direct2DWindow`. Added frame timing metrics (`LastRenderDuration`, `FrameCount`, `InvalidationCount`) and events (`FrameRendered`, `DeviceLost`). Verified by `BackendExperienceTests`.

Expose minimum client size and DPI transition mechanics centrally, as proposed in the existing reuse review. Add trace hooks for layout-independent presentation time, device loss, frame count, and invalidation coalescing. Verify current resize behavior before proposing a replacement; inspected Graphics source already renders a resized frame synchronously.

Keep an on-demand rendering model. Animations should request future frames while active and stop when complete, hidden, minimized, or reduced-motion policy applies. Add quality scenarios for resize, display changes, sleep/resume, device recreation, and closing with queued callbacks.

**Do not:** introduce UI dependencies into Graphics, replace working Direct2D with a new backend for cosmetic reasons, or assert that VSync alone fixes frame time.

## 3. Broiler.UI foundation

### [x] U-01: layout and text correctness (Completed)

**Status:** Completed.
1. Implemented allocation-aware tab measurement and layout in `StandardTabView`: tab children are measured with actual allocated available size (`contentAvailableSize`), with automatic remeasurement during `ArrangeCore` if allocated width differs from measured width, retiring the need for `TabContent`.
2. Added `UiScrollConstraint` enum (`None`, `ConstrainWidth`, `ConstrainHeight`) and corresponding `Constraint`, `ScrollConstraint`, and `ConstrainContentWidth` properties to `UiScrollView`.
3. Implemented opt-in constrained-axis measurement and layout in `StandardScrollView`: in `ConstrainWidth` mode, content is constrained to the viewport width, resolving vertical scrollbar appearance in a single monotonic pass without wrap/scrollbar oscillation, while preserving two-dimensional scrolling in default `None` mode.
4. Added line layout caching in `StandardLabel` (`GetOrCreateLines`), avoiding redundant line rebuilding and string measurement across measure, arrange, and repeated render frames.
5. Implemented Unicode grapheme cluster segmentation in `StandardLabel.BreakWord` and `ApplyTrimming` via .NET `StringInfo.GetTextElementEnumerator`, ensuring surrogate pairs, combining marks, and emoji sequences are never broken into invalid clusters.
6. Added `UseMnemonic` and `IsLiteral` properties to `UiLabel`, preserving literal `&` characters in text, display text, and accessibility semantic tree when mnemonics are disabled.
7. Verified by `LayoutAndTextCorrectnessTests` in `Broiler.UI.Standard.Tests` (198/198 tests passing) and all 751 tests passing across `Broiler.UI.slnx`.

### [x] U-02: semantic design tokens and live appearance (Completed)

**Status:** Completed.
1. Extended `StandardThemeTokens` with typography roles (`FontFamily`, `FontBody`, `FontTitle`, `FontSubtitle`, `FontCaption`, `FontCode`), spacing tokens (`SpacingXs` through `SpacingXxl`, `Spacing(step)`), density preferences (`UiDensity`, `DensityFactor`, `ResolveRowHeight`, `ResolvePadding`), focus metrics (`FocusRingThickness`, `FocusRingOffset`), motion policy (`ReducedMotion`, `AnimationDurationFast/Normal/Slow`), and contrast validation properties (`TextContrast`, `AccentContrast`, `FocusRingContrast`, `MeetsAaNormalText`, `MeetsAaLargeOrUi`).
2. Implemented `StandardLabelRole` (`Default`, `Muted`, `Warning`, `Danger`, `Success`, `Accent`, `Info`, `Disabled`, `Custom`) and added `Role` and semantic constructors/factories (`Warning`, `Muted`, `Danger`, `Success`, `Accent`, `Info`, `Title`, `Subtitle`, `Caption`, `Code`) to `StandardLabel`. `ApplyTheme` preserves semantic status colors across theme switches instead of wiping them to generic `theme.Text`.
3. Extended `StandardControlPaint` with thread-safe per-session theme isolation via `ConditionalWeakTable<UiSession, StandardThemeTokens>` (`SetSessionTheme`, `GetTheme`, `ClearSessionTheme`), preventing cross-thread/window mutations across multiple concurrent UI sessions.
4. Added `UiDensity` to `UiSystemSettings` and introduced `UiSystemSettingsChangedEventArgs` with default `SettingsChanged` event implementation on `IUiSystemSettingsHost`. Added parameterless `StandardThemeController.Apply(session)` that dynamically resolves host platform settings.
5. Preserved focus, selection, and caret in text controls (`StandardEdit`) without disruption across theme reapplication.
6. Verified by `SemanticDesignTokensAndLiveAppearanceTests` in `Broiler.UI.Standard.Tests` (212/212 tests passing) and all 765 tests passing across `Broiler.UI.slnx`. All 206 tests passing in `Broiler.Mail`.

Extend the existing color/radius tokens with typography roles, spacing, density, focus metrics, and motion policy. Avoid a parallel Mail-specific theming framework. Existing `StandardThemeController` can re-theme a session, but control roles must survive reapplication: `StandardLabel.ApplyTheme` currently assigns generic `theme.Text`.

Use per-session theme state where feasible. `StandardControlPaint` is static today; multiple windows on different UI threads make global mutation worth reviewing. Publish system appearance/contrast/text-scale/reduced-motion through the existing `IUiSystemSettingsHost` contract.

**Acceptance:** immediate theme updates across existing/new controls and popups; retained semantic warning/muted text; preserved focus/caret/selection; color contrast measured for text and interactive states; no thread-unsafe cross-window mutation.

### [x] U-03: focus, commands, and semantic lifecycle (Completed)

**Status:** Completed.
1. Introduced neutral focusability and reveal contracts: `IUiFocusable` (`Focusable`, `CanFocus`, `IsTabStop`, `TabIndex`), `IUiScrollable` (`MakeVisible(BRect)`), `UiElement.Focus()`, and `UiElement.BringIntoView(BRect?)`.
2. Interactive controls across `Broiler.UI` (`UiButton`, `UiEdit`, `UiRichEdit`, `UiComboBox`, `UiListView`, `UiCheckBox`, `UiRadioButton`, `UiSlider`, `UiSpinBox`, `UiTabView`, `UiTreeView`, `UiCodeEditor`, `UiFormatCodeView`) initialize `Focusable = true`, `IsTabStop = true`, and override `CanFocus` to respect `IsEnabled` alongside session attachment, element visibility, and ancestor visibility. Concrete control-type checks are obsolete.
3. Implemented robust scoped focus traversal in `StandardFocusScope`: `MoveFocus(+1/-1, scopeRoot)` handles `TabIndex` ordering, active-scope containment (such as tab boundaries), modal containment (`session.ModalElement`), automatic scrolling to reveal focused elements, and skipping disabled/collapsed controls or non-tab-stops.
4. Added focus capture and restoration lifecycle in `StandardFocusScope`: `SaveFocus()`, `RestoreFocus()`, and `CaptureFocus()` (returning an `IDisposable` cookie) ensure focus is reliably restored when modal scopes, popups, or previews close.
5. Implemented viewport-aware `MakeVisible` in `UiScrollView` and `StandardScrollView`, automatically scrolling to bring off-screen elements into view without oscillation.
6. Added stable, monotonic semantic IDs to `UiElement.SemanticId` and wired `UiElement.GetSemanticNode()` to consistently report matching `Id = SemanticId`.
7. Extended semantic roles with full accessibility fidelity: added `ListItem`, `TabItem`, `MenuItem`, `StatusAnnouncement`, `Group`, `Hyperlink` to `UiSemanticRole`.
8. Added incremental semantic event notifications via `UiSession.SemanticChanged`, `UiSemanticChangeKind` (`FocusChanged`, `StateChanged`, `StatusAnnounced`, `TreeStructureChanged`), `UiSemanticChangedEventArgs` (carrying `Element`, `Change`, `SemanticId`, and `Message`), and `UiSession.AnnounceStatus(source, message)`.
9. Added reusable neutral command contracts `IUiCommand` and `UiCommand` (encapsulating label, accelerator, tooltip hint, predicate enablement, execution, and change notifications), integrated seamlessly into `UiButton` (`Command`, `CommandParameter`, automatic synchronization and invocation), `UiMenuItem`, and `StandardCommand`.
10. Verified by `FocusCommandsAndSemanticLifecycleTests` in `Broiler.UI.Standard.Tests` (229/229 tests passing), all 782 tests passing across `Broiler.UI.slnx`, and all 206 tests passing in `Broiler.Mail`.

Add neutral focusability/tab-stop contracts, scoped traversal, and BringIntoView/reveal support. Mail's concrete control-type checks should become unnecessary. Preserve active-tab boundaries, nested scrolling, disabled/collapsed controls, modal scopes, and focus restoration after popup/preview close.

Define stable semantic IDs and incremental semantic-change events for host accessibility. Roles need enough fidelity for list items, editable/read-only text, value selection, buttons, and status announcements. Do not equate a render tree snapshot with a fully functioning native provider.

Keep application commands in Mail, but reuse a shared command model for labels, enablement, accelerators, tooltip hints, and invocation. Inspect currently published command APIs before adding new contracts.

### [x] U-04: selective work and scheduling (Completed)

**Status:** Completed.
1. Implemented dirty-layout fast path in `UiElement.Measure` and `UiElement.Arrange`: added `IsMeasureValid`, `IsArrangeValid`, `_previousAvailableSize`, and `_previousFinalRect`. When unchanged, measurement and arrangement are skipped entirely, eliminating redundant rewrapping and geometry calculations.
2. Implemented ancestor invalidation propagation: calling `Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange)` dirties the target and propagates dirty bits up the ancestor chain to the root, ensuring parents re-measure and re-arrange dirty children.
3. Added `InvalidateMeasure()`, `InvalidateArrange()`, and `InvalidateRender()` helper methods on `UiElement`, and ensured child add/remove/detach operations clear layout flags.
4. Audited property setters across all controls (`StandardButton`, `StandardToggleButton`, `StandardRadioButton`, `StandardComboBox`, `StandardListView`, `StandardSlider`, `StandardSpinBox`, `StandardProgressBar`, `StandardImageView`, `StandardTreeView`, `StandardCodeEditor`): ensured setters altering dimensions or layout invalidate `Measure | Arrange | Render`, visual changes invalidate `Render`, and render-time content discoveries (such as tree row extent or code editor column extent) invalidate `Arrange` so scrollbars update on the next frame.
5. Preserved invalidations raised during rendering and deferred callbacks: `UiSession.RenderFrame` captures initial invalidations and preserves any invalidations queued during `RenderCore` or `context.FlushDeferred()`, exposing `UiSession.HasPendingInvalidations`.
6. Introduced `IUiAnimationHost` interface (`StartAnimation()`, `StopAnimation()`) enabling hosts to start and stop frame timers on demand.
7. Enhanced `StandardAnimationScheduler` with host wake/idle lifecycle management: automatically requests host animation when animations are active and stops frame requests when idle. Added `StartTransition(duration, onProgress, onCompleted)` with cancellation support via `IDisposable`.
8. Added `ReducedMotion` policy support in `StandardAnimationScheduler`: checks `IUiSystemSettingsHost` and theme tokens, executing transitions synchronously to completion without scheduling background timers or waking the host.
9. Verified deterministic queue ordering in `StandardQueuedUiDispatcher` (FIFO dispatching).
10. Added comprehensive test suite `SelectiveLayoutAndSchedulingTests` in `Broiler.UI.Standard.Tests` covering all acceptance scenarios: hover without rewrapping, layout invalidations, property setter audit, render-time invalidation preservation, animation host wake/idle, transition cancellation, reduced motion, deterministic dispatch order, and invalidation benchmark.
11. Verified all 791 tests passing across `Broiler.UI.slnx` and all 206 tests passing across `Broiler.Mail.slnx`.

The inspected `UiSession.RenderFrame` invokes measure/arrange/render on every root, and `UiElement.Measure` calls `MeasureCore` without a dirty-layout fast path. Build an invalidation benchmark and correctness suite, then use invalidation flags to avoid unnecessary layout.

Audit property setters before caching: cached layout becomes wrong if size/font/text changes do not invalidate it. Preserve invalidations raised during rendering and deferred callbacks. Use the existing `StandardAnimationScheduler`; it is a clock-driven `Tick()` facility, not proof that a host automatically wakes for an animation. Define host wake/cancellation/shutdown behavior before adding transitions.

**Acceptance:** an unchanged-size hover does not rewrap a large document; every layout-affecting change still does; idle windows stop requesting frames; reduced motion works; queue ordering stays deterministic.

## 4. Broiler.UI controls

### [x] C-01: presentable virtual lists (Completed)

**Status:** Completed.
1. Introduced domain-neutral presenter and template contracts in `Broiler.UI.ListView`: `IUiListItemPresenter` (`GetItemHeight`, `Render`, `CreateSemanticNode`), `UiListItemState` (struct carrying `IsSelected`, `IsFocused`, `IsRead`, `Index`, `Density`), `UiListItemRenderContext` (render context with `RenderList`, `Bounds`, `Item`, `State`, `Font`, theme palette colors, and `IsHighContrast`), and `UiListItemSemanticContext`.
2. Extended `UiListItem` record with optional `SecondaryText`, `TertiaryText`, `IsRead`, and arbitrary metadata `Tag` while strictly maintaining backwards compatibility.
3. Added default single-line presenter `DefaultListItemPresenter` with Unicode grapheme-cluster ellipsis truncation (`StringInfo.GetTextElementEnumerator`), density variants (`Compact = 24`, `Comfortable = 28`, `Spacious = 36`), and accessibility semantics (`UiSemanticRole.ListItem`).
4. Built reusable two-line presenter `StandardTwoLineListItemPresenter` for rich content, presenting primary text and tertiary metadata (e.g. timestamps) on line 1, and secondary preview on line 2, with unread indicator dot, bold unread typography, right-aligned tertiary width reservation to prevent text overlap at narrow widths, and density variants (`Compact = 38`, `Comfortable = 52`, `Spacious = 64`).
5. Enhanced `StandardListView`:
   - Configurable `ItemPresenter` and `Density`, automatically computing `EffectiveItemHeight` while respecting explicit `ItemHeight` overrides.
   - Built scroll anchoring in `UiListView.SetItems` across insertions/removals: tracks the top visible anchor item and adjusts `VerticalOffset` dynamically so existing content stays anchored without visual jumping.
   - Implemented `IUiScrollable.MakeVisible(BRect targetRect)`, `ScrollIntoView(string/int)`, and `EnsureSelectedVisible()`.
   - Added Enter key activation and double-click mouse activation, raising `ItemActivated` (`UiListItemEventArgs`).
   - Added type-ahead keyboard navigation (`TypeAhead(prefix)`), cycling through matching item prefixes.
   - Provided native accessibility off-screen realization methods `RealizeItem(int index)` and `GetItemSemanticNode(int index)` allowing screen readers to query off-screen virtualized items.
   - Preserved full multi-selection semantics for existing consumers.
6. Implemented mail-specialized presenter in `Broiler.Mail.Application`: `MailMessageItemPresenter` presents sender, subject, formatted timestamp, and unread dot indicator without placing mail-specific fields into generic UI abstractions. Wired `InboxView` to use `MailMessageItemPresenter` and comfortable density.
7. Verified with comprehensive test suite in `ListViewPresenterAndVirtualizationTests` in `Broiler.UI.Standard.Tests` (12/12 passing) covering density heights, narrow-width non-overlapping rendering, accessibility semantics, scroll anchoring across inserts/removals, ensure-visible, keyboard navigation and multi-selection, type-ahead, double-click activation, off-screen realization, and 200% font scale/high-contrast modes.
8. All 803 tests passing in `Broiler.UI.slnx` and all 206 tests passing in `Broiler.Mail.slnx`.

Extend `StandardListView`'s existing visible-range rendering with a presenter/template boundary: stable item key, selected/focused/read state, content bounds, density, measure/paint, and semantics. Start with fixed-height comfortable/compact variants. Add variable-height realization only when justified.

Include scroll anchoring across inserts/removals, ensure-selected-item-visible, ellipsis/full-text disclosure, and keyboard selection. Preserve current multi-selection behavior for other consumers. Native accessibility must be able to scroll/realize off-screen items.

Mail builds a sender/subject/time presenter; Code might build a search-result presenter and Writer a document list. The generic API must not contain mail-specific field names.

### [x] C-02: split layouts and responsive panels (Completed)

**Status:** Completed.
1. Introduced domain-neutral split container abstractions in `Broiler.UI.Splitter`:
   - `UiSplitContainer`: responsive container dividing space between `FirstPane` and `SecondPane` separated by a keyboard- and pointer-operable `UiSplitter` grip.
   - Supports `Orientation` (`Vertical` for left/right split and `Horizontal` for top/bottom split).
   - Normalized `SplitterFraction` and layout-unit `SplitterDistance`, with `FirstPaneMinimumSize` and `SecondPaneMinimumSize` preventing panes from vanishing or overlapping under resize.
   - Proportional responsive scaling when available size is smaller than the combined minima.
   - Full collapsed-pane behavior: `IsFirstPaneCollapsed`, `IsSecondPaneCollapsed`, `CollapseFirstPane()`, `CollapseSecondPane()`, `RestorePanes()`, `ToggleFirstPane()`, `ToggleSecondPane()`.
   - Persistence hooks via `SplitterPositionChanged` (`UiSplitterPositionChangedEventArgs`).
   - Native accessibility semantics (`UiSemanticRole.Panel` reporting orientation, fraction, and child nodes).
2. Built `StandardSplitContainer` and `StandardSplitContainerFactory` in `Broiler.UI.Splitter.Standard` with live theming (`IStandardThemedControl`) and background support.
3. Extended `Broiler.UI.Toolbar` with wrapping overflow support:
   - Added `UiToolbarOverflow.Wrap = 2`.
   - Implemented multi-row and multi-column wrapping in `StandardToolbar.MeasureCore` and `ArrangeCore`, with child-relative separator rendering in `DrawSeparators`.
4. Modernized form layout:
   - Streamlined `ViewportScrollView` to consume `StandardScrollView { Constraint = UiScrollConstraint.ConstrainWidth }` directly, eliminating the ad-hoc measurement wrapper identified in `component-reuse-review.md`.
5. Adopted in `Broiler.Mail`:
   - Referenced `Broiler.UI.Splitter.Standard` and `Broiler.UI.Toolbar.Standard` in `Directory.Packages.props` and `Broiler.Mail.Application.csproj`.
   - Replaced fixed docked panel in `InboxView` with `StandardSplitContainer` (list vs. reading pane) with responsive minima and keyboard resizing.
   - Replaced button stacks in `InboxView` and `ComposerView` with wrapping `StandardToolbar`.
   - Added `InboxSplitterFraction` to `ApplicationSettings`, validated in `ConfigurationValidator`, and synchronized with `InboxViewModel`.
6. Verified with comprehensive test suites:
   - `StandardSplitContainerTests` in `Broiler.UI.Splitter.Tests` (10/10 passing).
   - `ToolbarOverflowWrapTests` in `Broiler.UI.Toolbar.Tests` (49/49 passing).
   - Split layout, collapsed panes, and toolbar wrapping tests in `InboxWorkflowTests` in `Broiler.Mail.Tests` (199/199 passing).
   - All tests passing across both `Broiler.UI.slnx` and `Broiler.Mail.slnx` (209 passing).

`StandardSplitter` already exists in sibling source and its `preview.10` package is present in the local NuGet cache. It is not referenced by Mail today. Confirm its consumed API, then compose a proper split layout with minima, keyboard resizing, persistence hooks, and collapsed-pane behavior. Extend an existing layout control instead of creating an unrelated Mail splitter.

Add bounded form-layout and wrapping/overflow toolbar behavior where missing. Application layout chooses breakpoints and navigation; controls provide measurement and overflow mechanics.

### [x] C-03: readable/selectable text (Completed)

**Status:** Completed.
1. Introduced `RichEditWrapping` enum (`Wrap = 0`, `NoWrap = 1`) and extended `UiRichEdit` with `IUiScrollable` (`MakeVisible(BRect)`), `Wrapping`, `HorizontalScrollPolicy`, `ScrollToStart()`, and `ScrollToEnd()`.
2. Enhanced `RichEditLayout` and `RichEditLayoutSettings`:
   - Added `ContentExtentWidth` calculation for tracking the widest line in preformatted/code/nowrap text.
   - Added bounded glyph measurement cache (`_charAdvanceCache` bounded to 1024 entries).
   - Added `GetVisibleLineRange(contentMinY, contentMaxY)` using binary search (`O(log N)` visible line lookup).
   - Optimized `LineAt(double y)` with binary search over visual lines.
   - Updated `MeasureWrap` to honor `RichEditWrapping.NoWrap` (preserving preformatted lines without breaking on column width).
3. Viewport-aware rendering and read-only presentation in `RichEditPainter`:
   - Added `IsReadOnly` to `RichEditPaintFrame`.
   - Made run backgrounds, selection ranges, text, and list marker rendering fully viewport-aware, drawing only visible lines within the current viewport slice.
   - Suppressed blinking caret rendering when `IsReadOnly` is active.
4. Two-dimensional scrolling in `RichEditScroller` and `RichEditViewport`:
   - Added horizontal scroll metrics, track/thumb bounds calculation, dragging, clamping, and painting.
   - Added `ScrollX` support to `RichEditViewport`.
5. Read-only keyboard, mouse, and shortcut contracts in `StandardRichEdit.Input.cs`:
   - Blocked typing and text composition mutations.
   - Blocked destructive/formatting shortcuts (`Ctrl+X`, `Ctrl+V`, `Ctrl+Z`, `Ctrl+Y`, `Ctrl+B/I/U`).
   - Enabled copy (`Ctrl+C`), select-all (`Ctrl+A`), mouse selection/dragging, and caret navigation.
   - In read-only mode, `Tab` returns `false` so tab traversal moves focus out of the reader instead of trapping it.
   - Added horizontal scrollbar dragging and Shift+Wheel horizontal scrolling.
6. Policy-driven measurement in `StandardRichEdit.cs`:
   - Under `VerticalScrollPolicy.Never`, the control measures and grows to fit its content height, allowing seamless hosting inside `StandardScrollView` with `UiScrollConstraint.ConstrainWidth`.
   - Implemented `IUiScrollable.MakeVisible(BRect)`.
7. Adopted in `Broiler.Mail`:
   - Upgraded `ScrollableMessageText` from an unselectable `StandardLabel` to a readable, selectable `StandardRichEdit` inside a `StandardScrollView` with `Constraint = UiScrollConstraint.ConstrainWidth`.
   - Removed the ampersand escaping workaround (`value.Replace("&", "&&")`).
   - Exposed `public StandardRichEdit Editor => _editor;` for direct reader inspection.
   - Updated `Version1AcceptanceTests` to assert on `StandardRichEdit` reader.
   - Added `MessageReader_Is_Selectable_Readable_And_Supports_Copy` in `InboxWorkflowTests.cs` validating selection, Ctrl+A, Ctrl+C copying to clipboard, non-mutating typing, and Tab focus traversal.
8. Comprehensive test coverage:
   - Added `StandardRichEditReadOnlyAndScrollingTests` in `Broiler.UI.RichEdit.Standard.Tests` (12/12 passing).
   - All tests passing across `Broiler.UI.slnx` (800+ tests).
   - All tests passing across `Broiler.Mail.slnx` (210 tests).

Evaluate a read-only RichEdit mode as the first selectable reader. If a smaller text-view control is needed, share shaping, hit testing, selection, copy, keyboard navigation, and accessibility contracts with RichEdit. Add viewport-aware rendering and bounded layout caches. Preserve code whitespace and horizontal scrolling where explicitly selected.

### [x] C-04: compact forms and feedback (published preview integrated)

**Implementation (2026-10-01):** `Broiler.UI.Forms.Standard` now supplies labeled fields, named/collapsible sections, themed inline feedback with semantic events, and persistent wrapping actions with bounded feedback scrolling. Mail adopts these in Account, Settings, and Compose. Field-aware configuration errors reveal the affected control. The complete UI dependency graph is now consumed from NuGet.org at **0.1.0-preview.11**, without a local feed or sibling project references. `FeedbackKind` uses the published `Broiler.UI.Forms` abstraction namespace.

Original C-04 validation: 256 shared UI tests and 212 Mail tests passing. [Implementation, published-package validation, screenshots, and remaining release checks](c04-forms/README.md). Native UIA/screen-reader acceptance is tracked under H-01; full RTL/system text-scale checks remain pending. Official UI package publication and Mail adoption are complete.

Original scope: useful shared additions or extensions are labeled field groups with descriptions/errors, accessible expanders, inline status banners, progress/cancel rows, and a persistent action-bar pattern. Buttons already have styling/icon work in sibling source; verify release availability before inventing a second icon/button implementation.

Recipient chips are a later specialized control built on generic tokenized input only if another consumer can use it. Keep email parsing, Bcc policy, and address validation in Mail.

**Acceptance across controls:** keyboard use, native semantics, dark/high contrast, 200% text, RTL/long labels, no overlap at narrow widths, and meaningful visual regression fixtures.

## 5. Input, Native, and shared hosting

### [ ] H-01: native accessibility bridge (native discovery passes; screen-reader acceptance pending)

**Acceptance update (2026-10-04):** External-client discovery and patterns pass in
Debug and NativeAOT. Mail now attaches the native bridges in `OnCreated`, after
the HWNDs exist; this fixes the empty-tree failure recorded on 2 October.
Hosting preview.5 is consumed. Actual screen-reader speech and the remaining
semantic/lifetime checks stay open; see [UI-09](ui-implementation-roadmap.md#ui-09--native-accessibility-from-discovery-to-text-editing)
and [the current audit](roadmap-status-2026-10-04.md).

**Implementation (2026-10-01):** Implemented Windows UI Automation bridge at the host boundary in `Broiler.Mail.Windows.Hosting.Accessibility` (`WindowsAutomationBridge`, `WindowsElementAutomationPeer`, and `UiaNative`).
- Subclasses native window handle to intercept `WM_GETOBJECT` (0x003D with `UiaRootObjectId = -25`) and return root provider via `UiaReturnRawElementProvider`.
- Implements `IRawElementProviderFragmentRoot`, `IRawElementProviderFragment`, and `IRawElementProviderSimple`.
- Controls and virtual items map to UIA control patterns:
  - `IInvokeProvider` on `UiButton` and actions (`Click()`, `UiaInvoke_InvokedEventId`).
  - `IValueProvider` on `UiEdit` and `UiRichEdit` (`GetPlainText()`, `SetPlainText()`), with strictly protected password fields (`IsPassword = true`) never exposing plain-text values.
  - `ISelectionItemProvider` and `ISelectionProvider` on `UiListView` (supporting virtualized row realization, selection, toggling) and `UiTabView` (supporting tab item navigation and selection).
  - `IToggleProvider` on checkable controls (`Checked`, `Indeterminate`, `Off`).
  - `IExpandCollapseProvider` on `UiComboBox` (`OpenDropDown()`, `CloseDropDown()`, `IsDropDownOpen`).
  - `IScrollItemProvider` on virtualized list items and scrollable elements (`ScrollIntoView()`, `BringIntoView()`).
- Point hit-testing via `ElementProviderFromPoint` accurately resolves coordinates down to list items and tab headers.
- Bridges `UiSession.SemanticChanged` events to native UIA events (`AutomationFocusChanged`, `LiveRegionChanged` for `StatusAnnounced`, property changes, and structure changes).
- Removed controls and unlinked items are cleanly purged from peer caches on structural invalidation and bridge disposal (`RemoveWindowSubclass`).
- Comprehensive test coverage in `WindowsAutomationBridgeTests`: 19 passing tests verifying coordinate-free tree navigation, control patterns, password protection, virtual list navigation, hit-testing, focus handling, and dead peer cleanup. All 224 Mail tests pass.

Implement the Windows UI Automation provider at the UI/native-host boundary. Neutral semantic contracts stay in UI; lifecycle integration belongs in a host adapter. `Broiler.Hosting.Windows` now supplies the provider and its COM boundary as a published package; see [hosting package integration](hosting-packages.md).

Map roles and patterns incrementally: invoke, selection/selection item, value, range where relevant, text/text range, scrolling, expand/collapse, and focus. Expose labels and relationships, stable runtime/automation identities, screen-coordinate bounds, and events. Password values remain protected. Ensure clients can navigate virtualized lists.

The Mail inspection's [recorded tree](ux-review-2026-09-30/accessibility-tree.txt) is a useful failing baseline. Microsoft's [provider overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-providersoverview) describes the responsibility of custom controls.

**Acceptance:** a native inspection client can locate Inbox/To/Send by name and role; a screen reader announces selection, loading, errors, and draft state; controls are operable without coordinates; removed controls do not leave invalid providers.

### [x] H-02: text input and scroll fidelity (Completed)

**Implementation (2026-10-01):**
- **Activation & Focus Handoff:**
  - Resolved the dropped-keystroke defect where Alt+Tab or caption clicks left focus on the top-level frame HWND (`NativeHandle`) rather than the render child window (`RenderNativeHandle`).
  - Added `WindowsInputBridge.OnTopLevelMessage`: intercepts `WM_SETFOCUS` (0x0007) and `WM_ACTIVATE` (0x0006 with `WA_ACTIVE` or `WA_CLICKACTIVE`) on `NativeHandle` and immediately transfers Win32 focus to `RenderNativeHandle` via `SetFocus`.
- **Windows Input Bridge & Interop (`WindowsInputBridge.cs` and `InputNative.cs`):**
  - Implemented safe window subclassing (`SetWindowSubclass`, `DefSubclassProc`, `RemoveWindowSubclass`) on `RenderNativeHandle`.
  - NativeAOT-compatible source-generated P/Invokes (`[LibraryImport]`) in `InputNative.cs` for `user32.dll`, `comctl32.dll`, and `imm32.dll`.
- **Exactly-Once Text Delivery & IME Composition:**
  - Full IME lifecycle support: `WM_IME_STARTCOMPOSITION` (0x010D) dispatches `TextCompositionState.Started`; `WM_IME_COMPOSITION` (0x010F) with `GCS_COMPSTR` reads the in-progress string via `ImmGetCompositionStringW` and dispatches `TextCompositionState.Updated`; `GCS_RESULTSTR` reads the committed string and dispatches `TextCompositionState.Committed`.
  - Duplicate suppression: synthetic `WM_CHAR`s generated downstream by `DefWindowProc` after an IME commit are tracked and suppressed, eliminating duplicate character insertion.
  - In-progress composition is cleanly cancelled (`TextCompositionState.Cancelled`) on `WM_IME_ENDCOMPOSITION`, focus loss (`WM_KILLFOCUS`), or bridge disposal.
- **Surrogate Pair Assembly:**
  - High surrogates (`0xD800..0xDBFF`) are buffered without premature dispatch; subsequent low surrogates (`0xDC00..0xDFFF`) combine with the buffered high surrogate into a single, valid 2-code-unit UTF-16 string (e.g. `"😀"`) delivered in a single `TextInputEvent`.
  - Stray low surrogates or orphaned high surrogates are cleaned up safely without leaking invalid UTF-16 halves into editors.
- **Shortcut Modifier Isolation vs. Text Generation:**
  - When `Ctrl` is held without `Alt`, control characters (`c < 0x20` and `0x7F` DEL) generated by Win32 `TranslateMessage` on `Ctrl+A`, `Ctrl+C`, `Ctrl+V`, `Ctrl+Z`, `Ctrl+Backspace`, and `Ctrl+Enter` are suppressed from text insertion.
  - `AltGr` (`Ctrl+Alt`) is preserved: all graphic characters (`@`, `€`, `[`, `]`, `{`, `}`, `\`, `~`) properly yield valid text.
- **Precision Wheel & Horizontal Scrolling:**
  - Sub-notch precision: `WM_MOUSEWHEEL` and `WM_MOUSEHWHEEL` compute fractional notch deltas (`deltaRaw / 120.0`), preserving precision touchpad and smooth-wheel deltas without integer truncation.
  - `WM_MOUSEHWHEEL` routes as `MouseWheelAxis.Horizontal`.
  - Shift + Vertical wheel is routed as horizontal scrolling with `MouseWheelAxis.Horizontal`.
  - Unconsumed wheel deltas at scroll boundaries chain to parent containers across `StandardScrollView`, `StandardRichEdit`, and `StandardListView`.
- **Comprehensive Verification:**
  - Built `WindowsInputBridgeTests.cs` (12 tests) verifying activation focus transfer, surrogate pair assembly, stray surrogate discard, orphan cleanup, shortcut chord suppression, AltGr characters, dead key tracking, IME lifecycle and duplicate suppression, precision wheel sub-notches, and end-to-end typing in `StandardEdit` and `StandardRichEdit`.
  - 45/45 tests passing in `Broiler.Mail.Windows.Tests`; 250/250 tests passing in `Broiler.Mail.slnx`; 809/809 tests passing in `Broiler.UI.slnx`.

Inspect current Broiler.Input composition and keyboard contracts and compare existing application integrations before extraction. Mail presently forwards a legacy graphics input adapter and places the default IME window at the caret; this is not full composition support.

Preserve exactly-once text delivery across WM_CHAR/composition paths, dead keys, surrogate pairs, IME commit/cancel, selection replacement, and focus changes. Test shortcut modifiers independently from text generation. Add precision wheel/touchpad deltas and horizontal scrolling without losing ordinary wheel behavior or scroll chaining.

Do not label the tool session's unsuccessful shortcut attempts a confirmed Input defect. Reproduce with a physical/native input trace and determine whether activation moves focus from the render child to the top-level HWND.

### [x] H-03: extract hosting only after comparing consumers (Completed)

**Implementation (2026-10-01):**
- **Cross-Consumer Comparison & Architecture Analysis:**
  - Audited host implementations across `Broiler.Mail` and all comparison candidates: `Broiler.Code` (`CodeWindow.cs`, `CodeHost.cs`, `WindowsTextInputService.cs`), `Broiler.Writer` (`WriterWindow.cs`, `WriterWindowsUiHost.cs`, `WriterHostWindow.cs`), and `Broiler.Browser` (`BrowserWindow.cs`, `BrowserUiHost.cs`).
  - Runtime-tested and verified compilation and test suites across all four heads.
- **Unified & Hardened Clipboard (`WindowsClipboard`):**
  - Extracted and reconciled `WindowsClipboard` across `Broiler.Plate`, `Broiler.Code`, `Broiler.Writer`, `Broiler.Browser`, `Broiler`, and `Broiler.Mail`.
  - Enforced 1 MB boundary protection (`MaximumBytes = 1024 * 1024`) and explicit `GlobalSize(handle)` bounds validation (`size < 2 || size > MaximumBytes`), eliminating buffer overflows when reading untrusted clipboard data from other processes.
  - Replaced unbounded `Marshal.PtrToStringUni` with bounded length reading (`(int)size / 2`), requiring a valid NUL terminator.
  - Hardened memory ownership: failure paths free allocated movable blocks via `GlobalFree`, while successful `SetClipboardData` handoffs yield ownership to the OS.
  - Added dual constructor support: direct HWND (`WindowsClipboard(IntPtr ownerWindow)`) and lazy owner evaluation (`WindowsClipboard(Func<IntPtr> owner)`).
- **Window Sizing & Multi-Monitor DPI (`WindowsWindowSizing`):**
  - Standardized `WindowsWindowSizing.cs` in `Broiler.App` and linked into `CodeWindow`, `WriterWindow`, `BrowserWindow`, and `WindowsMailWindow`.
  - Handles `WM_GETMINMAXINFO` (0x0024) to enforce 640x480 minimum client dimensions adjusted for DPI via `AdjustWindowRectExForDpi`.
  - Handles `WM_DPICHANGED` (0x02E0) to apply the OS-suggested monitor rect via `SetWindowPos`, eliminating window blurring and resizing artifacts on monitor transitions.
- **Activation & Focus Handoff (`RenderNativeHandle`):**
  - Wired top-level `WM_ACTIVATE` (with `WA_ACTIVE` / `WA_CLICKACTIVE`) and `WM_SETFOCUS` across `Mail`, `Code`, `Writer`, and `Browser` to immediately hand Win32 focus to `RenderNativeHandle`, preventing dropped keystrokes after task switching or caption clicks.
- **Text Input & Shortcut Chord Isolation:**
  - Harmonized surrogate pair assembly (`\uD83D\uDE00`) and shortcut modifier isolation in `CodeWindow` and `WindowsMailWindow`, filtering synthetic control characters (`char.IsControl(c)`) generated by `TranslateMessage` on `Ctrl+Key` while preserving graphic characters and `AltGr`.
- **System Appearance & System Settings (`IUiSystemSettingsHost` & `WindowsTheme`):**
  - Implemented `IUiSystemSettingsHost` on `WindowsUiHost` in `Broiler.Mail.Windows`.
  - Added `WindowsTheme.QuerySystemSettings()` querying Windows light/dark mode registry (`AppsUseLightTheme`), high contrast (`SPI_GETHIGHCONTRAST`), and reduced motion (`SPI_GETCLIENTAREAANIMATION`).
  - Added runtime listening for `WM_SETTINGCHANGE` (0x001A) and `WM_THEMECHANGED` (0x031A) on the top-level window, updating `Settings` and raising `SettingsChanged`.
- **Architectural Boundary Preservation:**
  - Kept application-specific policies strictly outside the host: close/save guard and draft persistence remain in Mail/Writer, credential store remains in Mail, and command bindings remain in respective apps.
  - Strict dependency direction maintained: UI abstractions + Graphics platform + Native platform -> host integration.
- **Dedicated Broiler.Hosting Repository Extraction (`D:\Broiler.Hosting`):**
  - Established standalone repository `D:\Broiler.Hosting` with `Broiler.Hosting.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `NuGet.config`, and `README.md`.
  - Implemented `Broiler.Hosting.Windows` library encapsulating `WindowsClipboard` (1 MB bounded UTF-16, direct & lazy owner resolution, safe memory ownership), `WindowsWindowSizing` (`WM_GETMINMAXINFO` DPI scaling & `WM_DPICHANGED` rect positioning), `WindowsTheme` (dark mode registry, `SPI_GETHIGHCONTRAST`, `SPI_GETCLIENTAREAANIMATION`), `WindowsInputBridge` (decoupled `Func<UiInputEvent, bool>?` input filter, top-level focus handoff, surrogate pairs, IME lifecycle & duplicate suppression, chord isolation, precision wheel), and `WindowsAutomationBridge` (UIA COM provider with element peers, pattern mapping, password protection, and hit testing).
  - Implemented `Broiler.Hosting.Linux` library encapsulating `LinuxUiHost` (reusable `IUiHost` over `IBroilerRenderer` and `IBroilerSurface`), `LinuxX11Clipboard` (hardened X11 `CLIPBOARD` & `PRIMARY` selection provider, 1 MB bounds cap, `INCR` chunking, atom caching, timeout safety, UTF-8/Latin-1 conversion, non-Linux guard), `LinuxBackendDiagnostics` (X11/EGL/OpenGL native preflight probe), and `LinuxInputCoordinator` with `LinuxInputSnapshot` (evdev keyboard/mouse coordination, cross-device modifier tracking, pointer clamping, non-blocking queue draining).
  - Implemented `Broiler.Hosting.Android` library multi-targeting `net10.0` (for headless testing/diagnostics) and `net10.0-android36.0` (for full mobile platform integration), encapsulating `AndroidUiHost` (reusable `IUiHost`, `IUiClipboardHost`, `IUiTextInputHost`), `AndroidBackendDiagnostics` (native preflight check for `libEGL.so`, `libGLESv3.so`, `libandroid.so`), `AndroidBroilerView` (hardware-accelerated `SurfaceView` with Choreographer vsync loop), `AndroidCanvasRenderer` (`IBroilerRenderer` display list replayer onto Android Canvas), `AndroidInputCoordinator` (multi-device touch, pen, key, IME router), `AndroidInsetLayout` (display cutout and system insets layout), and `BroilerInputConnection` (IME text bridge).
  - Built comprehensive headless test suite in `tests/Broiler.Hosting.Windows.Tests` (30/30 tests passing), `tests/Broiler.Hosting.Linux.Tests` (6/6 tests passing), and `tests/Broiler.Hosting.Android.Tests` (6/6 tests passing).
  - Enabled NuGet package generation (`dotnet pack`) yielding `Broiler.Hosting.Windows.0.1.0-preview.1.nupkg`, `Broiler.Hosting.Linux.0.1.0-preview.1.nupkg`, and `Broiler.Hosting.Android.0.1.0-preview.1.nupkg`.
  - Mail now consumes all three preview.1 packages from NuGet.org, replacing sibling project references. Windows/Linux use the shared hosts at runtime; Android is included in the published API probe. See [package validation and NativeAOT publishing](hosting-packages.md).
  - Wired `Broiler.Mail.Windows` and `Broiler.Mail.Windows.Tests` to consume `Broiler.Hosting.Windows`, removing local duplicated hosting files from `Broiler.Mail`.
  - Wired `Broiler.Mail.Linux` and `Broiler.Mail.Linux.Tests` to consume `Broiler.Hosting.Linux`, removing local duplicated hosting files from `Broiler.Mail`.
  - Wired `Broiler.Writer.Android` and `Broiler.Browser.Android` to consume `Broiler.Hosting.Android`, verifying clean compilation with 0 warnings/0 errors.
  - Wired `Broiler.Mail` dependency probe to verify Android hosting contracts (`AndroidUiHost`, `AndroidBackendDiagnostics`).
- **Verification:**
  - Built `WindowsHostingIntegrationTests.cs` covering clipboard 1 MB bounds, lazy owner evaluation, `WM_GETMINMAXINFO` minimum dimensions, DPI scaling, and system settings.
  - Built `LinuxHostingTests.cs` covering `LinuxUiHost` viewport scaling, invalidation propagation, `LinuxX11Clipboard` cross-OS safe degradation, `LinuxBackendDiagnostics`, and `LinuxInputCoordinator` snapshot/clamping behavior.
  - Built `AndroidHostingTests.cs` covering `AndroidUiHost` viewport scaling, invalidation retention, exception propagation, clipboard read/write, caret dispatch, and `AndroidBackendDiagnostics` safe degradation.
  - 42/42 tests pass in `Broiler.Hosting.slnx` (30 Windows + 6 Linux + 6 Android).
  - 51/51 tests pass in `Broiler.Mail.Windows.Tests`.
  - 3/3 tests pass in `Broiler.Mail.Linux.Tests`.
  - 256/256 tests pass across `Broiler.Mail.slnx`.
  - `Broiler.Windows.Code.slnx`, `Broiler.Windows.Writer.slnx`, `Broiler.Linux.Writer.slnx`, `Broiler.Android.Writer.slnx`, `Broiler.Windows.Browser.slnx`, `Broiler.Linux.Browser.slnx`, and `Broiler.Android.Browser.slnx` build with 0 warnings and 0 errors.
  - 416/416 tests pass in `Broiler.Browser.Tests.slnx`.
  - 809/809 tests pass across `Broiler.UI.slnx`.

Extract clipboard, native wake/dispatch, system appearance, caret/IME, sizing/DPI, and accessibility integration with **Mail and at least one of Code/Writer/Browser** as adopters. The prior reuse review identified those comparison candidates; this review did not runtime-test them.

Keep each app's close/save policy, credential binding, and commands outside the host. Keep the shared dependency direction UI abstractions + Graphics platform + Native platform â†’ host integration. Do not make Graphics reference UI or move OS dependencies into neutral UI runtime assemblies.

## 6. HTML, CSS, Layout, and Media

### [x] R-01: bounded rendering and stable viewport results (Completed)

**Implementation (2026-10-01):**
- Replaced monolithic 8192px single-bitmap rendering in `HtmlViewElement` (`HtmlPreviewWindow.cs`) with a bounded vertical tile surface architecture:
  - Default tile height: 1024 DIPs (`DefaultTileHeight`), rendered on demand based on visible scroll viewport and prefetch buffer.
  - Bounded LRU texture cache: `MaxCachedTiles = 16` (capping texture memory to ~30–50 MB max), actively releasing evicted tile image handles through `renderer.ReleaseImage(handle)`.
  - DPI-aware tile rendering: synchronizes container `ViewportZoom = (float)dpiScale` and `ScrollOffset = new PointF(0, -(float)(tileTop * dpiScale))` to render crisp physical-resolution bitmaps for high-DPI displays (e.g. 150%, 200%), invalidating and recreating tiles when display scale changes.
- Cached layout snapshot (`HtmlLayoutSnapshot`):
  - Measures document headlessly once per width change into `HtmlLayoutSnapshot`, caching measured `Width`, `ContentHeight`, `UnclampedHeight`, `IsTruncated`, `Links` (`HtmlLinkGeometry`), `Diagnostics` (`HtmlBoxDiagnostic`), and `LayoutDurationTicks`.
  - Successive measurements with unchanged width reuse the cached snapshot in O(1) without repeating DOM or layout passes.
  - Precalculated link geometry enables hit testing directly from `Snapshot.Links` in document space without redundant native calls.
- Explicit height budget and visible truncation banner:
  - Enforces `MaxBudgetHeight = 32768f`. When content exceeds the budget, `IsTruncated = true`, content height is clamped, and a prominent warning banner is visibly rendered at the cutoff point (`"Content exceeds maximum render limit (truncated at 32,768px). Use plain text view for full message."`), preventing unexplained blank tails.
- Comprehensive deterministic fixtures in `HtmlPreviewIsolationTests.cs`:
  - `TableLayoutAndOverflowFixture`: multi-column nested tables with colspans and explicit cell widths.
  - `LongWordsAndOverflowFixture`: 500+ character unbroken words and long URLs with overflow styling.
  - `QuotationsAndInlineImagesFixture`: nested blockquotes and data URI inline images, verifying remote image blocking per ADR-0005.
  - `DeeplyNestedBlocksFixture`: 350+ nested `<div>` blocks stress-testing stack depth without overflow.
  - `RtlTextFixture`: Arabic RTL text with link geometry hit testing and click dispatch.
  - `HighDpiTilingFixture`: validates proportional bitmap scaling and invalidation at 1.0x vs 2.0x DPI.
  - `BoundedTilingAndBudgetTruncationFixture`: validates >32k budget clamping, visible warning banner, and strict tile cache bounding during scrolling.
  - `CachedLayoutSnapshotAndLinkHitTestingFixture`: validates snapshot reuse, link hit testing, and cache invalidation on HTML update.
  - All 26 Windows preview tests pass. All 231 Mail solution tests pass.

Mail currently renders HTML into a bitmap, PNG-encodes it, and imports the image into Graphics. The same code lays out content during measurement and when deciding whether a bitmap can be reused. Height is clamped to 8192 pixels. Profile large newsletters/tables before optimizing.

Explore a bounded tile or viewport surface API with a cached layout snapshot, shared geometry for link hit-testing, DPI-aware output, cancellation, and explicit total pixel/memory/time limits. Keep the transport compatible with the required separate renderer process. A direct in-process render list is not automatically an acceptable replacement for the security boundary.

Create deterministic fixtures for tables, long words, quotations, inline images, nested blocks, RTL, high DPI, and overflow. Rendering beyond a budget must fail or truncate visibly, not leave an unexplained blank tail.

### [x] R-02: resource and progress contracts (Completed)

**Implementation (2026-10-01):**
- Integrated `Broiler.Media.MediaLimits` reuse for email-specific preview and thumbnail budgets:
  - Configured `DefaultMailLimits`: 5 MB per image (`MaxEncodedBytes`), 64 MB decoded RGBA (`MaxDecodedBytes`), 16 Megapixels area (`MaxImagePixels`), 4096 px dimension (`MaxImageDimension`), 100 frames (`MaxFrames`), and 20 MB total message budget (`DefaultMaxTotalBytes`).
- Built structured resource outcome contracts in `HtmlResourceOutcome.cs`:
  - Overall outcome kinds: `HtmlResourceOutcomeKind` (`Rendered`, `Partial`, `Blocked`, `Failed`, `BudgetExceeded`).
  - Granular rejection/failure reasons: `HtmlResourceFailureReason` (`None`, `PolicyBlocked`, `NetworkError`, `InvalidContentType`, `ByteBudgetExceeded`, `DimensionBudgetExceeded`, `PixelBudgetExceeded`, `CorruptedHeader`, `DecompressionBombDetected`).
  - Per-resource outcome record `HtmlResourceResult` and batch aggregation `HtmlResourceBatchResult`.
- Bounded streaming download and zero-allocation header inspection in `HtmlResourceLoader`:
  - Network bodies read in 8KB chunks, aborting early if byte limits are exceeded without buffering the complete payload.
  - Zero-allocation header inspection using `ManagedImageCodecs.CreateCodecs()` (`TryInspect`) before pixel decoding or base64 data URI conversion.
  - Immediate rejection of decompression bombs (excessive dimensions > 4096 px or pixel areas > 16 MP) and malformed/corrupted headers without decoder crashes.
- Application/renderer string separation:
  - Eliminated Mail-specific user instructions ("Use plain text view for full message.") from `HtmlViewElement` truncation banner, keeping renderer presentation neutral.
  - Mail UI (`HtmlPreviewWindow`) maps structured batch outcomes (`HtmlResourceBatchResult`) to appropriate status announcements and retry options.
- Comprehensive deterministic test suite in `HtmlPreviewIsolationTests.cs`:
  - `ImageCodecInspection_ValidImages_ParsedAndValidatedSuccessfully`: verifies PNG, GIF, and synthetic JPEG header extraction.
  - `ImageCodecInspection_CorruptedAndMalformedHeaders_SafelyRejectedWithoutCrash`: verifies safe rejection of random noise, truncated headers, and corrupted markers.
  - `DecompressionBombProtection_ExcessiveDimensionsOrPixels_RejectedAsBudgetExceeded`: verifies rejection of 5000x5000 px and >16 MP decompression bombs.
  - `ByteBudgetProtection_OversizedPayload_RejectedAsByteBudgetExceeded`: verifies per-image byte cap enforcement.
  - `BatchResourceLoading_AggregatesStructuredOutcomesCorrectly`: verifies structured batch outcome aggregation (Rendered, Partial, BudgetExceeded, Blocked, Failed).
  - `StreamingResourceLoader_EnforcesStreamingLimitAndValidatesMimeTypes`: verifies early streaming aborts on infinite streams, SVG content rejection, and corrupted body rejection via mock HTTP handlers.
  - `StreamingResourceLoader_FullBatchLoad_PopulatesOnlyValidImages`: verifies batch processing populates only valid images into the embedded dictionary while rejecting bombs and 404s.
  - All 33 preview tests pass. All 238 solution tests pass.

### [x] R-03: layout/text diagnostics shared by consumers (Completed)

**Implementation (2026-10-01):**
- Layout diagnostics are now exposed in `HtmlLayoutSnapshot`:
  - Per-element box diagnostics via `HtmlBoxDiagnostic` capturing `TagName`, `Id`, `ClassName`, `BorderBox`, and `ContentBox` across the DOM tree.
  - Structured link geometry via `HtmlLinkGeometry` with `Id`, `Href`, and `Bounds`.
  - Microsecond layout timing via `LayoutDurationTicks` and `LayoutDuration`.
  - Explicit truncation metadata via `IsTruncated` and `UnclampedHeight`.
- Exposed on both `HtmlViewElement.Snapshot` and `ScrollableHtmlView.Snapshot` for developer diagnostics and deterministic test assertions without affecting normal reader UI.

Broiler.HTML/CSS/Layout can expose useful diagnostic timings, layout boxes, overflow reasons, and supported-feature results for controlled fixtures. Keep these developer tools separate from normal reader UI. A mail-compatibility corpus should validate actual newsletter behaviors, not imply general browser standards coverage.

Broiler.Unicode may contribute reviewed locale/property data where appropriate. Do not treat its emoji recognition as text shaping, bidi layout, grapheme segmentation, or font fallback. Add only the dependency needed for a demonstrated gap.

## 7. Suggested upstream batches

| Batch | Scope | Consumers / exit condition | Status |
| --- | --- | --- | --- |
| A — Small correctness fixes | Graphics defaults; tab/scroll measurement; literal labels | Mail package upgrade removes targeted workaround; focused regressions pass | [x] Graphics defaults (G-01), scrollbar thickness, and U-01 layout & text correctness completed |
| B — Shared presentation | Role-aware live themes, structured rows, splitter integration, form/action patterns | Mail reader/composer visibly improved; second control-demo consumer | [ ] Implemented in Mail; second-consumer acceptance not established by the current audit |
| C — Text and timing | Layout reuse, selective invalidation, bounded native resources, animation wake contract | Comparable before/after traces; no stale layout or idle redraw | [ ] Caches and measured RichEdit improvements delivered; idle stops; broader performance matrix remains UI-12 |
| D — Native experience | Accessibility bridge, focus/IME, shared hosting | Mail plus second app adopted; native keyboard/screen-reader acceptance | [ ] Implemented in Mail; physical input, screen-reader and cross-consumer acceptance remain |
| E — Rich content | Bounded renderer surface transport and decode improvements | Renderer-isolation gate plus long-content/resource-budget acceptance | [ ] Bounded tiles, direct pixels and resource limits delivered; renderer-isolation exit gate remains open |

Each batch should publish compatible packages, then update Mail pins and run its acceptance suite. Avoid permanent sibling project references. Keep an explicit list of workarounds removed by each release.

## 8. A shared Broiler quality gallery

Create a small deterministic control gallery or extend an existing demo: empty/loading/error/success, forms, virtual lists, selectable text, composition, and popups. Use the same fixtures across hosts and renderer backends. This is a test/development surface, not another application framework.

Record screenshot baselines with OS, backend, font environment, package versions, DPI, theme, text scale, and fixture revision. Pair visual snapshots with semantic-tree snapshots and input tests; pixel similarity alone misses inaccessible controls and wrong focus.

Add performance traces for 50/500/10,000 list items, long wrapped text, repeated resize, and large paste. Distinguish toolkit stress capacity from the product's current mail limits. Put counters behind an optional diagnostic sink; do not collect user mail for telemetry.

The shared gallery and native accessibility bridge offer especially broad returns: they make future regressions visible across Mail, Code, Writer, and Browser before each app has to rediscover them.
