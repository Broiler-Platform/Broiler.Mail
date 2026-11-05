# M4 Windows x64 special-folder mapping validation

Validated locally on 2026-11-02 against the dirty working tree. These are unsigned
validation artifacts, not published releases or real-provider certification.

- Shared Release suite: 831 passed (16 additional role/protocol/form cases).
- Focused Windows reader/profile/keyboard/theme/remote-image checks: 79 passed.
- NativeAOT protocol probe: implicit TLS and STARTTLS both passed, including
  post-authentication SPECIAL-USE capability discovery, extended LIST request,
  Unicode literal mailbox handling and Archive role resolution.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed.
  The fresh synthetic profile/process-restart fixture persists all five explicit
  role overrides and verifies an unconfirmed Archive override offline alongside
  the existing folder-scoped body/cache checks.
- Dependency policy passed with 72 runtime libraries; renderer audit matched.
  No IL2xxx/IL3xxx warnings were observed in either application publish log.

The compatibility test initially exposed missing legacy folderOverrides values
being deserialized as null; omitted/null objects now normalize to automatic
defaults and the legacy round-trip passes. Existing disclosure tests now identify
Sent-copy settings by name and verify the new mapping disclosure's accessible
content relation and sentence-case Show/Hide labels. Form tests reveal and focus
invalid mapping fields at 640x480 with 100% and 200% text size.

Logs remain in ignored artifacts/m4-roles-shared.log, m4-roles-windows.log,
m4-roles-protocol-aot.log, m4-roles-aot.log and m4-roles-framework.log.
See [M4 progress](../../m4-folders.md) for behavior and remaining acceptance.
Server mutations, role-driven Sent-copy/draft synchronization, folder trees and
broader platform/provider/accessibility certification remain pending.
