# M1 Windows x64 validation

Local results from 2 November 2026; see [M1 persistence](../../m1-persistence.md)
for implementation scope, recovery instructions and outstanding work.

- Shared Release suite: 731 passed, no failures or skips.
- M1 coverage includes 13 original profile/cache/offline tests, 12 maintenance/UI
  cases, and 13 download-policy/quota cases. These cover ephemeral headers-only
  reads, explicit loaded-message downloads, partial failure/retry, cancellation,
  late-result/progress rejection, policy UI binding, quota rollback/refusal, and
  compatibility with manifests lacking a policy or containing unsupported modes, and
  a 640 x 480 cached-inbox toolbar with visible cancel focus.
- Focused Windows profile/cache/MIME and actual HTML raster tests: 2 passed.
- Both published application variants passed profile/SQLite/quota/download-policy
  persistence across a second process, verified backup/restore (including policy),
  recovery-backed cleanup, credential/MIME, actual HTML raster and headless shell checks.
- Both packages passed the known-engine dependency guard with 72 runtime libraries
  and the renderer audit. The NativeAOT package includes e_sqlite3.dll.
- No IL2xxx/IL3xxx warnings observed in captured application publish output.

The manifests identify their base revision, SDK, runtime, source working-tree
state and exact smoke scope. They describe a dirty local checkout, not a release or
ARM64/Linux/provider/device certification. Full logs and ZIPs remain ignored under
artifacts (m1-policy-* and m1-download-policy-tests.log). No real credentials or mail
are included here. Other folders, full attachment retention and authoritative sync
remain separate work; the explicit offline batch is bounded to loaded INBOX headers.
