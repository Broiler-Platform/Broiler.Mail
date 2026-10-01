using Broiler.Mail.Linux.Hosting;

namespace Broiler.Mail.Linux;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.SequenceEqual(["--help"]))
        {
            Console.WriteLine("Broiler.Mail.Linux [--help | --diagnostics]");
            Console.WriteLine("Linux project foundation using Broiler.Graphics X11/EGL.");
            Console.WriteLine("--diagnostics checks native libraries and the display environment without opening a window.");
            Console.WriteLine("The interactive mail window is not implemented yet.");
            return 0;
        }

        if (args.Length != 0 && !args.SequenceEqual(["--diagnostics"]))
        {
            Console.Error.WriteLine("Usage: Broiler.Mail.Linux [--help | --diagnostics]");
            return 2;
        }

        // Help and argument validation remain usable when cross-building on another OS.
        // Native library probing must only run on Linux.
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("Broiler.Mail.Linux requires Linux. Use --help for build-stage information.");
            return 1;
        }

        if (args.Length == 0)
        {
            Console.Error.WriteLine("The Linux mail window is not implemented yet. Use --diagnostics to check X11/EGL prerequisites.");
            return 2;
        }

        try
        {
            return LinuxBackendDiagnostics.Run(Console.Out) ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Linux backend diagnostics failed: {error.Message}");
            return 1;
        }
    }
}
