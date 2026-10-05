# Adopting Broiler.UI preview.18 and Broiler.Hosting preview.7 — 5 October 2026

This records the branch `claude/ui-09-upstream-adoption`: what it changes in Mail to consume the next
Broiler.UI and Broiler.Hosting releases, what those releases must contain, how to merge it, and the
evidence gathered with locally packed packages. Status and the rest of the round are in the
[5 October status](roadmap-status-2026-10-05.md); the native runs are in the
[5 October acceptance record](ui-acceptance-2026-10-05.md).

## Where it stands

- **Branch:** `claude/ui-09-upstream-adoption`, 38 commits on top of `claude/ui-13-acceptance` (the
  published stack, code tip `12ceded` plus documentation). Local and unpushed.
- **Pins (committed):** `BroilerUiVersion` 0.1.0-preview.18, `BroilerHostingVersion` 0.1.0-preview.7,
  `BroilerNativeVersion` 0.1.0-preview.7. Native preview.7 is on NuGet; **UI preview.18 and Hosting
  preview.7 are not**, so the branch does not restore from NuGet.org yet.
- **Verified with:** Broiler.UI `0.1.0-preview.18-local.7` (packed from `claude/roadmap-integration` at
  `fd7657f`) and Broiler.Hosting `0.1.0-preview.7-local.5` (packed from `claude/roadmap-integration` at
  `c388a66` against UI local.7), from the local feed `D:\local-packages\roadmap-2026-10-04`, using
  `-p:BroilerUiVersion=0.1.0-preview.18-local.7 -p:BroilerHostingVersion=0.1.0-preview.7-local.5
  -p:BroilerNativeVersion=0.1.0-preview.7 -p:RestoreAdditionalProjectSources=<feed>`.
- **Tests:** 934 passed (640 shared, 291 Windows, 3 Linux) twice at `e870135`, and again after the
  rebase onto the documented acceptance tip; each of the 38 commits also passed on its own.
- **Native runs:** at `e870135` on a NativeAOT publish with the local packs (`final3`, below).

## What the branch changes in Mail

| Change | Upstream it relies on | Mail files | Evidence |
| --- | --- | --- | --- |
| Consume the new pins; compile fixes for Hosting's native declarations moving to Broiler.Native (`InputNative` → `WindowNative`/`ImmNative` in tests) | Hosting preview.6/7, Native preview.7 | `Directory.Packages.props`, Windows tests | Full suite |
| Read-only scroll areas ('Inbox notice', 'Message header', 'Status and errors') become the toolkit's keyboard stops with `FocusWhenScrollable` and an accessible name, replacing Mail's own stop rule; a stop that stops scrolling hands focus on | ADR 0028, 0032, 0034 | `InboxView`, `ConfigurationForm`, `MailKeyboardNavigation` | Focus tests incl. the Account status hand-off; Tab walks in Accept-UI |
| The reader's editors ('Message text', 'Sender and recipients') draw a focus ring while focused | ADR 0028 | `ScrollableMessageText`, `InboxView` | Tests; screenshots |
| Message rows keep the selection's text colors (`context.WithItem`) | ADR 0029 | `MailMessageItemPresenter` | Row tests; the selected unread dot uses the accent text in Dark |
| Combo boxes size themselves from their font; Mail's sizing workaround removed | ADR 0029, 0033 | `AppearanceController` | 200 % text tests and screenshots |
| `TabContent` removed: the tab view keeps hidden tabs' arrangement | ADR 0030 | `MailShellView`, `TabContent.cs` deleted | `ShellLayoutTests` |
| In Windows high contrast, the theme comes from the system's own colors through an injected resolver; demo `--contrast` aquatic, desert, dusk or night-sky builds the Windows 11 contrast themes; Accept-UI `-Contrast` | Hosting `WindowsTheme.CreateHighContrastTheme`, `WindowsSystemColors`; UI selection/state/scrollbar roles, `AccentText` | `AppearancePolicy`, `AppearanceController`, Windows composition root, `DemoOptions`, `scripts/Accept-UI.ps1` | `AppearanceTests`; five contrast-palette runs |
| The IME is turned off whenever the focused element draws no composition (not only password fields), since Hosting no longer shows the default composition window | Hosting `DrawsCompositionInline` | `WindowsTextInput` | IME tests with posted messages |
| Default buttons' focus ring: Mail's `DefaultButtonFocus` workaround retired | ADR 0032 | `AppearanceController` | Per-state ring tests (rest, hover, pressed) in the Windows palettes |
| Disclosure toggles read in sentence case ('Show keyboard shortcuts'); their content panes are named apart from the section | ADR 0031 | `ComposerView`, `SettingsView`, `AccountProfileView` | Tests; Probe-Uia `disclosure` |
| Tests that encoded preview.5 behavior: runtime ids (now per bridge), IME commit copies (none with inline composition), `WM_SYSCHAR` | Hosting preview.7 | Windows tests | Full suite |
| Every window with a UIA bridge uses the queued dispatcher (structure-change coalescing needs it) | Hosting preview.7 | — (already true) | Guard test |
| `scripts/Probe-Uia.ps1` (disclosure, field errors, row names, row validity, runtime ids) and `scripts/Record-Uia.ps1` (focus, selection, notification and property events through one UIA client); `-Packages` labels in the acceptance scripts | Hosting preview.7 | `scripts/` | `final3` probe 6/6 and transcript |

## What the releases must contain

- **Broiler.UI 0.1.0-preview.18:** cut at or after `fd7657f` (`claude/roadmap-integration`, which already
  contains the topic branches for ADRs 0028–0034). Mail needs ADR 0031 and ADR 0034 to compile, and
  ADRs 0028–0030, 0032 and 0034 for its tests. The ADRs are still marked Proposed.
- **Broiler.Hosting 0.1.0-preview.7:** cut at or after `c388a66`, rebuilt and tested against the
  published UI preview.18 (it was packed against UI local.7). It needs Broiler.Native preview.7.
- Pack Hosting against the same UI release Mail pins, so that transitive packages such as
  `Broiler.UI.TreeView` resolve to one version (local.3 had mixed them; local.4 and later did not).

## Merging

1. Publish Broiler.UI preview.18, then Broiler.Hosting preview.7.
2. Rebase this branch onto the then-current `claude/ui-13-acceptance` (or main, once that is merged).
3. Build without the `-p` overrides, so the published packages restore; run the full suite twice.
4. Run Accept-UI (default; `-Contrast dusk`; `-Contrast aquatic`), Accept-Refresh and Probe-Uia on a
   NativeAOT publish; refresh the screenshot baselines that ADR 0031 and ADR 0034 change (tab ring and
   bar, page clip, form ring room, banner spacing, scrollbars, unread dot).
5. Update the README (contrast options, `Probe-Uia.ps1`, `Record-Uia.ps1`, `-Contrast`, `-Packages`,
   package versions), the UI roadmap and the status; then merge.

## Native evidence with the local packs (`final3`, `e870135`)

| Run | Result |
| --- | --- |
| Accept-UI, 22 fixtures × 3 sizes × 2 themes | 132/132 without automated findings |
| Accept-UI `-OpenReader`, inbox fixtures at 640x480 | 20/20 |
| Accept-UI `-TextScale 200`, 640x480 and 1100x720 | 44/44 |
| Accept-UI `-Contrast` aquatic, desert, dusk, night-sky, high (5 fixtures × 2 sizes) | 10/10 each |
| Accept-Refresh (kept, vanish, outside, renumber; Reply round trips) | 4/4, including focus returning after a UI Automation tab selection |
| Probe-Uia | 6/6: disclosure, draft-error, account-error, row-names, row-validity, runtime-ids (47 of 50 rows kept their ids across a refresh) |
| Record-Uia | Transcript of focus, selection and validity events (for example the refused To field: `IsDataValidForForm=False`, description 'Error: Enter valid email addresses separated by commas.') |

The screenshot reviews of the adoption runs confirmed the upstream fixes: the tab focus ring around the
selected header only, page content clipped inside its frame, the selected-tab bar, readable accent text
(Dark selected tab label 7.8:1), sentence-case toggles, palette-colored scrollbars, the unread dot on a
selected row, field rings whole inside forms and clear of the scrollbar, and banners spaced from the
action strip ([visual reviews](ui-acceptance-2026-10-05.md#visual-reviews)).

## Known limits and follow-ups of this branch

- **UIA2 clients:** UIAutomationCore calls a provider's `SetFocus` before `Select`, so a UIA2
  `SelectionItemPattern.Select` first moves focus to the previously selected row. COM clients are
  clean. Making `SetFocus` a no-op on items that are not keyboard-focusable would avoid it but reverses
  documented Hosting behavior; it is a decision for the user.
- **Contrast palettes:** the tab focus ring on the selected header sits flush against the window's
  client frame, so in some palettes ring and frame read as one line (Broiler.UI follow-up, cosmetic).
- **Performance:** frame builds on these packages are 0.3–0.4 ms slower in scroll, theme, select and
  splitter than on preview.17, and allocate 5–7 KB more per frame; not profiled
  ([performance record](ui-performance-2026-10-05.md)).
- **Not verified:** a real screen reader, a real IME, a real Windows contrast theme, other DPI scales,
  ARM64 ([manual checklist](ui-manual-acceptance-checklist.md)).
