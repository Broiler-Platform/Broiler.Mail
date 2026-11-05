# M4: offline search foundation

Updated: 2026-11-04. Saved-mail search includes downloaded body previews. Optional indexed phrase modes are now available; complete-body indexing remains open.

`IMailLocalSearch` is implemented by `SqliteMailCache`. It searches saved sender/subject headers and optionally downloaded plain-text
body previews without network access, using an explicit nonempty account ID and
optional exact mailbox, read-state, star-state and received-date filters. Matching is literal
`OrdinalIgnoreCase` substring matching, including Unicode; SQL syntax, `%` and `_`
have no special meaning. These literal modes need no derived index. The later [indexed-search increment](m4-indexed-search.md) adds optional FTS5 phrase modes and schema-v3 revisions without JavaScript or an additional dependency.

Only rows matching the current `mailbox_heads` epoch are eligible. Old
UIDVALIDITY rows and other accounts are excluded. Results preserve full message
identity, flags and cached-body availability. Ordering is mailbox name descending
(SQLite BINARY), then UID descending within the mailbox. This is deterministic
header browsing order, not relevance or cross-folder date ordering.

Queries accept up to 256 characters without controls (blank text requires a flag or
date/body-availability filter) and return at most
1–500 headers (default 50). One additional match detects `HasMore`; no total-count
scan is requested. The existing cache gate serializes database access. Cancellation
is checked before execution, in the literal matcher and while reading results;
SQLite cancellation errors surface as cancellation instead of a successful empty
result or a storage failure. Returned collections are read-only.

Coverage is saved headers and, when selected, downloaded plain-text body previews.
Attachments, raw HTML, text beyond the stored preview and server-only mail are
excluded. Current-folder and [all-saved-folders results within one account](m4-account-search.md) support bounded next/previous navigation. The
literal implementation scans current cached content. Optional indexed modes now use FTS5 over the same saved-text coverage. Large-mailbox latency and complete-body retention/indexing remain required before closing the parent roadmap item.

## Validation

Four shared tests cover reopened storage, account and epoch isolation, exact folder
filtering, bounded results and overflow indication, flags/body metadata, literal
SQL-like text, Unicode sender matching, body exclusion, invalid inputs and cancelled
queries. Published profile smoke checks search the synthetic deletion fixture after
reopening its cache, verify the retained body marker and reject cross-account results.
Windows x64 NativeAOT and framework-dependent publication both passed this fixture.
The storage-foundation increment passed 947 shared tests. See its [validation evidence](evidence/m4-search-win-x64/README.md).

## Reader controls

The inbox toolbar offers **Search saved mail** when the cache supports local search.
The search row accepts literal text, a Sender or subject / Sender only / Subject
only / Saved body preview / Headers or saved body selector, and an All / Unread /
Starred / Unread and starred filter. **Find saved headers** defaults to the selected folder of the current account. An explicit scope selector now offers all saved folders in that account, with a maximum of 500 results per page. The coverage notice names
the submitted query and folder and offers Next saved results when additional matches exist.
Editing the text alone does not change results; press Find again to submit it.

Search works for disabled accounts too, without enabling network access. Results
open saved bodies only. An uncached body explains that it must be downloaded after
leaving search; opening it never silently contacts the server. While results are
active, receive, ordinary mailbox paging, bulk download, server mutations, undo and local mutation
cleanup retries are unavailable. **Close search** reloads the folder's normal cached
headers without receiving from the server. Account/folder changes reset result mode;
account changes also reset query/filter inputs. Query state is session-only.

Search uses the existing cancellable background operation/generation guard, so
cancelled searches and results from an earlier account cannot replace current mail.
Retry after a search failure/cancellation searches saved headers again. Empty
results say no headers matched and do not claim the server mailbox is empty.

Search controls replace normal toolbar actions while expanded, retaining Cancel.
Regression checks at 640×480 and 100%/200% text verify input focus, transfer to Cancel
while searching, usable completion/close focus and remaining list space. Broader
native screen-reader and keyboard acceptance remains pending.

The shared suite now includes six model/view cases covering offline-only selection,
missing bodies, filters, disabled accounts, empty results, account changes, late
cancelled results and 100%/200% search controls. The published profile fixture also
drives the actual model through search, cached-body opening and exit on a disabled
account. The shared suite passed 953 tests; 79 focused Windows checks and both
Windows x64 publish variants passed. See [reader-search validation](evidence/m4-search-ui-win-x64/README.md).

## Received-date filtering

The reader now adds **All received dates**, **Last 7 days (UTC)**,
**Last 30 days (UTC)** and **Last 365 days (UTC)**. A date or flag filter may
now be submitted without a keyword. Each submitted search captures the current UTC instant
and searches `[now - selected days, now)`. These are rolling elapsed-day windows,
not local calendar dates. Future-dated and unknown-date headers are excluded from
a dated search; **All received dates** includes them. The result notice shows the
submitted UTC bounds. Editing the date selector requires another Find submission.
Date choice resets on account changes, alongside text and flag filters.

The storage contract also accepts optional independent `ReceivedFrom` (inclusive)
and `ReceivedBefore` (exclusive) instants, normalizing both to UTC. Equal or reversed
bounds are rejected. Stored header timestamps already use canonical UTC formatting,
so filtering needs no new index, database migration or dependency. Undated headers
are excluded if either bound exists. The date is the existing saved header's
`ReceivedAt` value; no additional server metadata is requested. Ordering and result
limits remain unchanged.

Four additional cases verify exact tick boundaries, equivalent offset timestamps,
one-sided bounds, unknown dates, invalid intervals, explicit submission and account
reset. Existing UI cases now select a dated query at both 100% and 200% text.
Published profile smoke exercises the rolling date filter on a saved synthetic body.
Validation passed: 957 shared tests, 79 focused Windows checks and both Windows x64
publish variants, including NativeAOT. See [date-filter validation](evidence/m4-search-dates-win-x64/README.md).

## Bounded search paging

**Next saved results** replaces the current result page with up to 500 more matching
headers from the same folder. It clears the selected message/body and keeps at most
500 rows in memory. **Find saved headers** starts over on the first page using the
current controls; Close search returns to ordinary cached-folder navigation.
Previous saved results now reloads earlier submitted pages from a bounded session
history (128 queries, no retained result rows). The notice names the page and result
count. See [previous-page behavior](m4-three-everyday-items.md).

Continuation uses an exclusive full message key `(AccountId, MailboxId, UIDVALIDITY,
UID)` rather than an offset or sequence number. The storage API requires an exact
mailbox filter for continuation and rejects zero UIDs, foreign account/folder keys,
and a missing or changed current mailbox epoch. Cross-folder first-page search is
still supported by the storage API, but cross-folder continuation is not exposed.

The model retains the submitted text, flag filters and captured UTC date bounds for
Next and Retry. Editing controls does not silently alter a later page or recalculate
a rolling date window. Retry after a page failure/cancellation repeats that page,
even if the editable text has since been cleared. Account/folder changes and storage
maintenance keep the existing cancellation and stale-result guards. Continuations
are session-only and results are not a database snapshot: saved mail may change
between pages. Restart Find if the mailbox epoch changes.

Three added shared cases verify complete, nonoverlapping descending-UID pages after
reopening storage; rejected foreign/unscoped/changed-epoch continuations; and a
501-message model workflow retaining submitted filters across edited controls and
a failed-page retry. The 100%/200% search view cases now drive Next and its Cancel
focus transition. Published profile smoke also traverses 501 synthetic headers,
opens a saved body on the second page and exits search on a disabled account.
Validation passed: 960 shared tests, 79 focused Windows checks and both Windows x64
publish variants, including NativeAOT. See [paging validation](evidence/m4-search-pages-win-x64/README.md).

## Header fields and filter-only searches

The field selector restricts literal matching to the saved **Sender**, **Subject**,
or either field. Sender matching uses the existing displayed sender string, not an
address parser or exact-address query language. Unicode case-insensitive substring
matching and literal `%`/`_` behavior are unchanged. Empty text is allowed when an
unread/starred/date or body-availability filter is selected; an empty unfiltered query remains disabled.
The storage contract also accepts explicit read/unstarred booleans as filter-only
criteria. Null/overlong/control-containing text and unsupported field values are
rejected before querying.

Submitted field choices and filter-only queries use the same account/folder scope,
current epoch, page limits, UTC dates and offline body rules as other searches. Next
and Retry retain the submitted field even after its selector changes. The coverage
notice names the submitted field and flags and explains when no keyword was used.
Field selection resets with the account. New selectors are covered by the existing
640x480 tests at 100%/200% text while traversing a second result page.

Five additional shared cases exercise source-specific matching, literal wildcard
characters, blank filtered queries, dates/flags, invalid inputs, explicit submission
and account reset. The existing failed-page retry case now edits the field too,
checking that the submitted field remains fixed. Published profile smoke checks
sender-only matches, subject-only rejection of those sender matches, and a filter-only
query on a disabled account. Validation passed: 965 shared tests, 79 focused Windows
checks and both Windows x64 publish variants, including NativeAOT. See [field/filter validation](evidence/m4-search-fields-win-x64/README.md).

## Downloaded body-preview search

**Saved body preview** matches only the cached `PlainText` preview. **Headers or
saved body** matches sender, subject or that preview. This includes text extracted
from HTML when the saved preview is an HTML fallback; it does not inspect HTML
source, CID image bytes, attachments, uncached bodies or text beyond the stored
32,000-character preview. Truncated previews remain searchable within their saved
text, with explicit coverage in the result notice. Header-only modes stay unchanged.

Opening a match still reads only the saved body, with no automatic receive or
resource download. Account/current-epoch, flag, date, UID continuation and bounded
page rules apply to both new modes. A blank filtered query in Saved body preview
requires a cached body; Headers or saved body can also return header-only rows.
Submitted modes are retained across pages/retries and reset with the account.

The existing source-generated body serializer and validators are reused. SQL checks
blob length before passing it into the managed matcher, and each evaluated body is
checked for supported size/content and exact account/mailbox/epoch/UID identity.
Malformed, oversized or mismatched evaluated bodies fail the query rather than
producing successful incomplete matches. Cancellation is checked around decoding
and comparison. No index, schema migration, script engine or package is introduced.
Decoding is bounded per row, but scanning decoded bodies costs more than header
matching; large-cache performance and indexing remain open.

Five additional shared cases cover reopened saved previews, HTML-source exclusion,
truncation, combined matches, missing bodies, account/epoch isolation and corrupt,
oversized or mismatched bodies. Model coverage verifies the coverage notice and
saved-only opening. Existing 100%/200% UI cases now select combined header/body
search. Published profile smoke finds and opens a body-preview-only match and
checks combined results. Validation passed: 970 shared tests, 79 focused Windows
checks and both Windows x64 publish variants, including NativeAOT. See [body-preview validation](evidence/m4-search-body-win-x64/README.md).

## Custom UTC calendar intervals

Choose **Custom UTC dates** and enter `YYYY-MM-DD..YYYY-MM-DD`. The first date is
inclusive at UTC midnight; the second date is exclusive at UTC midnight. Thus
`2026-11-01..2026-11-02` covers November 1 UTC. Either endpoint may be blank:
`2026-11-01..` searches on/after that date, and `..2026-11-02` searches before it.
At least one date is required. These are UTC calendar boundaries, not local dates.

The editor validates exact ISO dates, leap days, ordering, supported range and a
bounded control-free input. Invalid/equal/reversed intervals disable Find and show
format guidance, leaving existing results intact. A valid custom interval counts
as a filter, so it can be submitted without a keyword. Choosing All dates or a
rolling preset disables custom mode; switching accounts clears its state.
The submitted bounds and coverage notice remain fixed across Next and Retry even
when the editable range changes. Storage continues using its existing date-bound
contract; no schema, index or dependency changes are introduced.

Nine new model cases cover malformed/invalid calendar input, control characters,
reversed/equal intervals, exact midnight inclusion/exclusion, open ends, leap day,
filter-only submission and account reset. Existing paging/retry tests now submit a
custom interval. The 100%/200% search view cases exercise the actual range editor,
invalid Find state and next-page navigation. Published profile smoke submits a
custom interval and verifies that a later invalid edit does not alter its results.
Validation passed: 988 shared tests, 79 focused Windows checks and both Windows x64
publish variants, including NativeAOT. See [custom-date validation](evidence/m4-search-custom-dates-win-x64/README.md).

Read/unread, starred/unstarred and downloaded/not-downloaded controls are now
complete, including their combinations and keyword-free queries. See
[three state/coverage controls](m4-search-state-coverage.md).

Pending: broader keyboard/accessibility acceptance and body index,
large-mailbox responsiveness, broader target certification, and durable mutation
recovery. No provider or server search behavior is certified by these local fixtures.
