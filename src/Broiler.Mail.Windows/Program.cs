using Broiler.Mail.Windows.Hosting;
using Broiler.Mail.Windows.Services;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows;

internal static class Program
{
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
                Console.WriteLine("Account settings, protected passwords, IMAP reading, recoverable drafts, SMTP sending, and isolated HTML preview are available.");
                return 0;
            }

            string dataDirectory = customDirectory ? Path.GetFullPath(args[1]) : CompositionRoot.DefaultDataDirectory;
            var application = demo ? DemoApplication.Create() : CompositionRoot.CreateApplication(dataDirectory);
            // Before the window exists, complete initialization without changing the STA thread.
            application.InitializeAsync().GetAwaiter().GetResult();
            StandardControlPaint.ApplyTheme(WindowsTheme.Resolve(application.LoadedSettings.Theme));
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
