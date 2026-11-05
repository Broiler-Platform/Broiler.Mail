# M4: three everyday-mail subitems

Updated: 2026-11-03. These are completed subitems, not completion of all M0–M4 work.

## 1. Parsed-header inspection

The reader offers Show/Hide parsed headers beside its Back action. It expands the
existing copyable, read-only header text with parsed From, Reply-To, full Subject,
Message-ID, In-Reply-To and References when available. Normal To/Cc stay visible.
The disclosure explicitly states that original MIME/transport headers are not
retained. It never invents raw source, exposes Bcc, downloads another message or
changes flags. Cached composition metadata works offline. Selection changes reset
the disclosure. The normal header scroll/whole-row bounds and compact Back position
are preserved; desktop keeps the subject first.

## 2. Attachment reminder

Editable drafts show a warning when the subject or authored body contains the
English words attached, attachment or attachments (case-insensitive, whole words).
Quoted `>` lines, the standard signature separator and forwarded-message content
are excluded from body detection. The subject is always checked. This is an English
heuristic, not a guarantee that every reference to a file will be detected.

The reminder states that attachments are not supported and only plain text will be
sent. It appears/clears as the text changes and shares the existing scrollable
feedback area, including warning priority and announcement behavior. It does not
block sending, modify a draft, change SMTP acceptance/unknown states or introduce
retries. General attachment receive/save/compose/send remains open.

## 3. Previous saved-search page

Previous saved results reloads the prior submitted page using its original query,
field, flags, captured date interval and exclusive full-key continuation. At most
128 previous page requests are retained in memory, without retaining their rows or
bodies. The current page still holds at most 500 rows. Moving back clears the current
reader selection/body; Next can traverse forward again. Find, account/folder changes
and closing search clear history. Failed-page retries retain the requested page.

History is session-only and re-queries the live cache, not a frozen snapshot. Existing
account/folder/epoch checks and stale-result rejection remain in force. When older
history has been dropped, Find can restart from page one. Neither direction receives
from the server or opens uncached bodies automatically.

## Validation and remaining work

Twelve added cases cover English detection and exclusions, live composer feedback,
unchanged editable submission state, parsed-header content/scope, and offline header
disclosure at 100%/200% text. Existing model and view paging cases now traverse back
and forward, retain submitted filters after edits/failure and verify Cancel focus.
The complete responsive-reader suite covers short headers and resize behavior.
Published profile fixtures exercise previous/next, parsed metadata and reminder
appearance/clearing without provider credentials or network access.

Validation passed: 1,000 shared tests, 79 focused Windows checks and both Windows
x64 publish variants, including NativeAOT. See [Windows x64 evidence](evidence/m4-three-items-win-x64/README.md).

Still open: M0 matching-architecture certification; broader M1 storage/offline policy
acceptance; M2 process containment/accessibility; M3 aliases/orchestration; M4 durable
mutation recovery, attachments, indexed search, multiple drafts/outbox and MIME
import/export. Parsed metadata inspection does not complete raw-source retention.
