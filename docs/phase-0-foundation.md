# Phase 0: cross-platform foundation

Implementation date: 2026-09-30. The foundation is implemented; the Phase 0 exit gate still requires a successful hosted CI matrix. No Linux or Android application host is introduced here.

## Published dependency baseline

The following versions were found on nuget.org and restored into a fresh workspace-local package cache. The [API probe](../eng/Broiler.Mail.DependencyProbe/Program.cs) compiled against them and ran on Windows x64 without constructing native surfaces. Versions are pinned in [Directory.Packages.props](../Directory.Packages.props); the SDK is pinned to 10.0.401 in `global.json`.

| Package | Version | Verified managed API / remaining work |
| --- | --- | --- |
| Broiler.Graphics.Linux.OpenGL | 0.1.0-preview.7 | `LinuxOpenGlRenderer.CreateX11WindowSurface` exists. Native X11/EGL execution and ordinary desktop input remain Phase 1 work. |
| Broiler.Graphics.Android | 0.1.0-preview.7 | `AndroidOpenGlEsRenderer.CreateWindowSurface` exists. Activity/SurfaceView ownership and device validation remain Phase 3 work. |
| Broiler.Native.Linux, Broiler.Native.Android | 0.1.0-preview.6 | Published native binding dependencies restore with the selected graphics packages. Native libraries are supplied by the target system. |
| Broiler.Input.Touch.Android | 0.1.0-preview.5 | `AndroidTouchProvider` exists; Activity/view event forwarding remains to implement. |
| Broiler.Input.Text.Android | 0.1.0-preview.5 | `AndroidTextInputProvider` and `IAndroidEditorTextSource` exist; InputConnection and editor integration remain to implement. |
| Broiler.Input.Keyboard.Android | 0.1.0-preview.5 | `AndroidKeyboardProvider` exists; host integration remains to implement. |
| Broiler.UI standard controls / foundation | 0.1.0-preview.10 | Existing UI host, clipboard, text-caret and dispatcher contracts are reused. |

The probe is an engineering executable, outside the production host dependency graph. Its JSON output identifies the loaded assembly versions and revision metadata. CI also records the resolved transitive package graph. No Android workload is needed to check these `net10.0` managed APIs; this does not validate an Android application or native libraries.

Known upstream gaps: Wayland presentation and Vulkan WSI/swapchain presentation are unfinished in the inspected Broiler.Graphics sources. Neither is required for the initial Linux X11/EGL host. The Linux raw-evdev demo does not provide a normal desktop text/IME bridge. No `BroilerActivity` base class was found. These are explicit implementation dependencies, not promised package features.

## Contract and ownership decisions

| Responsibility | Reuse | Ownership / integration |
| --- | --- | --- |
| Viewport, scale, invalidation and presentation | Broiler.UI `IUiHost` | Each platform adapter owns its native window/surface and event loop. |
| Clipboard | Broiler.UI `IUiClipboardHost` | Platform adapter; password copy/cut restrictions remain in controls. |
| Text caret / composition placement | Broiler.UI `IUiTextInputHost` | Publishes caret state only. Hosts must additionally deliver committed/composing text and editing actions. |
| UI scheduling | Broiler.UI `IUiDispatcher` and queued dispatcher | Keep UI state on the owning thread; cancel/drop callbacks after disposal. |
| Credentials | Core `ICredentialStore`, `CredentialKey` | Platform-protected store; preserve account/protocol/server binding and cancellation. |
| HTML preview | Application `IHtmlPreviewHost` | Keep the public adapter; implement the restricted process/broker behind it. See the [security specification](html-renderer-security.md). |
| Settings, accounts, drafts, mail | Existing Core service contracts and Infrastructure implementations | Supply platform storage paths and lifetimes through composition roots. |

No new `Broiler.Mail.Hosting` interface layer is needed. `WindowsUiHost`, its clipboard/text adapters, and native window lifecycle remain Windows-specific. Shared views and view models now use system/device-neutral copy.

The potential extraction is the portable portion of `ScrollableHtmlView`, `HtmlViewElement`, and `DenyingRequestTransport`. It is deliberately deferred until the renderer process protocol is implemented: moving today's in-process renderer into a shared library would not meet the security boundary. Keep reusable graphics/input mechanics in Broiler.Graphics/Input/UI; mail policy, selected-message identity, and preview broker behavior belong in Broiler.Mail.

## CI and local verification

[Phase 0 CI](../.github/workflows/ci.yml) checks `ubuntu-24.04` x64, `windows-2025` x64, and `windows-11-arm` ARM64. Every runner builds/runs the API probe and runs shared tests; Windows runners also execute the native host suite. If an ARM64 runner is unavailable for the repository, configure an equivalent ARM64 runner rather than moving its smoke test onto x64.

The workflow has read-only repository permissions, pinned action revisions, and no signing/publishing secrets. Its NuGet cache starts empty on each hosted runner. [NuGet.config](../NuGet.config) excludes local feeds and fallback folders; `Directory.Build.props` explicitly selects it for restore, including on case-sensitive systems. Artifacts contain the SDK/runner/revision record, API probe JSON, dependency graph, and TRX results. `Confirm-TestResults.ps1` rejects missing, empty, failed, or skipped suites and writes actual counts to the job summary.

Windows packaging waits for the complete test matrix. `Publish-Windows.ps1` rejects a mismatched OS/process architecture before publishing, uses a fresh output directory, and runs the published EXE's headless smoke check. Packages include `build-manifest.json` with the source revision, dirty-tree flag, SDK/runtime, smoke scope, unsigned status, and pending renderer-isolation status. A checksum is not a signature. No GitHub Release is created by Phase 0 CI.

Local commands (PowerShell; the Windows suite/publish command require Windows):

```powershell
dotnet run --project eng/Broiler.Mail.DependencyProbe -c Release
dotnet test tests/Broiler.Mail.Tests -c Release --logger 'trx;LogFileName=shared.trx' --results-directory artifacts/validation
dotnet test tests/Broiler.Mail.Windows.Tests -c Release --logger 'trx;LogFileName=windows-host.trx' --results-directory artifacts/validation
./scripts/Confirm-TestResults.ps1 -ResultsDirectory artifacts/validation -IncludeWindowsHost
./scripts/Publish-Windows.ps1 -Runtime win-x64
```

## Validation record

| Check | Result on 2026-09-30 |
| --- | --- |
| Fresh NuGet restore and managed API probe | Passed on Windows x64, SDK 10.0.401 / runtime 10.0.12. |
| Shared tests | 195 passed, 0 failed, 0 skipped on Windows x64. |
| Windows host tests | 6 passed, 0 failed, 0 skipped on Windows x64. These tests do not prove OS renderer isolation. |
| Windows x64 package | Published executable smoke passed; ZIP manifest, included security documentation, and SHA256 verified. Artifact is unsigned and not release-ready. |
| Publish/result guards | ARM64-on-x64 rejected before publish; missing, empty, failed, and skipped result fixtures rejected. |
| Workflow syntax | Passed actionlint 1.7.12; this is static validation, not hosted execution. |
| Linux execution | Pending hosted CI. Local Ubuntu and Debian WSL registrations point to missing virtual disks. |
| Windows ARM64 execution | Pending architecture-matched hosted CI. |
| Hosted CI | Workflow added; not yet executed from this working tree. |

Platform claims must use the results of a completed run for the relevant revision. Local Windows success does not close the cross-platform exit gate.
