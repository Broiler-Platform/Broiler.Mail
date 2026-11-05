# Five M4 visible manual-outbox items: Windows x64 evidence

Local validation on 2026-11-03 against base revision `fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` plus the dirty working tree. Packages are unsigned validation artifacts, not releases/provider certification.

- Shared Release suite: **1,283 passed**, including 16 added outbox/outcome/view cases.
- Focused outbox/keyboard suite: **36 passed**; the final live-browsing case is included in the full suite. Focused Windows profile smoke: **2 passed**.
- Windows reader/profile/keyboard/theme/remote-image/viewport suite: **87 passed**.
- NativeAOT TLS/STARTTLS protocol probe passed.
- NativeAOT/self-contained and framework-dependent application publish/smoke passed; manifests retained alongside this record.
- Dependency gate passed with 72 runtime libraries; renderer compatibility audit matched.
- No IL2xxx/IL3xxx warnings observed in application/protocol publish logs.
- `git diff --check` passed apart from the preexisting LF/CRLF notice for `docs/roadmap.md`.

Added coverage includes all five account-scoped filters, no-write interruption projections, disabled-account scope/reset, pinned identity and bounded copyable details, live Sending/Unknown and Accepted/Pending/Unknown Sent-copy outcomes, protected accepted/unknown/interrupted parked-record activation and prior raw-draft retention across restart, save failure and unreadable isolation, 100%/200% text selection/filter/handoff, Ctrl+5/modal access, no navigation after closing during a held save, and browsing live progress without enabling another operation. Existing inbox layout test lookups now target Inbox content because the shell legitimately owns a second selectable text surface in Outbox; their geometry/focus assertions remain intact.

Published smoke projects three local records, separates SMTP acceptance from a failed Sent copy, opens an unknown record without transport invocation, preserves the previous active draft, and checks restart/verified backup/restore/cache-cleanup retention. General shell smoke renders Outbox too. A fixture lifetime error (disposing the shell before the handoff) was corrected before final validation. No real account/mail/server or automatic send is involved.

Ignored logs: `artifacts/m4-visible-outbox-build.log`, `m4-visible-outbox-targeted.log`, `m4-visible-outbox-shared.log`, `m4-visible-outbox-windows-targeted.log`, `m4-visible-outbox-windows.log`, `m4-visible-outbox-protocol.log`, `m4-visible-outbox-aot.log`, `m4-visible-outbox-framework.log`.

[Scope and remaining work](../../m4-visible-outbox.md). This is retained local state, not complete sent history or background dispatch. Attachments, import, broader recovery/provider/accessibility/target acceptance and containment remain open. M0–M4 are not declared complete.
