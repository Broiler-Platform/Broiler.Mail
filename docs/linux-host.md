# Linux project foundation

The first Phase 1 item creates `Broiler.Mail.Linux`, a `net10.0` executable referencing the shared Application/Infrastructure projects and the pinned `Broiler.Graphics.Linux.OpenGL` package. It has no dependency on the Windows host. Native scheduling, window rendering, input, clipboard, XDG storage, and credential integration remain separate roadmap items.

## Build and inspect

`Hosting/LinuxUiHost.cs` now implements the existing Broiler.UI `IUiHost` contract
over a caller-owned renderer and surface. It reads live logical viewport/DPI state,
submits frames with increasing frame indices, and preserves pending invalidation
when rendering fails or invalidates again. All calls belong to the UI thread; the
future window loop must handle scheduling, invalidate on resize, recover from
renderer failures, and dispose its resources. The adapter is not yet connected to
an interactive window and does not claim clipboard or text-input support.

The managed `Broiler.Mail.Linux.Tests` suite runs with fake surfaces/renderers on
Linux and Windows in CI. It verifies DPI/viewport forwarding, pending frames, and
failure propagation without requiring X11/EGL. These are adapter tests, not Linux
native acceptance tests.

Use the SDK pinned in `global.json`. This project can be built on Windows or Linux:

```powershell
dotnet build src/Broiler.Mail.Linux/Broiler.Mail.Linux.csproj -c Release
dotnet run --project src/Broiler.Mail.Linux -c Release --no-build -- --help
```

On Linux, check prerequisites:

```bash
dotnet run --project src/Broiler.Mail.Linux -c Release --no-build -- --diagnostics
```

Diagnostics reuse Broiler's native library probe to check `libEGL.so.1`, a desktop OpenGL loader (`libGL.so.1` or `libOpenGL.so.0`), and `libX11.so.6`. They also check that `DISPLAY` is set and the process is x64 or ARM64. Wayland/Vulkan libraries are not prerequisites for the selected X11/EGL backend. An XWayland session may supply X11; this does not establish native Wayland support.

The diagnostic command does not open a display, initialize EGL, read account files, access credentials, or contact mail providers. A successful result means only that the checked libraries and environment prerequisites are present. Display authentication, driver compatibility, actual window rendering, and native input still require runtime acceptance with the future host.

| Invocation | Exit behavior |
| --- | --- |
| `--help` | 0 on any OS; shows the current scope. |
| `--diagnostics` | 0 when the Linux preflight checks pass; 1 on unsupported OS/architecture, missing prerequisites, or probe failure. |
| No arguments | 2 on Linux because the interactive host is unfinished; 1 on another OS. |
| Unknown or extra arguments | 2, with usage text, before native probing. |

Build/help/error-path validation is performed on Windows x64. Linux native execution remains pending: the local WSL registrations have missing virtual disks, as recorded in [Phase 0](phase-0-foundation.md). This item does not claim a working Linux mail window or packaged Linux release.
