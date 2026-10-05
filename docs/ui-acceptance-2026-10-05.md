# UI acceptance, 5 October 2026

The second UI-13 acceptance record, taken at the end of the UI round of 4–5 October 2026. It covers
two local branches, neither pushed:

- **Published stack:** `claude/ui-13-acceptance`, built on the packages published on nuget.org
  (Broiler.UI 0.1.0-preview.17, Broiler.Hosting 0.1.0-preview.5). This is what Mail can merge
  without a new upstream release. The full final runs (`final3`) used `4c36950`. One code commit
  followed, `12ceded`, which points the composer's footer to a refused draft's details; it was
  checked with the full suite and a native recheck of the six composer fixtures (`final4`, below).
  `12ceded` is the branch's code tip.
- **Adoption stack:** `claude/ui-09-upstream-adoption` at `e870135`, 38 commits above `4c36950`, built
  against local packs of the unreleased Broiler.UI 0.1.0-preview.18 (`0.1.0-preview.18-local.7`, from
  Broiler.UI `fd7657f`) and Broiler.Hosting 0.1.0-preview.7 (`0.1.0-preview.7-local.5`, from
  `c388a66`). Its results hold for those packs. They have to be repeated once both packages are
  published and the branch is rebuilt against them. The branch will be rebased onto `12ceded`, so
  that rerun also covers the composer fix on the adoption stack.

It records the automated runs, the visual reviews and what came of them, what this machine could not
check, and what remains for a person or other hardware. The earlier record is
[ui-acceptance-2026-10-02.md](ui-acceptance-2026-10-02.md); the performance runs of the same round
are in [ui-performance-2026-10-05.md](ui-performance-2026-10-05.md).

## How to reproduce

On either branch:

```powershell
scripts/Accept-UI.ps1                                              # 22 fixtures, 3 sizes, 2 themes
scripts/Accept-UI.ps1 -OpenReader -Sizes 640x480 -Scenarios inbox,long-message,large-inbox,receive-error,body-error,html-only,long-html,receive-canceled,load-error,new-mail
scripts/Accept-UI.ps1 -TextScale 200 -Sizes 640x480,1100x720 -Themes light
scripts/Accept-UI.ps1 -HighContrast -Sizes 640x480,1100x720        # published stack
scripts/Accept-Refresh.ps1
```

The composer recheck at `12ceded` (published stack):

```powershell
scripts/Accept-UI.ps1 -Scenarios draft-invalid,send-rejected,draft-conflict,send-unknown,sent-copy-failed,large-draft -Sizes 640x480,1100x720
```

On the adoption branch only:

```powershell
scripts/Accept-UI.ps1 -Contrast aquatic -Scenarios inbox,large-draft,invalid-setup,save-error,draft-invalid -Sizes 640x480,1100x720 -Themes light
scripts/Probe-Uia.ps1
scripts/Record-Uia.ps1
```

The final runs gave every script `-Executable` with one NativeAOT build per stack. The adoption build
was published with the package versions overridden (`-p:BroilerUiVersion`, `-p:BroilerHostingVersion`,
`-p:BroilerNativeVersion`) and the local feed as an extra restore source. Its `project.assets.json`
resolved every Broiler.UI package to local.7, Hosting to local.5, and Native to preview.7, and its
runs named those versions with `-Packages`. The branch's committed pins already name preview.18,
preview.7, and Native preview.7, so it restores without overrides only once those packages are
published. Output goes to `artifacts/` in each
worktree, which is not committed.

## Method changes since 2 October

- **Accept-UI.**
  - It finds the app window by its render child and closes, or kills, every demo process it
    started (`dade54e`).
  - `-OpenReader` (`8f664f5`) opens the selected message with Read message before the Tab walk,
    runs the same UI Automation checks on the reader (findings marked `READER_`), reports a button
    cut at the bottom edge of the message header (`HEADER_CUT`), saves `<run>-reader.png`, and goes
    Back to inbox, so the Tab walk still covers the list. The reader runs use it on the ten
    fixtures with a message to open.
  - Adoption branch: `-Contrast high|aquatic|desert|dusk|night-sky` renders as if Windows high
    contrast were on, without changing the system setting. `high` uses the theme's own
    high-contrast preset (the same as `-HighContrast`). The other four use the colors of that
    Windows 11 contrast theme, built into a palette the way Broiler.Hosting builds one from the
    system's contrast colors. `-Packages` names the package versions in the summary when the
    build used overrides.
- **Accept-Refresh** (new, UI-02) runs the `new-mail` fixture once per demo server change
  (`--server-change`). Each run scrolls the list and the reader, selects a row and reader text,
  and presses F5:
  - `kept`: new rows arrive above the open message, which stays put;
  - `vanish`: the open message is deleted on the server;
  - `outside`: the open message falls below the newest page;
  - `renumber`: the server renumbers the inbox.

  The `kept` run then checks that focus returns to Reply after composing, once with a posted click
  on the Inbox tab and once by selecting the tab through UI Automation.
- **Probe-Uia and Record-Uia** (new, adoption branch; they need the Hosting preview.7 mapping).
  - Probe-Uia reads, from outside the process as a screen reader would, the semantics added
    upstream: the Cc and Bcc disclosure, refused-field errors, row names and validity, and runtime
    IDs across a refresh.
  - Record-Uia prints the focus, selection, notification, and property-change events of a short
    inbox walk and a short composer walk. Its transcript is input for the screen-reader pass
    (H-01), not a substitute for it.
- **Visual review** by agents, at pixel level.
  - Each reviewer took a share of the screenshots at full resolution. It sampled pixels and crops,
    computed contrast with the WCAG formula, traced candidate defects to the source, and rechecked
    the previous review's defects.
  - A second agent checked every candidate against the same pixels and the code and confirmed or
    rejected it.
  - The reviewers were told which behaviors are intended (the compact single-pane inbox, rows that
    shorten or drop their date, the white HTML canvas in the dark theme, forms that scroll). Later
    rounds were also told the items already known or decided.

## Environment of this record

| | |
| --- | --- |
| Machine | Windows 11 Enterprise 10.0.26200, x64 (AMD Zen 3, 16 logical processors), one monitor, 3840×2160 at 150 % |
| Build | `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:PublishTrimmed=true`, .NET SDK 10.0.401 |
| Published stack | `4c36950` (`final3` runs) and `12ceded` (composer recheck): Broiler.UI 0.1.0-preview.17, Hosting preview.5, Graphics preview.7, Native preview.6, Input preview.5 |
| Adoption stack | `e870135`: Broiler.UI 0.1.0-preview.18-local.7, Hosting 0.1.0-preview.7-local.5, Native 0.1.0-preview.7, Graphics preview.7, Input preview.5 |
| Appearance | App light and dark themes. System high contrast off; high contrast simulated with `--contrast` |
| Test suites | Published: 893 tests (618 shared, 272 Windows, 3 Linux), passed twice at `4c36950` and again at `12ceded`. Adoption with the local packs: 934 (640, 291, 3), passed twice, and each of its 38 commits passes on its own |

## Fixtures

Accept-UI reads 22 fixtures from `--help`, each with a fixed clock and data. The six marked new were
added this round:

- **Inbox and reading:** `inbox`, `empty`, `large-inbox`, `long-message`, `html-only`, `long-html`,
  `new-mail` (new).
- **Inbox problems:** `receive-error`, `body-error`, `receive-canceled` (new), `load-error` (new).
- **Composer:** `large-draft`, `draft-conflict`, `send-rejected`, `send-unknown`,
  `sent-copy-failed`, `draft-invalid` (new).
- **Account and settings:** `invalid-setup`, `save-error`, `test-canceled`, `smtp-test-failed`
  (new), `smtp-test-passed` (new).

The reader runs use the first two groups except `empty`, which has no message to open.
`large-inbox` now reaches the session limit: its demo
mailbox holds 600 messages, 500 are loaded, and a notice above the list says why Load older is
unavailable.

## Result of the final runs

Published-stack runs at `4c36950`: 5 October, 12:44–13:11. Adoption runs at `e870135`: 13:11–13:35
(from the run log; each summary's heading gives the time it was written). The composer recheck at
`12ceded` followed at 14:30 ([below](#composer-recheck-at-12ceded-final4)).

| Run | Stack | Fixtures, sizes, themes | Result |
| --- | --- | --- | --- |
| `final3-published` | published | all 22 at 640×480, 1100×720, 1920×1080; light and dark | 132 of 132 clean |
| `final3-published-reader` | published | 10 inbox fixtures at 640×480, light and dark, `-OpenReader` | 20 of 20 clean |
| `final3-published-200` | published | all 22 at 640×480 and 1100×720, light, `-TextScale 200` | 44 of 44 clean |
| `final3-published-hc` | published | all 22 at 640×480 and 1100×720, light and dark, `-HighContrast` | 88 of 88 clean |
| Accept-Refresh `final3-published` | published | `new-mail`, 4 variants, 1100×720, light | 3 of 4: `RETURN_FOCUS_AUTOMATION` in `kept` (see below) |
| `final4-published-compose` | published, `12ceded` | 6 composer fixtures at 640×480 and 1100×720, light and dark | 24 of 24 clean |
| `final3-adopt` | adoption | all 22, 3 sizes, light and dark | 132 of 132 clean |
| `final3-adopt-reader` | adoption | 10 inbox fixtures at 640×480, light and dark, `-OpenReader` | 20 of 20 clean |
| `final3-adopt-200` | adoption | all 22 at 640×480 and 1100×720, light, `-TextScale 200` | 44 of 44 clean |
| `final3-adopt-aquatic`, `-desert`, `-dusk`, `-night-sky`, `-high` | adoption | `inbox`, `large-draft`, `invalid-setup`, `save-error`, `draft-invalid` at 640×480 and 1100×720, light, `-Contrast` | 10 of 10 clean in each |
| Accept-Refresh `final3-adopt` | adoption | `new-mail`, 4 variants, 1100×720, light | 4 of 4 clean |
| Probe-Uia `final3-adopt` | adoption | 6 checks at 1100×720 | 6 of 6 pass |
| Record-Uia `final3-adopt` | adoption | inbox and composer walks | transcript |

In every Accept-UI run of both stacks, the app started and exposed a named UI Automation tree. Tab
moved through named, visible controls and back to the start: 3 to 15 stops (16 with 200 % text),
and 4 to 6 in the reader runs, which walk the list at 640×480 after Back to inbox. Every window
closed with exit code 0 and an empty stderr. Two fixtures need a note:

- The SMTP fixtures open the account form with its outgoing settings, which has more stops than
  the walk's 24 presses. The walk reached 24 distinct, named, visible stops without coming back to
  its start, so the summary gives no cycle length. This is not a finding.
- `draft-conflict` refuses an ordinary close because its draft cannot be saved, which is intended.

With the forced high-contrast palette, Mail follows the system's light or dark setting rather than
the app theme. The "dark" runs therefore show the light palette (in the final2 runs, 42 of the 44
light/dark pairs were byte-identical).

### Refresh continuity

Both stacks behaved the same apart from the UI Automation return:

| Variant | Result on both stacks |
| --- | --- |
| `kept` | 3 rows added above; the selected row (`item_1:52`) and its place kept. Reader pixels 0 % changed, after a wheel step that moved them about 15 %. Reply and a posted click on the Inbox tab return focus to Reply. |
| `vanish` | 1 row added. The reader closes and the status says the open message is no longer in the inbox. |
| `outside` | 50 rows added. The message stays open and the status points to Load older. |
| `renumber` | 50 rows added. The reader closes and the status says the server renumbered the inbox. |

When the Inbox tab is selected through UI Automation, the two stacks differ:

- **Published stack:** focus stays on the TabItem (`RETURN_FOCUS_AUTOMATION`). With Hosting
  preview.5, UI Automation calls the tab item's SetFocus before Select, and that SetFocus selects
  the tab and then focuses the tab view. The script's own note records this; a pointer click
  returns focus correctly.
- **Adoption stack:** focus returns to Reply.

UI Automation also reports the Inbox tab item's bounds differently. The published stack gives
412 px, an equal share of the tab strip. The adoption stack gives 95 px, the header itself.

### UI Automation semantics (adoption stack only)

Probe-Uia, 6 of 6:

- **disclosure:** "Show Cc and Bcc" reports Collapsed, then Expanded with ControllerFor naming the
  "Cc and Bcc fields" part, then Collapsed again.
- **draft-error:** after Check draft, the To field reports IsDataValidForForm false. Its
  DescribedBy and FullDescription carry "Error: Enter valid email addresses separated by commas."
- **account-error:** after Save account, the Email address field reports the same, with its own
  error.
- **row-names:** all 50 row names carry the received date. The first row shows the full format:
  "Unread, From: Broiler team <hello@example.test>, Subject: Welcome to Broiler.Mail, Received:
  9/28/2026 10:00 AM".
- **row-validity:** 50 rows and 4 tabs report valid and not required.
- **runtime-ids:** after F5 brings new mail, the 47 rows listed both before and after keep their
  runtime IDs (50 rows each time), and the new rows get IDs no other row had.

The Record-Uia transcript shows the following:

- **Tab walk:** one FocusChanged per Tab stop.
- **Select:**
  - A COM-client Select gives ElementSelected, then FocusChanged on the new row.
  - A UIA2 (managed client) Select first raises FocusChanged on the previously selected row,
    because UIAutomationCore calls SetFocus before Select. See the decisions below.
- **F5:** two notifications, "Progress: Receiving newest messages…" and "50 messages loaded."
- **Composer walk:**
  - Expand and Collapse raise ExpandCollapseState changes.
  - Typing into the refused To field makes it valid. Check draft makes it invalid again, raises an
    ImportantMostRecent notification with the error, and moves focus back to To with the error as
    its description.

The semantics Probe-Uia checks come from the Hosting preview.7 mapping, so the probe was not run on
the published stack. There, with Hosting preview.5, rows and tabs leave IsDataValidForForm
unanswered, and UI Automation reports such a value as false (invalid).

### Composer recheck at `12ceded` (`final4`)

The fifth visual review (below) found that after Check draft refused a draft, the composer's
footer kept the routine draft-storage line, while the Account and Settings footers point to their
problems. `12ceded` changes that: after Check draft or Send refuses a draft (and after a failed
discard or a send that could not start), the Compose footer says "The draft has a problem. Details
are below the buttons." An unsaved draft keeps its own pointer, "The draft is not saved. Details
are below the buttons."

- **Test:** `FeedbackPolicyTests.AFailedDraftCheckStaysAndAnEarlierConfirmationNeverClearsANewerStatus`
  asserts the new footer and fails without the fix. The full suite passed at `12ceded`: 893 (618
  shared, 272 Windows, 3 Linux).
- **Native:** `final4-published-compose`, a NativeAOT publish of `12ceded` on the published packages,
  5 October (summary written 14:30). The six composer fixtures (`draft-invalid`, `send-rejected`,
  `draft-conflict`, `send-unknown`, `sent-copy-failed`, `large-draft`) at 640×480 and 1100×720,
  light and dark: 24 of 24 runs without automated findings, with Tab cycles of 7 to 10 stops.
  `draft-conflict` again refuses an ordinary close, as intended. The summary names "12ceded plus
  uncommitted changes"; the uncommitted changes were documentation only. The `draft-invalid`
  capture at 1100×720, light, shows the new footer.

The adoption stack does not contain `12ceded` yet; it will after its rebase.

## Earlier passes of this round

| Pass | When | Build | Result |
| --- | --- | --- | --- |
| `full-stack` | 4 Oct 20:45 | Published packages, executable built at `dade54e` | 132 of 132 clean. The summary names `24dddce`, the checkout when it ran; the binary predates that commit (visual review 1 found its old footer wording). |
| `polish-check` | 4 Oct 21:41 | `581db71` | 24 of 24 (6 fixtures, 2 sizes, 2 themes) |
| `polish2-check`, `-200` | 5 Oct 01:09, 01:10 | `8f664f5` | 8 of 8 each; first runs with `-OpenReader` |
| `final-published` set | 5 Oct 02:17–02:58 | `8f664f5`; `a1903bf` for the two `tabfix` runs | See the list below |
| `final-adopt` set | 5 Oct 02:58–03:22 | `b26394f`, UI local.4, Hosting local.2 | Full 132/132, reader 20/20, 200 % 44/44, five palettes 10/10 each, refresh 4/4, probe 6/6 |
| `final2` | 5 Oct 08:52–09:42 | Published `f7fdb0b`; adoption `afb0926` with UI local.6, Hosting local.4 | Published: 132/132, reader 20/20, 200 % 44/44, high contrast 88/88, refresh 3/4. Adoption: 132/132, reader 20/20, 200 % 44/44, five palettes 10/10 each, refresh 4/4, probe 6/6, and a transcript. |

Ranges are from the run logs; a single time is the one in the run's summary, written when it
finished. `b26394f` and `afb0926` were rewritten away when the adoption branch was rebased, so
neither is on a branch any more; `e870135` is the current adoption revision.

Results of the `final-published` set:

- Full matrix: 132/132.
- High-contrast palette: 88/88.
- Refresh: 3/4.
- 200 % text: 43/44. `new-mail` at 640×480 reported `TAB_OFFSCREEN`, because Tab reached a row
  scrolled out of view. `a1903bf` fixed it, and the `tabfix` runs on the 10 inbox fixtures were
  clean: 60/60, and 20/20 at 200 %.
- Reader: 20 of 44. This first reader run took all 22 fixtures. The 24 runs with findings were the
  12 fixtures that have no message to open ("no enabled Read message button"), so later reader
  runs take only the 10 inbox fixtures.

## Visual reviews

Five reviews were made, each followed by the second-opinion check. The fifth, of the `final3`
captures, was a targeted check of the changes made after the fourth rather than another broad
polish round.

| Review | Screenshots | Candidate defects | Confirmed | Distinct confirmed | Fixed in Mail | Fixed upstream (adoption only) | Left for the user |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1. `full-stack`, 4 Oct | 132 | 33 | 18 | 8 | 4, and 1 stale binary | 3 | 0 |
| 2. `final-published` set, 5 Oct | 304 | 14 | 10 | 10 | 8 | 2 | 0 |
| 3. `final-adopt` set, 5 Oct | 266 | 16 | 12 | 8 | 1 | 4 | 3 |
| 4. `final2`, both stacks, 5 Oct | 570 | 22 | 10 | 8 | 6 | 2 | 0 |
| 5. `final3`, both stacks, 5 Oct | 226 | 9 | 4 | 3 | 1 | 0 | 0 |

Several candidates described the same defect in different fixtures, so the distinct count is
smaller than the confirmed count. The rejected candidates were mostly intended behavior, for
example the inbox notice and the footer saying the same thing in the empty inbox, or a URL that has
no break opportunity wrapping at a character. Review 5's three distinct defects are all
low-severity and cosmetic; two were left open as follow-ups, one in Mail and one in Broiler.UI
(see its table).

### Review 1: `full-stack` (132 screenshots)

| Defect | Disposition |
| --- | --- |
| The footer status text touched the window's left and bottom edges (6 reports) | Mail: inset like the tab names (`38cf169`) |
| The list notice and its Retry sat flush with the tab frame, which covered the notice's colored stripe (2) | Mail: inset like the toolbar (`ffb5812`, `5cba9ff`) |
| The footer said Details and Retry are "beside the list" (1) | Mail: "above the list" (`7a23103`) |
| At 640×480, `body-error`'s footer pointed to a pane the compact layout hid (1) | Mail: the footer says how to show the pane (`eaa7a91`, `bea3d84`) |
| `new-mail`'s footer used wording that `24dddce` had already replaced (1) | The binary predated the revision in the summary. Later runs were built from the revision they name. |
| The tab view drew its focus ring around the whole view, with page text and scrollbar thumbs drawn past it (3) | Upstream (Broiler.UI ADR 0031): ring on the selected header, page clipped inside its frame |
| The dark theme's selected tab label measured 3.4:1 (3) | Upstream (ADR 0031): accent text `#7AB7FF` and a selected-tab bar |
| A disclosure read "Show Keyboard shortcuts" (1) | Upstream (ADR 0031): `FormSection.ShowText`/`HideText`; Mail sets sentence-case labels (adoption branch) |

### Review 2: `final-published` set (304 screenshots)

| Defect | Disposition |
| --- | --- |
| After a receive that found nothing, the reader still said "Receive mail to load your inbox." | Mail (`c7a263c`) |
| The Reply / Reply all / Forward buttons merged with their strip's border | Mail: inset like the toolbar (`c9e8483`) |
| Opening a message dropped a list problem (failed or canceled receive, failed Load older) | Mail: the problem stays until the next page operation (`e4eaeab`, `a94510f`) |
| At 200 % text the inbox notice ended in the middle of a line and hid Retry | Mail: Retry pinned below the explanation, which ends between lines (`da3deaf`, `ffe6b1e`) |
| At 200 % text the composer's capped feedback area showed information first and hid the error | Mail: errors first, scrolled into view (`1acbc23`, `3828e01`) |
| At 200 % text the reader header ran into the message text with nothing between them | Mail: a divider, and Reply kept in view beside the list (`b42b7dc`, `3e33022`) |
| In the compact list, with no reader on screen, the footer said "Reading plain text…" | Mail: "Message selected. Open it to read." (`424cd90`) |
| `new-mail` at 640×480 with 200 % text: `TAB_OFFSCREEN` | Mail (`a1903bf`); already fixed when reviewed |
| At 200 % text the combo box arrow ran into the border | Upstream (ADR 0033): the arrow's slot grows with the font |
| The forced high-contrast palette did not reach the scroll views' scrollbars (grey thumb at 1.9:1) | Upstream (ADR 0033): every scrollbar uses the theme's track and thumb roles |

### Review 3: `final-adopt` set (266 screenshots)

| Defect | Disposition |
| --- | --- |
| `large-inbox`: Load older was disabled with nothing on screen saying why | Mail: the session-limit notice above the list, a fixture that really reaches the limit, and a compact-reader pointer (`9b5912b` to `f7fdb0b`) |
| In the dark theme, a selected row's unread dot measured 2.76:1 (3 reports) | Upstream (ADR 0034): dot color chosen for the row's fill; Mail's presenter passes it on with `WithItem` (adoption branch) |
| A full-width field's focus ring was cut at the form's sides (1) | Upstream (ADR 0034): room for the ring inside forms. Mail's own Tab reveal keeps that room too (adoption branch). |
| The message list's right border was painted over by its scrollbar, leaving a stray corner arc (2, Dusk and high contrast) | Upstream (ADR 0034): frame stroked after the scrollbar. The notch in the toolbar's bottom border where the splitter meets it, reported with it, is still open in Broiler.UI. |
| The first feedback banner touched the action strip (1) | Upstream (ADR 0034): feedback starts 4 DIP below |
| Light and Dark scrollbar thumbs measured 1.9–2.4:1, in two different colors (2) | Not changed; decision for the user |
| The splitter grip measured 2.3–2.6:1 (1) | Not changed; decision for the user |
| Unfocused field borders measured 1.3–1.4:1, up to 1.6:1 at the darkest antialiased pixels (1) | Not changed; decision for the user |

### Review 4: `final2`, both stacks (570 screenshots)

| Defect | Disposition |
| --- | --- |
| The empty reader left a blank band under "The inbox is empty." (2) | Mail (`953bf06`, `da17ade`) |
| On the published stack, the refused recipient field was not marked (1) | Mail: the field shows the error and takes focus, and Cc and Bcc open when needed (`5bdbeaf`, moved from the adoption branch) |
| When the compact header was capped, the Reply strip touched the divider (2) | Mail: a 4 DIP gap, as under the pinned commands (`2863ac5`, `3549ebc`). The review asked for 8 DIP; that was declined. Review 5 confirmed the gap. |
| The session-limit notice did not say what the limit blocks (1) | Mail: "Older ones cannot be loaded in this session." (`f6460de`, `97c81f3`) |
| The reader's date line wrapped before its separator and left a lone "·" (1) | Mail: the line wraps after the separator, never inside the date (`661502c`, `fd48a49`) |
| In the compact reader, the message-problem pointer said "beside it" (1) | Mail: "Details and Retry are below its date." (`cfa75c7`, `6c28375`) |
| The inbox notice was sometimes a Tab stop that scrolled nothing (1) | Upstream (Broiler.UI, in local.7): a scroll view keeps the sizes of its last arrange, and is a stop only while a bar shows. The bar now shows only past a 0.5 DIP overflow, which needs sign-off. The published stack keeps the cause, though the final3 runs did not show the stop. |
| In the contrast palettes, a form's thumb touched the focused field's ring and the fields' corners (1) | Upstream (Broiler.UI, in local.7): a 2 DIP gap between a form and its bar. The reader body's thumb still starts directly under Mail's header divider (not covered). |

### Review 5: `final3`, both stacks (226 screenshots)

The reviewers took `final3-published`, `-reader`, and `-200`, `final3-adopt`, `-reader`, and
`-200`, and the five contrast-palette folders (`final3-adopt-aquatic`, `-desert`, `-dusk`,
`-night-sky`, `-high`), and were asked first to check the changes made after review 4. They
confirmed each of them on the stacks that carry it:

- both stacks: the empty inbox's reader header has no blank band under "The inbox is empty.";
  the capped compact header keeps a gap between its Reply strip and the divider (`html-only` and
  `long-html` at 640×480, light and dark); at 200 % text the date line wraps after "·" and never
  inside the date; the session-limit notice says "Older ones cannot be loaded in this session.";
  `body-error`'s footer says "Details and Retry are below its date.", where they are; the refused
  To field shows the focus ring, the caret, and its error;
- adoption stack: the inbox notice is no Tab stop when it does not scroll (at 200 % and 1100×720
  it shows without a scrollbar, and the Tab cycle is the same as at normal text; only the capped,
  scrolling notice at 640×480 adds a stop); a focused field's ring keeps 2 px or more from the
  scrollbar thumb; and, in all five contrast palettes, the toolkit fixes of earlier reviews: the
  tab ring around the selected header only, "Show keyboard shortcuts", scrollbar thumbs in palette
  colors, a readable unread dot on the selected row, one clean list corner, field rings not cut at
  the form's sides, notices clear of the action strip, and selected-tab and default-button labels
  at 7.0:1 to 11.2:1.

| Defect | Disposition |
| --- | --- |
| After Check draft refused a draft, the composer's footer still showed the routine draft line instead of pointing to the problem, as the Account and Settings footers do (1) | Mail (`12ceded`): "The draft has a problem. Details are below the buttons." Rechecked natively (`final4`, [above](#composer-recheck-at-12ceded-final4)). |
| Compact reader at 640×480, header capped below the Reply strip (`html-only`, `long-html`, light and dark, both stacks): 1 physical pixel of the next, hidden row's top border shows just above the divider, so the divider looks doubled (2) | **Open** (Mail, cosmetic). The cap ends exactly where that row starts. A fix would end it 1 DIP above, which changes layout invariants pinned by the header tests and the 5,300-size sweep, so it is left for a follow-up. |
| In the contrast palettes, the tab focus ring on the selected header sits flush against the window's client frame, so in `high` and some palettes the ring and the frame read as one thick line (1, adoption only) | **Open** (Broiler.UI, cosmetic). The ring comes from ADR 0031 and is only in the adoption packs; the published stack draws its whole-view ring instead. |

The rejected candidates were intended or already known: the compact `long-message` reader showing
only the subject in its capped header (unchanged since review 4, by design); at 640×480 with 200 %
text, the refused To field's label scrolled out of view (2 reports; label, field, and error do not
fit the form's view at that size together, and the field and the first line of its error stay
shown); a focused field that differs only weakly from an unfocused one in `high` and `dusk` (the
ring is visible, which meets WCAG 2.4.7); and the reader body's thumb directly under Mail's divider
(already recorded under review 4).

## Still visible on the published stack

These were fixed upstream. The fixes made in local.4 to local.6 looked right in the reviewed adoption
captures (`final-adopt` and `final2`). Review 5 saw the toolkit fixes again in the `final3`
adoption captures and confirmed the two fixed only in local.7 (the notice Tab stop and form content
against the thumb). The published stack keeps all of them until Mail consumes Broiler.UI
preview.18 and Broiler.Hosting preview.7:

- the tab view's whole-view focus ring, with page content drawn past it;
- the dark selected tab label (3.4:1) and the dark selected unread dot (2.76:1);
- "Show Keyboard shortcuts" in Settings;
- grey scroll-view scrollbars in the forced high-contrast palette, and the combo arrow at 200 % text;
- a focused full-width field's ring cut at the form's sides, the list frame under its scrollbar,
  and the first banner against the action strip;
- the intermittent notice Tab stop, and form content against the thumb;
- the UI Automation side: the return-focus path through UI Automation, tab item bounds as an equal
  share of the strip, rows and tabs read as invalid, and the other semantics Probe-Uia checks, which
  come from Hosting preview.7 (on the published stack, for example, a row's name has no date).

## Deliberately not changed: decisions for the user

The items this round left for the user are listed once, in the
[roadmap status](roadmap-status-2026-10-05.md#decisions-for-the-user). From this record they are:
the scrollbar thumb, splitter grip, and unfocused field border contrast (review 3); the 0.5 DIP
tolerance on Auto scrollbar visibility and the thumb color of Hosting's system palette; the UIA2
Select that first focuses the old row (Record-Uia); and the inbox split minimums, which leave
windows of 680–687 DIP up to 8 DIP short of the readable widths. Localization (UI-14) was skipped
this round, so translated labels and right-to-left layout were not checked.

## Known limits of this record

- **Display:** one monitor at 150 %. Text scale (`--text-scale`) and contrast (`--contrast`) were
  set for the app alone. Display scale was not varied in these runs; the simulated `--scale` is
  covered by tests and the performance runs only.
- **Contrast:** no Windows contrast theme was turned on. The palettes are built from those themes'
  colors. On the adoption branch Mail takes the system's own contrast colors when high contrast is
  on, but that path ran here only through `--contrast`.
- **Screen reader:** none was used. UI Automation structure, properties, and events were checked,
  but speech was not.
- **Input:** posted keyboard input only in these runs. Posted wheel (precision and tilt), dead keys,
  surrogate pairs, and IME composition and placement are covered by tests on a hidden window
  (UI-10), not by physical devices.
- **Servers:** the demo server only, with no live IMAP or SMTP server.
- **Architecture:** win-x64 only; no ARM64 machine.

## Checks still pending

- **The releases.** Broiler.UI `claude/roadmap-integration` at `fd7657f` already contains its topic
  branches (ADRs 0028–0034). Once the ADRs are accepted, it is merged into Broiler.UI main and
  published as 0.1.0-preview.18 (from `fd7657f` or later). Broiler.Hosting
  `claude/roadmap-integration` (`c388a66` or later), packed so far against UI local.7, is rebuilt
  and tested against the published preview.18 and published as 0.1.0-preview.7. Then:
  1. Rebase the adoption branch onto `12ceded` and rebuild it against the published packages.
  2. Rerun the full suite, Accept-UI (default, `-Contrast dusk`, `-Contrast aquatic`),
     Accept-Refresh, and Probe-Uia.
- **Two cosmetic follow-ups from review 5**, not started: the doubled-looking divider under a
  capped compact header (Mail) and the tab focus ring flush against the client frame in the
  contrast palettes (Broiler.UI); see [review 5](#review-5-final3-both-stacks-226-screenshots).
- **Checks for a person, hardware, or Windows settings**: Narrator and NVDA (H-01), a real CJK IME,
  physical keyboard layouts, a touchpad and a tilt wheel, real displays and monitors, real contrast
  themes, text size, and reduced motion, ARM64, and the live SMTP sign-in test. Their steps,
  expected results, and the place to record them are in the
  [manual acceptance checklist](ui-manual-acceptance-checklist.md).
