# M2: five selected-message text find items

Updated 2026-11-03. Five text-alternative usability subitems are implemented:

1. **Bounded local literal matcher.** `MessageTextFinder` searches one decoded text preview with ordinal case-insensitive, non-overlapping matching. It accepts 1–256 query characters without controls or invalid Unicode and at most 1,000,000 input characters, returning at most the first 1,000 hits plus an explicit overflow flag. There is no regex/query language, accent folding or canonical normalization. This does not raise existing receive/cache preview limits and never searches other messages or contacts a server.
2. **Find/next/previous controls.** HTML-capable messages have a Find text button. The bar provides a submitted query, Find, Next, Previous, a bounded match index, wrapping notices and Close. Editing the query disables stale result navigation/copy until it is submitted again. No matches, invalid inputs and the first-1,000-only limit are explicit. Queries/results are local to the current reader body and are not persisted.
3. **Visible selectable-text matches.** Find switches HTML into the ordinary selectable text alternative, without remembering that temporary switch as a global reader preference. A new message returns to the normal preference. Search uses only body preview text, excluding application-generated fallback/truncation labels. CRLF/CR normalization matches the text editor's representation. The current hit is selected and scrolled into view using the same plain-text layout engine/font/zoom, without rewriting the body or making it editable. The HTML/text viewport and remote-image consent/cancellation policies remain in force. This is not a search over rendered HTML or original source MIME.
4. **Reader-scoped keyboard operation.** Ctrl+F opens/focuses find from the visible reader, including plain-text-only messages. Enter submits the query. F3 and Shift+F3 move next/previous while find is open; Escape closes it and returns focus to selectable text instead of navigating away. AltGr/Alt/Windows chords, list focus, hidden compact-reader panes and Account/Settings/Compose dialogs do not invoke message find. Body replacement disposes old controls and clears query/results; stale public callbacks cannot act on a newer body. Plain-text-only mail keeps its existing collapsed navigation row and can open find with Ctrl+F.
5. **Explicit bounded Copy match.** Copy match writes only the current match (at most 256 characters) as plain text, preserving original case, and never reads clipboard contents. It neither copies the entire body nor opens a link/image or submits mail. Unavailable/busy/failing clipboard hosts show guidance while retaining the message and selected text. Copy is disabled for invalid/edited/no-result queries and old message controls are disposed. Normal text selection/copy remains available after closing find.

## Validation and remaining scope

Fourteen added shared cases cover literal/Unicode/non-overlapping matching,
metacharacters, empty/invalid/oversized queries and text, the result cap/read-only
results, forward/backward wrap, match reveal at 100%/200% text, original-case copy
without clipboard reads, copy failure/current-body guarding, CRLF and generated-label
selection offsets, reader/list/AltGr shortcut scope, close/focus, reset/disposal and
restoration of HTML on the next message, and invalid-query result disabling.
Responsive/header, viewport, keyboard and existing app workflows remain passing.

A new Windows shell case exercises Ctrl+F, Enter, F3 and Escape through the actual
HTML adapter and selectable alternative without opening links. Published raster
smoke exercises local matching, visible selection/reveal, next/wrap, bounded copy
with a private synthetic clipboard, copy failure/stale context and hit limits in
both Windows variants. It does not read or modify the user's real clipboard.
See [Windows x64 validation evidence](evidence/m2-five-message-find-items-win-x64/README.md).

Direct HTML selection/copy, exact semantic HTML/text anchors, complete host lifecycle
refactoring, renderer process containment/native crash/hang recovery and broader
native accessibility/platform acceptance remain open. Complete-body/MIME/attachment
and mailbox-wide search are separate work. M0–M2 are not declared complete. No
JavaScript, runtime code generation or new package dependency is introduced.
