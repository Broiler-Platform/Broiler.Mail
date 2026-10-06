// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Hosting.Linux;
using Broiler.Mail.Core.Diagnostics;
using System.Text;

namespace Broiler.Mail.Cli;

internal static class Program
{
    private const string Usage = "Broiler.Mail.Cli [--help | --smoke-test | --diagnostics]";

    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        GlobalExceptionHandler.Install();
        if (args.SequenceEqual(["--help"]))
        {
            WriteHelp(Console.Out);
            return 0;
        }

        if (args.SequenceEqual(["--smoke-test"]))
        {
            try
            {
                SmokeTestRunner.Run();
                Console.WriteLine("Broiler.Mail: composition, inbox, and all three dialogs rendered successfully.");
                Console.WriteLine("This checks composition and UI rendering only; credentials, mail servers, and HTML process isolation are not validated.");
                return 0;
            }
            catch (Exception exception)
            {
                GlobalExceptionHandler.LogException(exception, "Cli.SmokeTest");
                Console.Error.WriteLine($"Smoke test failed: {exception.Message}");
                return 1;
            }
        }

        if (args.SequenceEqual(["--diagnostics"]))
        {
            if (!OperatingSystem.IsLinux())
            {
                Console.Error.WriteLine("Broiler.Mail.Linux requires Linux. Use --help for build-stage information.");
                return 1;
            }

            try
            {
                return LinuxBackendDiagnostics.Run(Console.Out) ? 0 : 1;
            }
            catch (Exception error)
            {
                GlobalExceptionHandler.LogException(error, "Cli.Diagnostics");
                Console.Error.WriteLine($"Linux backend diagnostics failed: {error.Message}");
                return 1;
            }
        }

        Console.Error.WriteLine("Usage: " + Usage);
        return 2;
    }

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("Broiler.Mail.Cli [--help | --smoke-test | --diagnostics]");
        output.WriteLine("Command-line interface for Broiler.Mail diagnostics and validation.");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine("  --help          Show help information and options.");
        output.WriteLine("  --smoke-test    Validate composition, inbox, and all dialogs headlessly.");
        output.WriteLine("  --diagnostics   Check Linux native libraries and X11/EGL display environment.");
    }
}
