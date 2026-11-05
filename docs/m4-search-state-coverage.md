# M4: three completed saved-search controls

Updated: 2026-11-03. This closes three search-control subitems, not the broader
indexed-search milestone or the remaining M0–M4 acceptance gates.

## 1. Complete read-state filtering

The saved-search filter selector now supports all, read and unread states. Read can
be combined with either star state and either body-availability state. Comparisons
use cached server flags; a search never refreshes or changes those flags. State
labels in the result notice describe the submitted query, even after controls change.

## 2. Complete star-state filtering

The same selector supports all, starred and unstarred messages, including explicit
read/unstarred and unread/unstarred combinations. All nine read/star combinations
are available. Existing first choices retain their earlier all/unread/starred/both
behavior. Typing a keyword now preserves the selected criteria rather than deriving
state from a small fixed set of selector indexes.

## 3. Downloaded-body coverage filtering

Each read/star choice is offered with any body availability, downloaded body only,
or body not downloaded (27 total combinations in one selector). No extra row of
controls is needed at large text sizes. Body coverage can be the only criterion:
no keyword, date or flag filter is required when it is set.

The storage query adds an optional nullable `IsBodyCached` filter, evaluated against
the existing body-blob presence marker. Downloaded means a saved decoded body exists;
it may still be a truncated preview, and it does not certify complete MIME,
attachments, freshness or freedom from corruption. Opening retains the existing
body validation/error behavior. Body-preview-only matching and missing-body filtering
have no intersection, so that combination correctly returns no matches.

All three controls are local-only, account/current-folder/current-epoch scoped and
compatible with header/body text matching and UTC date filters. Empty unfiltered
queries remain disabled. Next/Previous/Retry preserve submitted criteria, including
explicit false states, despite later edits. Account changes reset all three controls.
No cache migration, new dependency, network request or server mutation is introduced.

## Validation

Twelve added cases cover all nine read/star combinations, both body-availability
states after reopening SQLite, and model behavior for keyword-free searches,
editable text preservation, accurate coverage notices, offline operation and account
reset. Existing failed-page retry and previous/next model/UI tests now change body
coverage after submission and verify that the original criteria remain in effect.
The 640x480 controls pass at both 100% and 200% text. Published profile smoke verifies
read/unstarred/downloaded matches and excludes them with the missing-body filter.

Validation passed: 1,012 shared tests, 79 focused Windows checks and both Windows
x64 publish variants, including NativeAOT. See [Windows x64 evidence](evidence/m4-search-three-filters-win-x64/README.md).

Indexed full-text search, large-cache responsiveness, broader native accessibility,
other-architecture/platform certification, durable mutation recovery and the other
recorded M0–M4 gaps remain open.
