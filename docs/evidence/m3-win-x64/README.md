# M3 account selection validation

Local checkout results from 2 November 2026; [scope and next work](../../m3-account-collection.md).

- Shared Release suite: 776 passed, no failures/skips in the final run.
- Eight new integration cases cover selected startup/restart, account-scoped cached
  UIDs, retained draft authority, add/cancel, unsaved edits, active submission,
  disabled-cache access, late receives, keyboard selection and form/password reset.
- Management coverage additionally verifies enable/disable credential/cache/draft
  retention, no automatic receive, canceled/unsaved changes, persistent ordering
  without reader reset, late receive rejection after disabling, bounded controls
  at 640 x 480 and 200 percent text, and usable focus at boundaries/collapse.
- Removal covers explicit confirmation/cancel, last-account focus, retained and
  unreadable drafts, backup/restore, unchanged confirmation checks, partial password
  failure, cancellation between credential slots and idempotent retry.
- Focused Windows reader/broker/theme/profile tests: 79 passed, including synthetic collection
  import, selected identity/order/disabled-state reopen and verified recovery restore.
- Windows x64 NativeAOT and framework-dependent packages passed the published smoke
  checks. The new source-generated collection serialization is exercised by those
  binaries, not just by managed unit tests.
  Published smoke also exercises the actual application model's selected startup,
  switching, pinned draft sender and selector/shell layout without network access.
  It now checks account enable/disable/order through the actual application model.
  Published removal additionally uses real synthetic Windows Credential Manager
  slots, including old SMTP credentials without current SMTP settings. The other
  account's credential and removed account's local body remain intact; recovery
  restores the removed identity.
- Dependency policy passed with 72 runtime libraries; renderer package fingerprints
  matched. No IL2xxx/IL3xxx warnings were observed.

The first collection smoke exposed the old five-file backup-manifest ceiling;
the implementation now derives it from the allowlist, and the backup regression
includes every configuration file. One initial shared run also reported a composer
focus failure; it did not recur in the focused outcome matrix or complete rerun.

Manifests describe unsigned dirty-checkout artifacts, not published releases.
Latest logs remain in ignored artifacts/m3-removal-shared.log,
m3-removal-targeted.log, m3-removal-windows.log, m3-removal-aot.log and
m3-removal-framework.log. Broader native UI/provider acceptance, aliases and
per-account orchestration still prevent calling M3 complete.
