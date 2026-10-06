// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   18
// Annotated:        0/18
// Exempt:           9
// Human-reviewed:   0/18
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       18
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Broiler.Mail.Core.Diagnostics;

/// <summary>The origin or mechanism through which the exception was intercepted.</summary>
public enum ExceptionSource
{
    /// <summary>An unhandled exception on an application thread (AppDomain.UnhandledException).</summary>
    Unhandled,

    /// <summary>An unobserved exception on a background task (TaskScheduler.UnobservedTaskException).</summary>
    UnobservedTask,

    /// <summary>A first-chance exception before any catch block runs (AppDomain.FirstChanceException).</summary>
    FirstChance,

    /// <summary>An exception explicitly intercepted and passed to the logger.</summary>
    Captured,
}

/// <summary>Context metadata provided to the exception filter.</summary>
public sealed record ExceptionFilterContext(
    ExceptionSource Source,
    string? CallerContext = null,
    bool IsTerminating = false);

/// <summary>
/// Global exception filter and handler that intercepts unhandled, unobserved, first-chance,
/// and captured exceptions, logging verbose diagnostic details to the current directory.
/// </summary>
public static class GlobalExceptionHandler
{
    private static readonly object SyncLock = new();
    private static int _installed;

    /// <summary>The file name for the log file. Defaults to "exceptions.log".</summary>
    public static string LogFileName { get; set; } = "exceptions.log";

    /// <summary>
    /// Optional override for the log directory. If null, <see cref="Environment.CurrentDirectory"/> is used.
    /// </summary>
    public static string? CustomLogDirectory { get; set; }

    /// <summary>Whether first-chance exceptions should be intercepted and filtered. Defaults to true.</summary>
    public static bool EnableFirstChanceLogging { get; set; } = true;

    /// <summary>
    /// Optional custom filter predicate. Returns true if the exception should be logged; false to ignore.
    /// When null, <see cref="DefaultFilter"/> is used.
    /// </summary>
    public static Func<Exception, ExceptionFilterContext, bool>? Filter { get; set; }

    /// <summary>Resolves the target file path where exceptions are logged.</summary>
    public static string LogFilePath =>
        Path.Combine(CustomLogDirectory ?? Environment.CurrentDirectory, LogFileName);

    /// <summary>
    /// Installs the global exception handlers on <see cref="AppDomain.CurrentDomain"/>
    /// and <see cref="TaskScheduler"/>.
    /// </summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1) return;

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    /// <summary>
    /// Uninstalls the global exception handlers.
    /// </summary>
    public static void Uninstall()
    {
        if (Interlocked.Exchange(ref _installed, 0) == 0) return;

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
    }

    /// <summary>
    /// Intercepts and logs an exception with verbose diagnostics.
    /// </summary>
    /// <param name="exception">The exception to log.</param>
    /// <param name="callerContext">Optional description of the location or operation.</param>
    /// <param name="source">The interception source.</param>
    /// <param name="isTerminating">Whether the exception causes process termination.</param>
    public static void LogException(
        Exception exception,
        string? callerContext = null,
        ExceptionSource source = ExceptionSource.Captured,
        bool isTerminating = false)
    {
        if (exception is null) return;

        var context = new ExceptionFilterContext(source, callerContext, isTerminating);
        if (!ShouldLog(exception, context)) return;

        string formatted = FormatVerbose(exception, context);
        WriteLog(formatted);
        MailLogger.Error("Exception", $"{context.Source} [Caller={context.CallerContext ?? "(none)"}]: {exception.GetType().FullName}: {exception.Message}", exception);
    }

    /// <summary>
    /// Evaluates whether an exception should be logged according to the active filter.
    /// </summary>
    public static bool ShouldLog(Exception exception, ExceptionFilterContext context)
    {
        if (Filter is not null)
        {
            try { return Filter(exception, context); }
            catch { return true; }
        }

        return DefaultFilter(exception, context);
    }

    /// <summary>
    /// Default filter implementation. Logs all unhandled, unobserved, and captured exceptions,
    /// while filtering benign/expected first-chance exceptions (such as routine task cancellations
    /// and assembly probing).
    /// </summary>
    public static bool DefaultFilter(Exception exception, ExceptionFilterContext context)
    {
        if (context.Source is ExceptionSource.Unhandled or ExceptionSource.UnobservedTask or ExceptionSource.Captured)
        {
            return true;
        }

        if (context.Source == ExceptionSource.FirstChance)
        {
            if (!EnableFirstChanceLogging) return false;

            // Filter routine cancellations that are handled normally
            if (exception is OperationCanceledException or TaskCanceledException)
            {
                return false;
            }

            // Filter harmless assembly probe failures from runtime assembly loader
            if (exception is FileNotFoundException fnf && fnf.Source?.StartsWith("System.Private.CoreLib") == true)
            {
                return false;
            }

            // Filter UIA_E_ELEMENTNOTAVAILABLE (0x80040201: The element is no longer available),
            // which is the standard Windows UI Automation COM status code returned when elements are destroyed or closed.
            if (exception is System.Runtime.InteropServices.COMException { HResult: unchecked((int)0x80040201) })
            {
                return false;
            }

            // Always log critical exceptions, COM exceptions, or exceptions from Broiler code
            if (exception is System.Runtime.InteropServices.COMException
                or NullReferenceException
                or AccessViolationException
                or OutOfMemoryException
                or InvalidCastException
                or IndexOutOfRangeException
                or ArgumentNullException)
            {
                return true;
            }

            if (exception.Source?.StartsWith("Broiler", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            // Log if any frame in the stack trace mentions Broiler
            string? trace = exception.StackTrace;
            if (trace is not null && trace.Contains("Broiler", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        return true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ??
            new InvalidOperationException($"Unhandled non-exception object: {e.ExceptionObject}");
        LogException(exception, "AppDomain.UnhandledException", ExceptionSource.Unhandled, e.IsTerminating);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception, "TaskScheduler.UnobservedTaskException", ExceptionSource.UnobservedTask);
    }

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        // Avoid recursive logging if writing the log itself threw a first-chance exception
        if (e.Exception.Data.Contains(LoggedKey)) return;

        var context = new ExceptionFilterContext(ExceptionSource.FirstChance, "FirstChance");
        if (ShouldLog(e.Exception, context))
        {
            try { e.Exception.Data[LoggedKey] = true; } catch { }
            string formatted = FormatVerbose(e.Exception, context);
            WriteLog(formatted);
            MailLogger.Error("Exception", $"FirstChance: {e.Exception.GetType().FullName}: {e.Exception.Message}", e.Exception);
        }
    }

    private const string LoggedKey = "__BroilerGlobalExceptionLogged";

    private static void WriteLog(string content)
    {
        lock (SyncLock)
        {
            try
            {
                string path = LogFilePath;
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(path, content, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                try
                {
                    Trace.WriteLine($"[GlobalExceptionHandler] Write failed: {ex.Message}");
                    Console.Error.WriteLine($"[GlobalExceptionHandler] Write failed: {ex.Message}");
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// Generates detailed and verbose diagnostic information for an exception.
    /// </summary>
    public static string FormatVerbose(Exception exception, ExceptionFilterContext context)
    {
        var sb = new StringBuilder();
        string title = context.IsTerminating ? "FATAL UNHANDLED EXCEPTION"
            : context.Source switch
            {
                ExceptionSource.Unhandled => "UNHANDLED EXCEPTION",
                ExceptionSource.UnobservedTask => "UNOBSERVED TASK EXCEPTION",
                ExceptionSource.FirstChance => "FIRST-CHANCE EXCEPTION",
                _ => "CAPTURED EXCEPTION",
            };

        sb.AppendLine(new string('=', 80));
        sb.AppendLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] [{title}]");
        sb.AppendLine(new string('-', 80));
        sb.AppendLine($"Context:            {context.CallerContext ?? context.Source.ToString()}");
        sb.AppendLine($"Source Mechanism:   {context.Source}");
        sb.AppendLine($"Is Terminating:     {context.IsTerminating}");
        sb.AppendLine($"Exception Type:     {exception.GetType().FullName}");
        sb.AppendLine($"HResult:            0x{exception.HResult:X8} ({exception.HResult})");
        sb.AppendLine($"Message:            {exception.Message}");
        sb.AppendLine($"Source Assembly:    {exception.Source ?? "(none)"}");
        sb.AppendLine($"Target Site:        {GetTargetSiteSafe(exception)}");
        sb.AppendLine($"Thread ID:          {Environment.CurrentManagedThreadId} (Name='{Thread.CurrentThread.Name ?? "unnamed"}', ThreadPool={Thread.CurrentThread.IsThreadPoolThread}, Background={Thread.CurrentThread.IsBackground})");
        sb.AppendLine($"Process:            PID={Environment.ProcessId}, Name={GetProcessName()}");
        sb.AppendLine($"Machine:            {Environment.MachineName}");
        sb.AppendLine($"OS Version:         {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
        sb.AppendLine($"CLR Version:        {Environment.Version}");
        sb.AppendLine($"Current Directory:  {Environment.CurrentDirectory}");
        sb.AppendLine($"App Base Directory: {AppContext.BaseDirectory}");

        if (exception.Data.Count > 0)
        {
            sb.AppendLine("Exception Data:");
            foreach (System.Collections.DictionaryEntry entry in exception.Data)
            {
                if (entry.Key is string s && s == LoggedKey) continue;
                sb.AppendLine($"  - {entry.Key}: {entry.Value}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Stack Trace:");
        sb.AppendLine(string.IsNullOrWhiteSpace(exception.StackTrace) ? "  [No stack trace available]" : exception.StackTrace);

        if (exception is AggregateException aggregate)
        {
            var flattened = aggregate.Flatten();
            sb.AppendLine();
            sb.AppendLine($"Flattened Aggregate Inner Exceptions ({flattened.InnerExceptions.Count}):");
            for (int i = 0; i < flattened.InnerExceptions.Count; i++)
            {
                FormatInnerException(sb, flattened.InnerExceptions[i], i + 1);
            }
        }
        else if (exception.InnerException is { } inner)
        {
            int depth = 1;
            for (Exception? current = inner; current is not null; current = current.InnerException)
            {
                sb.AppendLine();
                FormatInnerException(sb, current, depth++);
            }
        }

        sb.AppendLine(new string('=', 80));
        sb.AppendLine();
        return sb.ToString();
    }

    private static void FormatInnerException(StringBuilder sb, Exception inner, int depth)
    {
        sb.AppendLine($"--- Inner Exception {depth} ---");
        sb.AppendLine($"Type:        {inner.GetType().FullName}");
        sb.AppendLine($"HResult:     0x{inner.HResult:X8} ({inner.HResult})");
        sb.AppendLine($"Message:     {inner.Message}");
        sb.AppendLine($"Source:      {inner.Source ?? "(none)"}");
        sb.AppendLine($"Target Site: {GetTargetSiteSafe(inner)}");
        sb.AppendLine("Stack Trace:");
        sb.AppendLine(string.IsNullOrWhiteSpace(inner.StackTrace) ? "  [No stack trace available]" : inner.StackTrace);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Diagnostic logger best-effort method name capture; safely catches if trimmed or unreferenced.")]
    private static string GetTargetSiteSafe(Exception ex)
    {
        try
        {
            return ex.TargetSite?.ToString() ?? "(none)";
        }
        catch
        {
            return "(unavailable)";
        }
    }

    private static string GetProcessName()
    {
        try { return Process.GetCurrentProcess().ProcessName; }
        catch { return "(unknown)"; }
    }
}
