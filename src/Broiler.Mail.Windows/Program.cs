// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        2/2
// Exempt:           0
// Human-reviewed:   0/2
// IP risk:          Low
// Security risk:    High
// Criteria:         2/2
// Resource impact:  7/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Hosting.Windows;
using Broiler.Mail.Windows.Hosting;
using Broiler.Mail.Windows.Services;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows;

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=D26BF0
// Broiler-Falsified-If: a --smoke-test run creates or reads files under the default data directory instead of only a fresh directory under the temp path
// Broiler-Human:        PENDING
internal static class Program
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=366F93
    // Broiler-Falsified-If: an argument list other than none, --help, --demo, --smoke-test or --data-directory with a non-blank path starts the application instead of returning exit code 2
    // Broiler-Human:        PENDING
    [STAThread]
    private static int Main(string[] args)
    {
        bool smoke = args.SequenceEqual(["--smoke-test"]);
        bool demo = args.SequenceEqual(["--demo"]);
        bool customDirectory = args.Length == 2 && args[0] == "--data-directory" && !string.IsNullOrWhiteSpace(args[1]);
        if (args.SequenceEqual(["--help"]))
        {
            Console.WriteLine("Broiler.Mail.Windows [--demo | --smoke-test | --data-directory <path>]");
            return 0;
        }
        if (args.Length != 0 && !smoke && !demo && !customDirectory)
        {
            Console.Error.WriteLine("Usage: Broiler.Mail.Windows [--demo | --smoke-test | --data-directory <path>]");
            return 2;
        }

        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);

        try
        {
            if (smoke)
            {
                // The smoke check never reads or creates the user's application data.
                string isolatedPath = Path.Combine(Path.GetTempPath(), "Broiler.Mail.Smoke", Guid.NewGuid().ToString("N"));
                var smokeApplication = CompositionRoot.CreateApplication(isolatedPath);
                smokeApplication.InitializeAsync().GetAwaiter().GetResult();
                using var shell = smokeApplication.CreateShell();
                ShellSmokeCheck.Run(shell);
                Console.WriteLine("Broiler.Mail: composition and all four UI tabs rendered successfully.");
                Console.WriteLine("This checks composition and UI rendering only; credentials, mail servers, and HTML process isolation are not validated.");
                return 0;
            }

            string dataDirectory = customDirectory ? Path.GetFullPath(args[1]) : CompositionRoot.DefaultDataDirectory;
            var application = demo ? DemoApplication.Create() : CompositionRoot.CreateApplication(dataDirectory);
            // Before the window exists, complete initialization without changing the STA thread.
            application.InitializeAsync().GetAwaiter().GetResult();
            bool isDark = application.LoadedSettings.Theme switch
            {
                Broiler.Mail.Core.Settings.AppTheme.Dark => true,
                Broiler.Mail.Core.Settings.AppTheme.Light => false,
                _ => WindowsTheme.IsDarkThemePreferred()
            };
            StandardControlPaint.ApplyTheme(WindowsTheme.ResolveTheme(isDark));
            Console.WriteLine(demo ? "Demo mode: synthetic mail; no files, saved credentials, or network access." : $"Configuration directory: {dataDirectory}");
            using var window = new WindowsMailWindow(application, demo);
            return window.Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Broiler.Mail could not start: {exception.Message}");
            return 1;
        }
    }
}
