# Published hosting packages

Mail consumes `Broiler.Hosting.Windows`, `Broiler.Hosting.Linux`, and
`Broiler.Hosting.Android` **0.1.0-preview.5** from NuGet.org. Versions are pinned in
`Directory.Packages.props`. No sibling checkout or local package source is required.

Mail currently consumes UI preview.17, Graphics preview.7, Native preview.6, and
Input preview.5 alongside Hosting. The original preview.1 adoption used UI
preview.11 and package repository commit `af65d6f7d40801813c79eb33b03f9a032be3683c`;
the validation record below preserves that historical baseline.

| Consumer | Package and responsibility |
| --- | --- |
| Windows application and host tests | `Broiler.Hosting.Windows`: clipboard, theme/system settings, sizing/DPI, input bridge, and UI Automation provider |
| Linux application | `Broiler.Hosting.Linux`: UI host, X11 clipboard, input coordination, and backend diagnostics |
| Dependency probe | All three packages: compile and report published host APIs; Android uses its `net10.0` diagnostic/host surface |

The shared APIs had already been extracted and integrated into Mail. This change
replaces every sibling `ProjectReference` with a `PackageReference` and expands the
dependency probe to report Windows and Linux hosting alongside Android. Mail's
commands, credential storage, draft persistence, and close/save policy remain in Mail.
There is no Android Mail application in this solution.

## NativeAOT publishing

`scripts/Publish-Windows.ps1` and the Visual Studio folder profile enable NativeAOT
and trimming. The script resolves the NativeAOT runtime license from the restored
SDK pack instead of looking for a `.deps.json`, which a native executable does not
emit. The build manifest records the compilation mode and runtime pack version.
The existing CI Windows package matrix invokes this same script for x64 and ARM64.

The published Windows hosting package includes the generated COM UIA adapter.
Mail also retains its generated JSON serialization and the existing, version-specific
`NativeAot.Substitutions.xml` binding for Broiler.HTML.Image preview.9. That HTML
binding must be reviewed when changing renderer versions; adopting Hosting does
not remove it.

## Current validation status (4 October 2026)

The [consolidated audit](roadmap-status-2026-10-04.md) records 421 passing local
tests and green hosted Windows x64, Linux x64, and Windows ARM64 jobs at `d7d78d4`,
including x64/ARM64 NativeAOT package smoke. The recorded native x64 acceptance
also verifies external UIA controls/patterns and status notification events.

The initial empty UIA tree was fixed by creating the native bridges in
`WindowsMailWindow.OnCreated`, after the native HWNDs exist. Hosting preview.5
also fixes IME duplicate-character suppression under load. Real screen-reader
speech, physical IME/input, interactive ARM64 UI, and Linux/Android application
acceptance remain open. Package adoption alone does not establish these results.

## Original preview.1 validation (2 October 2026)

Verified on Windows x64 with .NET SDK 10.0.401:

- NuGet.org restore of the complete solution; restored package metadata confirms
  the public feed for all three Hosting packages.
- 256 tests passed: 202 shared, 51 Windows, and 3 Linux adapter tests.
- Dependency probe built without warnings and reported all three published
  Hosting assemblies at preview.1 and the repository commit above.
- NativeAOT publish completed with warnings treated as errors; the packaged
  executable passed the headless four-tab rendering smoke test.
- Launched the native executable in `--demo`, loaded 50 synthetic inbox messages
  with the Receive button, and navigated to Compose with Ctrl+4.

### Historical UI Automation failure (discovery subsequently fixed)

The initial live native demo inspection exposed the OS window, render pane, and title-bar
controls, but not the Broiler controls beneath the pane. Explicit COM apartment
initialization was investigated and did not change that observation; no speculative
startup workaround was retained. The managed provider tests pass, but do not prove
that an external UIA client can traverse the native provider.

The resulting investigation called for an external-client test of `WM_GETOBJECT`,
fragment navigation, and Control/Content view inclusion. The preview.1 provider
defined the `IsControlElement`/`IsContentElement` property IDs without handling
them in its property switches; this was considered alongside native HWND attachment.
This was an investigation lead, not the confirmed cause. Packaged native discovery
now passes; H-01 remains open for actual screen-reader acceptance.

The [2 October follow-up audit](remaining-improvements-2026-10-02.md) preserves
that investigation. Microsoft documents both view-inclusion flags as defaulting
to true, so their omission alone does not establish the cause of the native failure.

Linux native display/input, Android device execution, Windows ARM64 interactive
UI, and full screen-reader acceptance remain separate platform checks; the
headless tests and API probe do not establish those results.

Package pages: [Windows](https://www.nuget.org/packages/Broiler.Hosting.Windows/0.1.0-preview.5),
[Linux](https://www.nuget.org/packages/Broiler.Hosting.Linux/0.1.0-preview.5),
[Android](https://www.nuget.org/packages/Broiler.Hosting.Android/0.1.0-preview.5).
