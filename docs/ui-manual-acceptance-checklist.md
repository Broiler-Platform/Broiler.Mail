# UI manual acceptance checklist

The checks below need a person, particular hardware, or Windows settings. The automated passes of
4 and 5 October 2026 could not provide them: they ran the published NativeAOT app on one x64
machine with one monitor at 150 %, without a screen reader, a CJK IME, a touchpad, or a tilt wheel,
and they drove keyboard, wheel, and IME input with posted window messages. What those passes
checked is in the [UI acceptance record of 5 October](ui-acceptance-2026-10-05.md) and the
[roadmap status](roadmap-status-2026-10-05.md). This list is what remains for H-01 and UI-13.

## Which build to test

| Build | Branch and verified revision | Packages | Restores from NuGet.org |
| --- | --- | --- | --- |
| Published stack | `claude/ui-13-acceptance` at `12ceded` (the full native runs used `4c36950`; `12ceded` adds the composer footer pointer) | Broiler.UI 0.1.0-preview.17, Broiler.Hosting 0.1.0-preview.5 | Yes |
| Adoption build | `claude/ui-09-upstream-adoption` at `e870135` (on `4c36950`; to be rebased onto `12ceded`) | Broiler.UI 0.1.0-preview.18, Broiler.Hosting 0.1.0-preview.7, Broiler.Native 0.1.0-preview.7 | Only once UI preview.18 and Hosting preview.7 are published; verified so far with local packs |

Both branches are local and not pushed. Each check carries one mark:

- **Both**: run it now on the published stack and again on the adoption build after the releases.
  Where the results should differ, the check says so.
- **After releases**: it tests behaviour that only the upstream releases provide. The result on the
  published stack is already known and stated with the check, so it need not be reported again.
- **Now**: run it on the published stack; the code it tests is the same on the adoption branch.

The release order is:

1. Broiler.UI `claude/roadmap-integration` at `fd7657f` already contains its topic branches (ADRs
   0028–0034). Once the ADRs are accepted, it is merged into Broiler.UI main and published as
   0.1.0-preview.18 (from `fd7657f` or later).
2. Broiler.Hosting `claude/roadmap-integration` (`c388a66` or later), packed so far against UI
   local.7, is rebuilt and tested against the published preview.18 and published as
   0.1.0-preview.7.
3. The adoption branch is rebased onto `12ceded` and rebuilt against the published packages, and
   the suite, Accept-UI, Accept-Refresh, and Probe-Uia are run again.

The [roadmap status](roadmap-status-2026-10-05.md#branches) names the topic branches.

Before the releases, the adoption branch builds only on the machine that holds the local packs.
Results from such a build are previews; repeat them on the published packages.

```powershell
# With C:\Program Files (x86)\Microsoft Visual Studio\Installer on PATH (NativeAOT needs vswhere).
dotnet publish src/Broiler.Mail.Windows/Broiler.Mail.Windows.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishAot=true -p:PublishTrimmed=true `
  -p:BroilerUiVersion=0.1.0-preview.18-local.7 -p:BroilerHostingVersion=0.1.0-preview.7-local.5 `
  -p:RestoreAdditionalProjectSources=D:/local-packages/roadmap-2026-10-04 -o artifacts/accept-app-local
```

## Preparing

- Get the source onto the test machine. Both branches exist only on the development machine, and
  `Publish-Windows.ps1` builds only for the machine's own architecture, so a check on another
  machine (ARM64, a second monitor or touchpad, a system with a CJK IME) needs
  `claude/ui-13-acceptance` pushed first, which needs no upstream release, or its source copied
  there. The adoption build also needs the published packages, or the local feed copied with it.
- Use the published app: `./scripts/Publish-Windows.ps1` (or `-Runtime win-arm64` on an ARM64
  machine) writes `artifacts/Broiler.Mail-2.0.0-<runtime>/Broiler.Mail.Windows.exe`.
- Most checks use demo fixtures: `Broiler.Mail.Windows.exe --demo <fixture>`, optionally with
  `--size 640x480` for the compact layout. They need no account, network, or saved data, and the
  window title names the fixture. Demo drafts are kept in memory only.
- Do not add `--scale`, `--text-scale`, or `--contrast` for these checks: those options simulate
  what the checks below set for real.
- For a real account, use a dedicated test mailbox and a separate profile with
  `--data-directory <folder>`.

## Recording results

Append a dated section to the [UI acceptance record of 5 October](ui-acceptance-2026-10-05.md),
one per test session, or to a newer acceptance record if one has replaced it:

```markdown
## Manual checks, <date>

| | |
| --- | --- |
| Tester | |
| Build | branch, revision, packages (as an Accept-UI summary names them) |
| Machine | Windows edition and build; x64 or ARM64; monitors and their scales |
| Settings | keyboard layouts and IMEs; text size; contrast theme; animation effects |
| Assistive technology | Narrator (Windows build) or NVDA version, with non-default settings |

| Check | Result | Notes |
| --- | --- | --- |
| H01-1 | Pass, Fail, or Not done | what was heard or seen |
```

For a failure, write down the steps, the build, and what was heard or seen, and keep screenshots
with the notes. Never record a password. For the SMTP check, record the provider, port, TLS mode,
authentication type, date, and result, as the [SMTP checklist](version-2-smtp-checklist.md) asks.

## H-01: Screen readers (Narrator and NVDA)

Do each check with Narrator (Ctrl+Win+Enter) and with NVDA, at 1100×720 unless a check says
640×480. The full pass belongs on the adoption build after the releases, because Hosting
preview.7 adds most of what a screen reader is given: dates in row names, disclosure state, field
errors as descriptions, rows and tabs reported valid, tab header bounds, and stable row IDs. A
shorter pass on the published stack still finds Mail-side problems such as missing, repeated, or
misplaced announcements.

### Event transcripts to compare with

The adoption branch has two scripts that read what Mail gives a screen reader, without one. They
check Hosting preview.7 behaviour, so run them on the adoption build, with the screen reader off.

- `scripts/Probe-Uia.ps1 -Executable <exe> -Packages "<packages>"` reads the UI Automation tree as
  a screen reader would and prints PASS or FAIL for six checks: the Cc and Bcc disclosure, the
  refused To field, the refused Email address field, row names with dates, rows and tabs valid for
  forms, and row runtime IDs kept across new mail. On `e870135` with the local packs, 6 of 6 passed.
- `scripts/Record-Uia.ps1 -Executable <exe> -Packages "<packages>"` records the focus, selection,
  notification, and state-change events of two scripted walks and saves `transcript.txt`: `inbox`
  (Tab through the window, select rows, Down arrow, F5) and `composer` (on `draft-invalid`: show
  and hide Cc and Bcc, type into To, Check draft).

`-Packages` labels a build made with package overrides; without `-Executable`, the scripts publish
the app themselves. To use a transcript, start the same fixture by hand with the screen reader on,
repeat the walk's steps with the keyboard, and compare line by line. Each FocusChanged and
Notification line should be spoken once; an ExpandCollapseState change as expanded or collapsed;
`IsDataValidForForm=False` as invalid, with the description. A line that is not spoken, speech
that is repeated, or speech with no line is a finding; note the transcript line with it.

### Inbox

- [ ] **H01-1** (Both) `--demo inbox`. Tab from the tab strip through Receive
  mail, Load older, Read message, the message list, and the splitter.
  - Expected: each control is spoken once, with its name and role; the splitter as "Resize panes,
    vertical, 35 %".
  - After releases: the selected row is spoken as "Unread, From: Broiler team
    <hello@example.test>, Subject: Welcome to Broiler.Mail, Received: …", with the full received
    date. Published stack (known): "Unread · Welcome to Broiler.Mail — Broiler team
    <hello@example.test>", without the date.
  - Neither a row nor a tab is spoken as invalid or required. After releases, both report valid.
    Published stack: record what each reader says.
- [ ] **H01-2** (Both) Down and Up arrows in the list.
  - Expected: each newly selected row is spoken once, with its read state. Loading its body is
    silent, and nothing says it was marked read.
- [ ] **H01-3** (Both) Select an unselected row with the screen reader's own command
  (for example Narrator's or NVDA's activate command on the row).
  - Expected: only the new row is spoken.
  - After releases: Record-Uia shows that a Select through the UI Automation COM client, the
    interface Narrator and NVDA use, raises ElementSelected and then one FocusChanged on the new
    row. Only a managed UIA2 client focuses the previously selected row first (an open decision in
    the status).
  - Published stack: not recorded; Record-Uia needs Hosting preview.7. Record what each reader
    says.
- [ ] **H01-4** (Both) Press F5.
  - Expected: "Progress: Receiving newest messages…" and then "50 messages loaded.", once each.
    Focus stays where it was.
- [ ] **H01-5** (Both) `--demo large-inbox`. Press F5, then Load older until it becomes
  unavailable (nine times).
  - Expected: on the last page, "500 messages loaded." and then the notice "Session limit
    reached (500 messages). Older ones cannot be loaded in this session. Receive mail to start
    again.", once each and in that order. Focus moves from the unavailable Load older to the
    message list and is spoken once. Moving through messages afterwards announces nothing.
- [ ] **H01-6** (Both) `--demo receive-error`, `receive-canceled`, and `load-error`. Read the
  notice above the list, Tab to its Retry, and activate it.
  - Expected: the problem text can be read; Retry is named "Retry receiving" or "Retry loading
    older"; the footer says "… Details and Retry are above the list." (a cancellation says
    "Canceled. Retry is available."). The progress and outcome of the retry are each spoken once.
  - After releases: the notice area takes focus and shows a ring only while it scrolls.

### Reader

- [ ] **H01-7** (Both) `--demo inbox`, Enter on the selected row.
  - Expected: with the screen reader's reading commands, the subject, From and To, the date line
    ("Received … · Unread on server"), and the message text can be read in order. Reply, Reply
    all, and Forward are named.
  - Tab after the splitter reaches "Sender and recipients", Reply, Reply all, Forward, and
    "Message text". After releases, both text areas show a focus ring, and "Message header" takes
    focus while it scrolls.
- [ ] **H01-8** (Both) `--demo long-message`, open the message.
  - Expected: the long subject and address and the Unicode text, including the Arabic and Hebrew
    lines, are read in order and in full.
- [ ] **H01-9** (Both) `--demo body-error`, open the message.
  - Expected: the problem under the header and "Retry loading" are read; Retry is reachable by
    Tab and works.
- [ ] **H01-10** (Both) `--demo inbox --size 640x480`. Before opening, read the footer; then
  press Enter on the row and go back, once each with Back to inbox, Escape, and Alt+Left.
  - Expected: the footer says "Message selected. Open it to read."; Enter shows the reader alone;
    each way back returns focus to the row, spoken once.

### Composer

- [ ] **H01-11** (Both) `--demo large-draft`. Tab through To, the Cc and Bcc toggle, Cc, Bcc,
  Subject, the body ("Message"), and the buttons, and press Space on the toggle twice.
  - Expected: each field is spoken with its label; focus stays on the toggle.
  - Published stack (known): the state is only in the toggle's text, "Show Cc and Bcc" or "Hide
    Cc and Bcc".
  - After releases: the toggle is also spoken as collapsed or expanded, and it controls the
    "Cc and Bcc fields" part. Expanding through UI Automation moves keyboard focus to the toggle
    (known); record what each reader says when its own command expands it.
- [ ] **H01-12** (Both) Type a sentence into the body and wait for the draft to be saved.
  - Expected: only the screen reader's own typing echo; autosave, the sender line, and the footer
    are silent.
- [ ] **H01-13** (Both) `--demo draft-invalid`, select Check draft.
  - Expected: "Error: Enter valid email addresses separated by commas." is spoken once, focus moves
    to To, and To is spoken with the error. The footer says "The draft has a problem. Details are
    below the buttons." (from `12ceded`).
  - Published stack: the error is part of the field's name (Broiler.UI preview.17 has no
    description).
  - After releases: To is spoken as invalid, with the error as its description.
  - Typing a character clears the error without an announcement per keystroke.
- [ ] **H01-14** (Both) Correct To to a valid address and select Check draft again.
  - Expected: "Draft fields are valid. No mail was sent." is spoken once and goes away after about
    6 seconds without an announcement.
- [ ] **H01-15** (Both) `--demo send-rejected`, `send-unknown`, and `draft-conflict`.
  - Expected: the outcome and the server's reason can be read; an unavailable Send is spoken as
    unavailable; in `send-unknown`, nothing invites sending again. Discard draft in `send-unknown`
    moves focus to New message, spoken once.

### Setup

- [ ] **H01-16** (Both) `--demo invalid-setup`.
  - Expected: Email address is spoken with "Enter an email address without a display name.": on
    the published stack as part of its name, after releases as invalid with the error as its
    description.
- [ ] **H01-17** (Both) `--demo smtp-test-passed`: type into Password / app password and into
  SMTP password / app password.
  - Expected: the screen reader says only that a hidden character was typed (in its own words),
    never the character. Mail announces nothing while typing. No status, notification, or footer
    text ever contains a password. In a test profile, Save password and Save SMTP password clear
    the field.
- [ ] **H01-18** (Both) `--demo smtp-test-passed`. Tab past Forget SMTP password to Test SMTP
  sign-in (do not press Forget: the test needs the saved password) and activate it.
  - Expected: "Signing in to the SMTP server… No message is sent." and the result are each spoken
    once; the outgoing checklist line reads "Done — Outgoing sign-in tested; no message was sent.";
    focus stays on the button.
- [ ] **H01-19** (Both) `--demo smtp-test-failed`.
  - Expected: the rejection reason can be read beside the outgoing step, and the footer says "SMTP
    sign-in test failed. Details are below the buttons."; the receiving lines keep their state.
    Activating the test again speaks the progress and the result once each.
- [ ] **H01-20** (Both) `--demo test-canceled`, then change any account field without saving and
  select Test connection.
  - Expected: the cancellation is read as information, not as an error. The unsaved test does not
    start: the refusal says what to do first, with the footer "The test did not start.", and each
    press speaks it once.
- [ ] **H01-21** (Both) `--demo save-error` (Settings).
  - Expected: the error is spoken once, and the field it belongs to can be found from it.

### Throughout

- [ ] **H01-22** (Both) Across all of the above:
  - no announcement and no focus change is spoken twice;
  - a result that arrives later never moves focus away from typing;
  - the final result of an operation is spoken, even when its progress was still being read.

### HTML preview

- [ ] **H01-23** (Both) `--demo html-only`, open the message, select Open HTML preview, and Tab
  through the preview.
  - Expected: Show plain text, Zoom out, Zoom in, the HTML message, then its links, each named.
    The level button is skipped while it shows the default zoom.
- [ ] **H01-24** (Both) Press Ctrl+= twice, then Ctrl+-, then hold Ctrl+= for a moment.
  - Expected: "Zoom 110 %.", "Zoom 125 %.", "Zoom 110 %." once each (at 100 % system text size);
    a held key is announced once, at the level it ends on.
  - Tab to the level button: its name starts with the level and says where it resets to, for
    example "125 %, reset zoom to 100 %"; at the default it is "100 %, the default zoom" and
    unavailable. With focus on it, a zoom change is not announced separately, because the
    button's name carries the level. Record whether each reader speaks the new name (not yet
    verified with a screen reader).
- [ ] **H01-25** (Both) Select Show plain text, read the text, then press Escape.
  - Expected: the plain text can be read line by line; Escape closes the preview. Record where
    focus lands.

## IME (Japanese and Chinese)

Add Japanese (Microsoft IME) and Chinese (Simplified, Microsoft Pinyin) in Settings > Time &
language > Language & region. Use `--demo large-draft`, or `--demo` and then Compose > New
message. Type in the body ("Message") and in To, for example "nihon" (Japanese) or "zhongwen"
(Pinyin).

- [ ] **IME-1** (Both) Start a composition.
  - Expected: the composition text is drawn inline at the caret, in the field's font.
- [ ] **IME-2** (After releases) Watch the screen while composing.
  - Expected: no second copy of the composition in a separate IME window (Hosting preview.7 keeps
    the composition inline).
  - Published stack (known risk): Hosting preview.5 lets the IME show its own composition window
    as well. This was confirmed at the message level and has not been seen on screen; record
    whether it appears.
- [ ] **IME-3** (Both) Press Space to open the candidates and choose one.
  - Expected: the candidate list opens next to the composition without covering it, and arrow
    keys and numbers choose a candidate. The candidate list stays in both builds.
- [ ] **IME-4** (Both) Commit with Enter, then type one more character at once.
  - Expected: the committed text (for example 日本) appears exactly once, and the character typed
    straight after it is typed too.
- [ ] **IME-5** (Both) Start a composition and press Escape.
  - Expected: the composition goes, the draft is as it was, and nothing else reacts to Escape.
- [ ] **IME-6** (Both) Type a few plain characters, start a composition at once, keep it open until
  the draft shows it is saved, then commit.
  - Expected: the composition survives the autosave and the status update; the commit appears once
    and is saved.
- [ ] **IME-7** (Both) After IME-6, press Ctrl+Z, then Ctrl+Y.
  - Expected: Ctrl+Z removes the committed text (record whether in one step), Ctrl+Y restores it,
    the text typed before is unchanged, and the draft is saved again.
- [ ] **IME-8** (Both) With the IME on, move into Password / app password and type.
  - Expected: plain characters, no composition, and no IME window showing what was typed.
  - After releases: the IME is also off while focus is on a list, a button, or a tab. Published
    stack: record what happens there.

## Keyboard layout (German)

Add German (Germany) and use a physical German keyboard where possible. Compare each result with
Notepad on the same layout.

- [ ] **KEY-1** (Both) In To and in the body: AltGr+Q, AltGr+E, AltGr+2, AltGr+7, AltGr+8, AltGr+9,
  AltGr+0, AltGr+ß, AltGr++.
  - Expected: @, €, ², {, [, ], }, \, ~, each typed once; no shortcut fires (AltGr+2 does not open
    Account).
- [ ] **KEY-2** (Both) Dead keys in the body: ^ then e, ´ then e, ` (Shift+´) then a, ^ then Space,
  ^ then n.
  - Expected: the same characters as Notepad types, each once.
- [ ] **KEY-3** (Both) Ctrl+2 and Ctrl+4.
  - Expected: Account and Compose open; the shortcuts need their exact modifiers.
- [ ] **KEY-4** (Both) In the HTML preview: Ctrl with the + key, Ctrl with the - key, Ctrl+0, and
  the same on the number pad; then AltGr++.
  - Expected: zoom in, zoom out, reset; AltGr++ does not zoom.
- [ ] **KEY-5** (After releases) In the composer body: Alt+F and Alt+Space.
  - Expected: nothing is typed; Alt+Space opens the window menu; an Alt+letter may beep.
  - Published stack (known): Hosting preview.5 types "f" and a space.

## Touchpad and tilt wheel

Use a precision touchpad and a mouse with a tilting wheel. Record the touchpad's scrolling
direction setting.

- [ ] **PTR-1** (Both) Two-finger scrolling over the message list, the reader, the composer body,
  and the Account form.
  - Expected: smooth scrolling in proportion to the movement, in the direction set for the
    touchpad, stopping when the fingers stop.
- [ ] **PTR-2** (Both) One wheel notch over the list and the reader.
  - Expected: one step per notch, not two.
- [ ] **PTR-3** (Both) Over the HTML preview, Ctrl with two-finger scrolling, then a pinch.
  - Expected: Ctrl with scrolling zooms in whole steps, with small movements adding up; turning the
    other way starts again. Mail handles only Ctrl+wheel; record whether a pinch zooms (Windows
    usually delivers it as Ctrl+wheel).
- [ ] **PTR-4** (After releases) `--demo html-only`, open the preview, zoom to 300 %, then tilt the
  wheel right and left, and swipe sideways on the touchpad.
  - Expected: tilting right shows content further right, as in other applications; the touchpad
    moves the same way as in other applications. Shift+wheel scrolls sideways correctly in both
    builds.
  - Published stack (known): Broiler.UI preview.17 scrolls a tilt wheel the wrong way.
- [ ] **PTR-5** (Both) Tilt the wheel over the main window's reader.
  - Expected: nothing moves sideways, because the reader wraps its text, and nothing else reacts.

## Display scale and monitors

Set Settings > System > Display > Scale. Use 100 % and 200 % on one monitor, and two monitors with
different scales if available. Have a message open, a draft open, and the HTML preview open.

- [ ] **DPI-1** (Both) Start Mail at 100 %, then at 200 %.
  - Expected: sharp text, the same layout in DIPs as at 150 %, the 640×480 minimum size, and sharp
    preview tiles.
- [ ] **DPI-2** (Both) Change the scale while Mail runs, without moving the window.
  - Expected: Mail redraws at the new scale without a restart; the compact or split layout follows
    the width in DIPs; focus, the selected message, the reader's scroll position, and the draft's
    text selection stay.
  - Known: a scale change outside a move is remembered only when the window closes or changes
    state.
- [ ] **DPI-3** (Both) Drag the window from one monitor to another with a different scale, and
  back. Do the same with the HTML preview.
  - Expected: the same as DPI-2, with no flicker loop at the monitor edge; the preview redraws
    sharply at the new scale and keeps its zoom.
- [ ] **DPI-4** (Both) Close Mail on a monitor whose scale differs from the primary monitor's, and
  start it again; repeat with the window maximized.
  - Expected: it opens on that monitor at the same position and size in DIPs, or maximized, and
    restoring a maximized window gives the remembered size. The splitter keeps its position. This
    is unit-tested only so far.
- [ ] **DPI-5** (Both) With the window on the second monitor, disconnect that monitor; then close
  Mail and start it with the monitor still disconnected; then reconnect it and start again.
  - Expected: after the disconnect, Windows moves the window and Mail stays usable with its focus;
    on the next start the window opens on a remaining monitor with its title bar reachable.
    Record what happens after reconnecting.
- [ ] **DPI-6** (Both) At 100 % and at 200 %, compose with an IME in the body and in To.
  - Expected: the composition and candidates are placed at the caret (checked only by tests with
    an injected scale of 100, 150, and 200 %, not on a real display).

## Contrast themes, text size, and motion

### Contrast themes

Turn on Aquatic, Desert, Dusk, and Night sky in turn (Settings > Accessibility > Contrast themes),
once before starting Mail and once while it runs.

- [ ] **HC-1** (Both) Look at all four tabs, the reader, the composer, and the HTML preview's
  toolbar.
  - Published stack (known): Mail uses its own high-contrast palette, light or dark as Windows'
    mode, not the theme's colors. Record whether everything is readable.
  - After releases: Mail uses the theme's own colors for the window, text, selection, buttons, and
    unavailable controls. Compare with screenshots from `scripts/Accept-UI.ps1 -Contrast
    aquatic|desert|dusk|night-sky` on the adoption build, which simulate the same colors.
- [ ] **HC-2** (Both) Select a row, and select text in the reader and the composer.
  - After releases: the selection uses the theme's highlight pair and stays readable; the unread
    mark of a selected row is visible.
  - Published stack (known): the selection uses Mail's own high-contrast palette, as in HC-1, not
    the theme's highlight pair. Record whether the selection and the unread mark are readable.
- [ ] **HC-3** (Both) Tab to Save account, Send, Test connection, a message row, and a tab, and
  hover the pointer over each focused button.
  - After releases: a focus ring is visible on each, also while the pointer hovers it.
  - Published stack: record where the ring is weak or missing. The adoption review found it so
    on the accent fill of Save account and Send, and on hovered buttons, before Broiler.UI drew
    each ring for the fill it is on.
- [ ] **HC-4** (Both) Look at unavailable buttons, errors, warnings, confirmations, and the inbox
  notice (`receive-error`, `draft-invalid`, `send-rejected`).
  - Expected: all readable and distinguishable.
  - Record whether scroll bar thumbs are easy to see; their color in contrast themes is an open
    decision.
- [ ] **HC-5** (Both) Turn the contrast theme off while Mail runs.
  - Expected: Mail returns to the theme chosen in Settings without a restart.

### Text size

- [ ] **TXT-1** (Both) Settings > Accessibility > Text size: 150 %, then 225 %, while Mail runs, at
  1100×720 and 640×480.
  - Expected: text grows without a restart; tab names, rows, the reader header, the forms, and the
    footer are not cut off, as in the `--text-scale` captures. At 640×480 with 200 % or more, the
    compact reader keeps about two lines of text (known).
- [ ] **TXT-2** (Both) With one preview opened before the change and not zoomed, and another zoomed
  with Ctrl+=, change the text size; then open a new preview.
  - Expected: the unzoomed preview follows the new size; the zoomed one keeps its zoom, and its
    level button now resets to the new size; the new preview opens at the new size.

### Reduced motion

- [ ] **MOT-1** (Both) Turn Settings > Accessibility > Visual effects > Animation effects off and on
  while Mail runs; switch tabs, open and close Cc and Bcc, select messages, and change the theme.
  - Expected: nothing in Mail animates with either setting, and switching the setting changes
    nothing visible. On the published stack, neither Mail nor the Broiler.UI controls it uses
    start an animation; on the adoption build, record any. A confirmation still disappears after
    about 6 seconds; that is a timed change, not motion.

## ARM64

On a Windows 11 ARM64 machine with the .NET 10 SDK, PowerShell 7, and the Visual Studio C++
desktop build tools (including the Windows SDK). CI builds and packages win-arm64, but no ARM64
machine has run the app interactively.

- [ ] **ARM-1** (Both) `./scripts/Publish-Windows.ps1 -Runtime win-arm64`.
  - Expected: the publish succeeds, including its smoke test of the executable.
- [ ] **ARM-2** (Both) Start the ARM64 executable, then `--demo`: receive, read, compose, open and
  zoom the HTML preview, change the theme, and close.
  - Expected: the same behaviour as on x64.
- [ ] **ARM-3** (Both) `scripts/Accept-UI.ps1 -Executable <the ARM64 exe>` (the script's own
  publish is x64 only).
  - Expected: no automated findings, as on x64; the summary names the architecture.
  - Do the IME, keyboard, and display checks the machine allows.

## Live SMTP sign-in test

SMTP-1 to SMTP-3 are step 2 of the [SMTP checklist](version-2-smtp-checklist.md), split into
result IDs; SMTP-4 and SMTP-5 go beyond that step and are not in the SMTP checklist yet. Use a
dedicated test mailbox and a separate profile (`--data-directory`). The test code is the same on
both branches.

- [ ] **SMTP-1** (Now) In Account > Outgoing mail setup, choose Configure SMTP with the provider's
  host, port 465, and TLS; save the account, save the SMTP password, and select Test SMTP sign-in.
  - Expected: "Signing in to the SMTP server… No message is sent.", then "Done — Outgoing sign-in
    tested; no message was sent." on the outgoing line. No message arrives anywhere, and no Sent
    item appears.
- [ ] **SMTP-2** (Now) The same with port 587 and Required STARTTLS.
  - Expected: as SMTP-1.
- [ ] **SMTP-3** (Now) Save a wrong SMTP password and test; then save the correct one again.
  - Expected: a rejected sign-in, not a connection or certificate problem; no message is sent.
- [ ] **SMTP-4** (Now) Test port 465 with Required STARTTLS, and port 587 with TLS.
  - Expected: a failure that names the server, the port, and the connection security, or a
    timeout; never a pass. Record the wording.
- [ ] **SMTP-5** (Now) Run Test connection before and after the SMTP tests.
  - Expected: the receiving result is not changed by the SMTP tests.
