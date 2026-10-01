using System.Runtime.InteropServices;
using Broiler.Graphics.Linux;
using Broiler.Graphics.Linux.OpenGL;

namespace Broiler.Mail.Linux.Hosting;

/// <summary>Preflight library checks only; does not initialize a display or an EGL context.</summary>
internal static class LinuxBackendDiagnostics
{
    public static bool Run(TextWriter output)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("X11/EGL diagnostics require Linux.");

        output.WriteLine($"Backend: {nameof(LinuxOpenGlRenderer)} (X11/EGL)");
        output.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        bool supportedArchitecture = RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;
        if (!supportedArchitecture)
            output.WriteLine("Only x64 and ARM64 are planned targets.");

        // Match the runtime SONAMEs used by the selected backend. Desktop OpenGL
        // is required; an OpenGL ES-only installation cannot satisfy eglBindAPI.
        var libraries = LinuxNativeLibraryProbe.Check([
            LinuxGraphicsDependencies.Egl with { LibraryNames = ["libEGL.so.1"] },
            LinuxGraphicsDependencies.OpenGl with { LibraryNames = ["libGL.so.1", "libOpenGL.so.0"] },
            LinuxGraphicsDependencies.X11 with { LibraryNames = ["libX11.so.6"] },
        ]);
        foreach (var library in libraries)
            output.WriteLine($"{(library.IsAvailable ? "Available" : "Missing")}: {library.Diagnostic}");

        bool displayConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));
        output.WriteLine(displayConfigured ? "DISPLAY is set (connection not tested)." : "DISPLAY is missing; an X11 or XWayland session is required.");
        output.WriteLine("Native Wayland and Vulkan are not required for this backend.");
        output.WriteLine("These checks do not validate display access, the EGL driver, window rendering, or mail functionality.");
        return supportedArchitecture && displayConfigured && libraries.All(library => library.IsAvailable);
    }
}
