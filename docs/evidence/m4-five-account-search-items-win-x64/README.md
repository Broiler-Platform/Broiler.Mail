# Five M4 account-wide saved-search items: Windows x64 evidence

Local validation on 2026-11-04 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned validation artifacts, not releases/provider certification.

- Shared Release suite: **1,445 passed**, including **25 new account-wide search cases**.
- Focused search/navigation suite: **99 passed**.
- Windows reader/profile/keyboard/theme/remote-image/viewport suite: **87 passed**.
- NativeAOT TLS/STARTTLS protocol fixtures: **both passed**.
- NativeAOT/self-contained and framework-dependent application publish/smoke: **passed**; retained [NativeAOT manifest](self-contained-build-manifest.json) and [framework-dependent manifest](framework-dependent-build-manifest.json).
- Dependency gate: **72 runtime libraries**; renderer compatibility audit matched.
- No IL2xxx/IL3xxx warnings in protocol or either application publish log.
- `git diff --check` passed apart from the preexisting LF/CRLF notice for `docs/roadmap.md`.

New coverage includes binary folder/UID ordering (including supplementary Unicode), literal/indexed cross-folder paging with existing filters, independent epochs and duplicate UIDs, token/account binding, changed/add/remove/foreign heads, bounded head count, previous/submitted-query behavior, disabled accounts, exact cached body attribution, missing bodies without network, rejected invalid/stale/queued results, and 100%/200% explicit scope and accessible folder labels. Existing search view tests caught the added scope selector squeezing results at 200%; bounded scrolling restored the list-height/focus checks. Full-key row IDs fix the duplicate-ID failure uncovered by equal epoch/UID pairs across folders. The refresh-continuity test now checks the selected full message key rather than its former epoch:UID presentation string; its anchor, text selection, scroll and preview assertions remain unchanged.

Published synthetic application smoke checks the scope control, 502 saved headers across two folders, page-boundary continuation, full-key cached body selection and account/folder reader labels, Previous plus epoch-change refusal and explicit new search. No real mail/provider or network receiving/sending is involved.

Ignored logs: `artifacts/m4-account-search-build.log`, `m4-account-search-targeted.log`, `m4-account-search-shared.log`, `m4-account-search-windows.log`, `m4-account-search-protocol.log`, `m4-account-search-aot.log`, `m4-account-search-framework.log`.

[Scope and remaining work](../../m4-account-search.md). This searches saved previews in one account. Full-body indexing, multi-account unified views, authoritative sync, broader provider/target/accessibility/latency acceptance and renderer containment remain open. M0–M4 are not declared complete.
