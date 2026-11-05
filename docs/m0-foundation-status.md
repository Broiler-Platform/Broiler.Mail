# M0 foundation progress

Updated: 2026-11-02. Status: Windows x64 implementation and acceptance pass;
matching-architecture Windows ARM64/Linux CI evidence remains outstanding.

This records the M0 baseline. [M1](m1-persistence.md) now adds the previously
probe-only SQLite provider to the Windows application's durable cache; earlier
M0 package/dependency counts and probe-only descriptions below are historical.

## Delivered

- The [Broiler.Net reuse review](broiler-net-session-profile-review.md) distinguishes
  its existing HTTP/cookie sessions from the durable profile/mailbox storage it does
  not supply. A new preview.4 NativeAOT probe verifies isolation, cookie-free modes,
  redirect policy, cancellation and disposal without bringing a Browser/JS dependency
  into Mail. Net reuse is planned for M2's HTTP resource broker; M1 storage stays Mail-owned.
- ADR 0007 selects a single SQLite database for initial mailbox metadata/message
  blobs with bounded incremental I/O, after evaluating split-file storage. Existing
  profile/draft JSON and credentials remain untouched until M1 migration work.
  Microsoft.Data.Sqlite 10.0.14 / SQLitePCLRaw 2.1.13 are probe-only dependencies;
  the shipped application has no database dependency or durable mail cache yet.
- ADR 0008 makes inline HTML the forward default while preserving resource policy
  and the separate containment gate. ADR 0009 defines release/dependency policy.
- Validated MailboxKey and IMailCache contracts define account/epoch boundaries.
  Existing protocol adapters retain their account-scoped cancellation contracts.
- The Windows published executable saves a synthetic profile/settings, round-trips
  a uniquely scoped OS credential, and reopens them in a second executable process.
  The fixture uses a fixed temporary root and GUID identities, never real profiles.
  MIME parsing, actual Broiler.HTML table/text layout and raster pixels, and the
  existing headless shell checks run in the same published application's smoke mode.
- A separately published NativeAOT ProtocolProbe links the existing synthetic TLS
  servers and exercises production receive/send adapters over both TLS and STARTTLS:
  bounded read-only IMAP receive/body, SMTP acceptance, and private Bcc handling.
  It is development-only and is not included in Mail packages. This is controlled
  protocol acceptance, not certification of any live provider.
- StorageProbe publishes/runs NativeAOT and verifies 20,000-row account/epoch-scoped
  paging, FTS5, migration commit/rollback, newer-schema refusal, writer contention,
  process-kill recovery, integrity checks, quota-induced SQLITE_FULL, and an 8 MiB
  file/database blob hash round trip using 64 KiB writes. Split-store orphan recovery
  is only simulated; no application cache/migration/recovery engine exists yet.
- Windows and Android publishers enforce the known-engine runtime dependency guard.
  Both native probes also run it. The renderer audit pins reviewed package binaries
  and substitution contents, forcing re-audit on upgrades. See
  [renderer AOT audit](renderer-aot-audit.md). Probe AOT/linker warnings are errors.
- CI now runs Windows x64/ARM64 probes and both application package variants; a Linux
  x64 job runs storage/protocol probes without advertising a Linux GUI. The Publish
  workflow gates Windows release builds on native probe success and retains evidence.

## Local validation

Environment: Windows x64, SDK 10.0.403, runtime/compiler 10.0.14, base revision
`fc1f74ffc5e8d3dae03b8fd81c39aa851bdf5c23` with working-tree changes.
Package and probe manifests record the dirty-tree flag and source/SDK context.

| Check | Result |
| --- | --- |
| Shared Release suite | 693 passed, 0 failed, 0 skipped (foundation suite). |
| Windows profile/MIME and actual HTML raster tests | 2 passed. |
| Windows x64 application NativeAOT and framework-dependent packages | Both passed second-process profile/credential reopen, MIME, actual HTML layout/raster, and shell smoke. 67 runtime libraries passed policy in each variant. |
| NativeAOT StorageProbe | Passed all checks; SQLite 3.53.3. Output includes e_sqlite3.dll; package inventory includes MIT/Apache-2.0 license metadata. |
| NativeAOT ProtocolProbe | TLS and STARTTLS receive/send fixtures passed. |
| Broiler.Net session probe | Managed and Windows x64 NativeAOT passed five groups of synthetic-handler session/policy checks against consumed preview.4; no AOT warnings. No disk/live-HTTP/OAuth claim. |
| Dependency/audit checks | Policy fixtures passed; restored renderer hashes match and changed/missing renderer-package fixture is rejected. |
| Android ARM64 APK packaging | Passed; 56 runtime libraries passed dependency policy. Development-key package only; no device or storage-provider acceptance claimed. |
| Warnings | No IL2xxx/IL3xxx warnings observed in local application/probe publish logs. |

The first NativeAOT storage sample inserted 20,000 rows in 44 ms and read one
50-row keyset page in less than 1 ms; 8 MiB file/database writes took 15/56 ms and
peak process working set was approximately 27 MiB. A rerun measured 46 ms inserts
and 18/58 ms blob writes. These single synthetic runs validate feasibility; they
are not performance budgets, broad benchmarks, or equivalent full-store comparisons.

Reproduce from the Mail repository using PowerShell 7:

```powershell
./scripts/Test-NativeDependencies.ps1
./scripts/Confirm-M0Probes.ps1 -Runtime win-x64
./scripts/Confirm-RendererAotAudit.ps1 -AssetsPath src/Broiler.Mail.Windows/obj/project.assets.json
dotnet test tests/Broiler.Mail.Tests/Broiler.Mail.Tests.csproj -c Release
dotnet test tests/Broiler.Mail.Windows.Tests/Broiler.Mail.Windows.Tests.csproj -c Release --filter FullyQualifiedName~ProfileSmokeCheckTests
./scripts/Publish-Windows.ps1 -Runtime win-x64 -Variant self-contained
./scripts/Publish-Windows.ps1 -Runtime win-x64 -Variant framework-dependent
./scripts/Publish-Android.ps1 -Runtime android-arm64 -Format apk
```

Small result/inventory snapshots are retained under
[evidence/m0-win-x64](evidence/m0-win-x64/README.md). Full ignored local evidence is
under artifacts/m0/win-x64, with application logs m0-publish-html.log and
m0-publish-framework.log and Android log m0-android-policy.log. Generated ZIP/APK
files remain unsigned/development-key validation artifacts. Nothing was published
externally. Renderer containment is still unimplemented.

## Remaining before M0 completion

1. Execute the configured matching-architecture Windows ARM64 and Linux x64 CI
   jobs and record their results. They were not dispatched from this dirty local
   checkout. This machine is x64; both its Ubuntu and Debian WSL entries failed to
   mount missing virtual disks, so neither could supply Linux runtime evidence. No WSL repair was
   attempted as part of Mail work.
2. Resolve any platform-specific provider/native-library/AOT warnings revealed by
   those runs. Android cache-provider/device validation belongs to its M1/platform
   integration; APK packaging alone does not certify SQLite there.

M1 implements cache persistence, profile migration, locking/recovery, quota policy,
and representative mailbox performance. M2 implements inline hosting/containment.
The probes and design decisions do not enable either user-facing feature.
