# Published hosting packages

Mail consumes `Broiler.Hosting.Windows`, `Broiler.Hosting.Linux`, and
`Broiler.Hosting.Android` **0.1.0-preview.1** from NuGet.org. Versions are pinned in
`Directory.Packages.props`. No sibling checkout or local package source is required.

The packages' repository metadata identifies commit
`af65d6f7d40801813c79eb33b03f9a032be3683c`. Their dependency requirements match Mail's
existing UI preview.11, Graphics preview.7, Native preview.6, and Input preview.5.

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

## Validation

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

### Outstanding UI Automation acceptance

The live native demo inspection exposed the OS window, render pane, and title-bar
controls, but not the Broiler controls beneath the pane. Explicit COM apartment
initialization was investigated and did not change that observation; no speculative
startup workaround was retained. The managed provider tests pass, but do not prove
that an external UIA client can traverse the native provider.

Follow up in the shared Windows host with an external-client test of `WM_GETOBJECT`,
fragment navigation, and Control/Content view inclusion. The preview.1 provider
defines the `IsControlElement`/`IsContentElement` property IDs without handling
them in its property switches; review this along with the native HWND attachment.
This is an investigation lead, not a confirmed cause. Keep H-01 native UIA and
screen-reader acceptance open until the packaged provider exposes the controls.

Linux native display/input, Android device execution, Windows ARM64, and full
screen-reader acceptance are separate platform checks; the headless tests and
API probe do not establish those results.

Package pages: [Windows](https://www.nuget.org/packages/Broiler.Hosting.Windows/0.1.0-preview.1),
[Linux](https://www.nuget.org/packages/Broiler.Hosting.Linux/0.1.0-preview.1),
[Android](https://www.nuget.org/packages/Broiler.Hosting.Android/0.1.0-preview.1).
