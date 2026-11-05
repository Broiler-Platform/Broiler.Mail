# Three M4 everyday-mail subitems: Windows x64 evidence

Validated locally on 2026-11-03 against the dirty working tree. Unsigned local
validation packages are not releases or real-provider certification.

- Shared Release suite: 1,000 passed, including twelve added cases.
- Focused Windows reader/profile/keyboard/theme/remote-image suite: 79 passed.
- NativeAOT/self-contained and framework-dependent publish/smoke passed.
- Published profile fixtures exercise backward/forward search navigation, parsed
  threading metadata and live attachment reminder appearance/clearing. Unexpected
  network access fails the synthetic search receiver.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
- No IL2xxx/IL3xxx warnings observed in either publish log.

Added tests cover English attachment words and quote/signature/forward exclusions,
unchanged draft submission state, live editor feedback, parsed-header content and
scope, and offline disclosure at 100%/200% text. Extended paging model/UI tests
traverse back and forward with retained submitted criteria and Cancel focus. The
complete responsive suite also passes short-header and compact/desktop resize cases.

Ignored logs: artifacts/m4-three-items-shared.log, m4-three-items-windows.log,
m4-three-items-aot.log and m4-three-items-framework.log. Both package manifests
are retained alongside this document. Protocol behavior did not change; the
standalone protocol probe was not rerun for this increment.

See [exact completed scopes and remaining milestones](../../m4-three-everyday-items.md).
Raw MIME source, general attachments and indexed full-text search remain open,
alongside the previously recorded M0–M4 acceptance and implementation gaps.
