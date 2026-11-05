# M4 Windows x64 navigation validation

Validated locally on 2026-11-02 against the dirty working tree. These manifests
record unsigned validation packages, not released binaries or provider certification.

- Shared Release: 815 passed, including 11 new folder navigation/race/offline/UI cases.
- Focused Windows reader/profile/keyboard/theme/remote-image checks: 79 passed.
- Both self-contained NativeAOT and framework-dependent packages passed the
  published profile/process-restart fixture, folder-scoped offline model fixture,
  72-library dependency policy and renderer package audit.
- No IL2xxx/IL3xxx warnings were observed in either publish log.

Published profile fixtures use synthetic data and fresh temporary profiles. Saved
INBOX and Archive messages deliberately share UID 1 and UIDVALIDITY 7; the restarted
application model must expose Archive in its cached folder selector, open the right
body, switch back to INBOX and open its different body without network access.
Shared tests additionally exercise discovery retry/cancellation, stale body/page
results, account catalog isolation, folder-specific paging and batch downloads,
nonselectable entries and selector bounds/focus at 640x480 and 200% text scale.

Logs remain in ignored artifacts/m4-navigation-shared.log,
m4-navigation-targeted.log, m4-navigation-windows.log, m4-navigation-aot.log and
m4-navigation-framework.log. Earlier protocol discovery validation is recorded in
m4-discovery-protocol-aot.log. See [M4 progress](../../m4-folders.md) for limits.

Special-use role mapping, server mutations, folder trees, broader provider and
assistive-technology acceptance remain pending. HTML process isolation and other
architecture/platform CI evidence remain open from earlier milestones.
