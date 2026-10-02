# UI acceptance, 2 October 2026

The first UI-13 acceptance pass: the published NativeAOT app, every gallery fixture, three window
sizes, and both themes, checked automatically for accessibility structure and keyboard focus, and by
eye from screenshots. It records what passed, what was fixed because of it, and which cases this
machine could not check.

## How to reproduce

```powershell
scripts/Accept-UI.ps1
```

The script publishes the app as NativeAOT (the shipped configuration), reads the fixture list from
`--help`, and runs each fixture at 640×480, 1100×720, and 1920×1080 DIPs in the light and dark
themes (96 runs). Each run:

- saves a screenshot (`PrintWindow`) for visual review;
- reads the UI Automation tree from the window's render child and reports interactive controls without
  a name, controls cut off by the window edge (outside scroll views), and controls that overlap
  (using the part not hidden by a scroll view);
- presses Tab up to 24 times and reports a step with no focus, a stuck focus, an unnamed focus, or a
  focus that is not on screen, recording the cycle;
- closes the window and checks the exit code and that stderr is empty.

Input is posted to the window, so a run does not take focus from other applications, although each
window shows briefly. Output: `artifacts/acceptance/<timestamp>/` with `summary.md`, `results.json`,
and the screenshots. `-Scenarios`, `-Sizes`, `-Themes`, and `-Executable` narrow or redirect a run.

## Environment of this record

| | |
| --- | --- |
| Revision | `3f5ae83` plus the uncommitted UI-08, UI-11, and UI-13 changes of branch `claude/ui-08-announcements` |
| Packages | Broiler.UI 0.1.0-preview.14, Graphics preview.7, Native preview.6, Input preview.5, Hosting preview.3 |
| Build | `dotnet publish -r win-x64 -p:PublishAot=true -p:PublishTrimmed=true` (no warnings), SDK 10.0.401 |
| Machine | Windows 11 Enterprise 10.0.26200, x64 (AMD Zen 3), one monitor at 150 % |
| Appearance | Light and dark app themes; system high contrast off |

## Result

**96 of 96 runs have no automated findings** (after the fixes below; the first pass had 48 runs with
findings). Every run started, settled, exposed a named UI Automation tree, cycled Tab through named,
visible controls back to its starting point (3 to 15 stops depending on fixture and size), and closed
with exit code 0 and an empty stderr. `draft-conflict` refuses an ordinary close because its draft
cannot be saved, which is the intended protection.

Screenshots were reviewed for clipping, overlap, and hierarchy at all three sizes in both themes;
apart from the two Broiler.UI defects below, none showed clipped or overlapping content.

## Matrix

| Dimension | Required cases | Status | Evidence or blocker |
| --- | --- | --- | --- |
| Layout | 640×480, normal, wide, repeated breakpoint transitions | Pass, with one defect fixed upstream | All fixtures at 640×480, 1100×720, 1920×1080. Transitions: `ResponsiveInboxTests` and the UI-12 `resize` workload (30 size changes, no errors). The compact inbox showed an empty band above the list (defect 2). |
| Display | 100/150/200 % DPI; move between monitors; text scale | 150 % checked; others pending | One monitor at 150 %. Tile painting at 100, 150, and 200 % is covered by `HtmlPreviewKeyboardTests`, after UI-11 found it wrong above 100 %. Pending: running at 100 % and 200 %, moving between monitors with different scales, and system text scale; these need display settings or hardware this pass did not change. |
| Appearance | Light, dark, System, high contrast, reduced motion | Light and dark pass; others pending | Light and dark in all 96 runs. System mode follows the OS setting (UI-07 tests). Pending: actual high contrast and reduced motion (system settings). |
| Input | Mouse, precision wheel, keyboard only, AltGr/dead keys, IME, Unicode | Keyboard pass; others pending | Keyboard-only Tab walks in all runs; synthetic wheel in the UI-12 `scroll` workload; Unicode content in `long-message` and `large-draft`. Pending: physical mouse and precision touchpad, AltGr/dead keys, and IME composition (H checks). |
| Content | Empty and 500-row inbox, long subject/address, large body/draft, HTML-only | Pass | `empty`, `large-inbox`, `long-message`, `large-draft`, `html-only`, and `long-html` (past the preview render budget). |
| State | Busy/cancel/retry, failed save, conflicting draft, unknown send, failed Sent copy | Pass | `receive-error`, `body-error`, `save-error`, `invalid-setup`, `test-canceled`, `draft-conflict`, `send-rejected`, `send-unknown`, `sent-copy-failed`, with UI-08's `FeedbackPolicyTests` and `InboxStateTests`. |
| Accessibility | External tree and patterns; screen reader reading, editing, announcements | Structure pass; speech pending | Automated UIA checks in every run; the HTML preview's tree and Tab cycle (UI-11); notification events checked against a local Hosting build (UI-08). Pending: a screen reader's speech (H-01) and the Hosting release that carries announcement text. |
| Deployment | Published Windows x64 and ARM64 | x64 pass; ARM64 pending | x64 NativeAOT publish exercised here. CI builds and packages win-arm64, but no ARM64 machine ran this pass. |
| Direction | Long translated labels, mixed RTL/LTR content | Content pass; layout pending | Arabic and Hebrew lines render in `long-message`. Right-to-left layout and translated labels wait for localization (UI-14). |

## Defects found by this pass

| # | Defect | Fix | Status |
| --- | --- | --- | --- |
| 1 | On Compose, Account, and Settings, Tab after the last button landed on the form's feedback area, which UI Automation did not expose (nothing to announce) and which draws no focus ring, even when there was nothing to scroll. | Mail: a read-only scroll view is a tab stop only while it scrolls (`MailKeyboardNavigation`), and the feedback area is named "Status and errors" (`ConfigurationForm.NameFeedback`). | Fixed; `FeedbackPolicyTests`. |
| 2 | In the compact inbox, an empty band (the height of the empty-inbox notice) stayed above the list after mail arrived, until a resize. A measure invalidation stopped at an element already left invalid under a valid parent, so the layout was never redone. | Broiler.UI branch `claude/measure-invalidation`: invalidation always walks to the root. | Verified with a local package; waits for the next Broiler.UI release. |
| 3 | At small sizes a field's validation error stayed hidden under the action bar: `FormSurface.Reveal` scrolled to the control, not to the error below it. | Same Broiler.UI branch: Reveal brings the whole field into view. | Verified with a local package; waits for the next Broiler.UI release. |
| 4 | `--help` misaligned descriptions after the longest fixture name. | Column width follows the longest name. | Fixed. |

Also recorded for later, not blocking: UI Automation bounding rectangles are not clipped to their
scroll viewport (Broiler.Hosting), so a highlight can extend under an action bar; a focused scroll
view draws no focus ring (Broiler.UI), which matters only while long feedback scrolls.

## Next pass

Repeat after the Broiler.UI and Broiler.Hosting releases are consumed, and add the pending rows that
need other settings or hardware: 100 % and 200 % scale, a second monitor, high contrast, text scale,
an IME, a screen reader, and an ARM64 machine.
