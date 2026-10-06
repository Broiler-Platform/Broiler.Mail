using Broiler.Mail.Core.Diagnostics;

namespace Broiler.Mail.Linux;

internal static class Program
{
    private static int Main(string[] args)
    {
        GlobalExceptionHandler.Install();
        // The Linux GUI mail window is not implemented yet.
        // For diagnostics and headless validation, use the Broiler.Mail.Cli application.
        return 0;
    }
}
