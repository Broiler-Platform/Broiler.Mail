# M2: three reader viewport items

Updated 2026-11-03. Three bounded parts of the inline reader's viewport and
keyboard work are complete:

1. **Platform-neutral viewport contract.** `IReaderViewport` exposes a UI-thread, message-local `ReadingPosition` and `RestoreReadingPosition`. Position is a fraction of the vertical scroll range, not a text offset. Finite values clamp to 0–1; NaN/infinity are refused. The selectable text reader and Windows inline HTML adapter implement the contract. A requested position survives hidden/unmeasured layout and applies at the next nonempty layout; a short document has position zero. Explicit new-document/reset behavior clears pending position and zoom anchors. Text selection and contents are not changed by scrolling. This extracts viewport responsibility without replacing the legacy optional window host or claiming full host-lifecycle refactoring.
2. **Approximate mode/failure handoff.** HTML → Plain text and Plain text → HTML transfer the current relative vertical position for the same body. Because HTML and text previews can have different content/lengths, this is approximate coverage, not exact paragraph matching. Managed render failure captures the position before failed layout can collapse its scroll range, then restores it in the text fallback. Existing surface/body guards reject late failures and old toggle callbacks. New message/account selection still disposes the old surface and starts at the top. Positions are not persisted across application restart, new selection or profiles. Existing zoom, theme, remote-image consent/cancellation and no-script policies remain in force.
3. **Keyboard-accessible navigation.** HTML-capable messages and their selectable text alternative show compact Top and End buttons with accessible names Top of message and End of message. Standard Tab/Enter activation restores the active viewport's start/end without loading mail, opening links, changing selection or requesting images. Unsupported optional adapters disable the commands in HTML mode; the text alternative supports them. The commands are a bounded reader-header row so the body still fills its pane, and HTML reader shortcuts recognize focus in this row. Plain-text-only messages retain their existing editor navigation and layout.

## Validation and remaining scope

Ten added shared cases cover clamping, nonfinite refusal, short text, hidden/zoom
restoration, selection preservation, mode handoff, keyboard End, Top, managed-failure
handoff and new-message/stale-failure isolation. Seven added Windows cases exercise
the actual HTML adapter's fractions, deferred restores through zoom/theme/reflow,
new-document reset and nonfinite refusal. The existing responsive/header, zoom,
keyboard, palette and remote-image cases remain passing.

Published HTML raster smoke restores a long actual HTML document, transfers its
position to the text alternative, navigates to the end, and verifies a new document
starts at the top, alongside its actual raster/no-script/palette checks. Both Windows
x64 application variants run this smoke. See [validation evidence](evidence/m2-three-viewport-items-win-x64/README.md).

Direct HTML selection/copy, broader screen-reader/native high-contrast/resize/DPI
acceptance, exact semantic anchors between different representations and complete
host lifecycle unification remain open. Renderer process containment, native crash/
hang recovery, memory/timeout termination and other-platform certification remain
open. Managed-failure fallback is not a renderer sandbox or native crash guarantee.
No JavaScript or package dependency is introduced; M0–M2 are not declared complete.
