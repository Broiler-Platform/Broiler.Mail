# M4 downloaded body-preview search: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Unsigned local
validation packages are not releases or real-provider certification.

- Shared Release suite: 970 passed, including five added body-preview cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
- Published profile smoke finds and opens a saved body-preview-only match on a
  disabled synthetic account, then verifies combined header/body results. Its
  receiver rejects unexpected network requests.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in either publish log.

Shared coverage includes reopened plain-text previews, HTML-source exclusion,
truncation, missing bodies, header/body combination, filter-only body availability,
account/epoch isolation, oversized blobs, corrupt JSON and mismatched body keys.
The model case checks the explicit preview/download coverage notice and offline
opening. Existing 640x480 search view cases now select combined header/body mode at
100% and 200% text, traverse a next page and preserve usable focus/list space.

Ignored logs: artifacts/m4-search-body-shared.log, m4-search-body-windows.log,
m4-search-body-aot.log and m4-search-body-framework.log. Both package manifests
are retained alongside this document. Protocol behavior did not change; the
standalone protocol probe was not rerun for this increment.

See [search coverage](../../m4-search.md). This is bounded saved-preview scanning,
not indexed full-body search. Attachment text, raw HTML, uncached mail, text beyond
the stored preview, large-cache performance certification, broader accessibility,
other-platform certification and durable mutation recovery remain open.
