// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text;

namespace Broiler.Mail.Core.Diagnostics;

/// <summary>
/// Thread-safe diagnostic file logger that records detailed operational logs,
/// HTML preview events, image processing details, and diagnostic information
/// to Broiler.Mail.log in the application directory.
/// </summary>
public static class MailLogger
{
    private static readonly object SyncLock = new();

    /// <summary>Log file name. Defaults to "Broiler.Mail.log".</summary>
    public static string LogFileName { get; set; } = "Broiler.Mail.log";

    /// <summary>Optional custom log directory. If null, current directory or base directory is used.</summary>
    public static string? CustomLogDirectory { get; set; }

    /// <summary>Minimum log level to record. Defaults to Info.</summary>
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    /// <summary>Resolves the current log file path.</summary>
    public static string LogFilePath
    {
        get
        {
            string dir = CustomLogDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrWhiteSpace(dir)) dir = Environment.CurrentDirectory;
            return Path.Combine(dir, LogFileName);
        }
    }

    /// <summary>Records an informational message.</summary>
    public static void Info(string category, string message) =>
        Log(LogLevel.Info, category, message);

    /// <summary>Records a warning message.</summary>
    public static void Warning(string category, string message, Exception? exception = null) =>
        Log(LogLevel.Warn, category, message, exception);

    /// <summary>Records an error message.</summary>
    public static void Error(string category, string message, Exception? exception = null) =>
        Log(LogLevel.Error, category, message, exception);

    /// <summary>Records a debug diagnostic message.</summary>
    public static void Debug(string category, string message) =>
        Log(LogLevel.Debug, category, message);

    /// <summary>Writes a formatted entry to the log file.</summary>
    public static void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        if (level < MinimumLevel) return;

        var sb = new StringBuilder();
        sb.Append('[').Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz")).Append("] ");
        sb.Append('[').Append(level.ToString().ToUpperInvariant().PadRight(5)).Append("] ");
        sb.Append('[').Append(category).Append("] ");
        sb.Append(message);

        if (exception is not null)
        {
            sb.AppendLine();
            sb.Append("    Exception: ").Append(exception.GetType().FullName).Append(": ").Append(exception.Message);
            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                sb.AppendLine();
                sb.Append("    Stack: ").Append(exception.StackTrace.Trim());
            }
        }

        string line = sb.ToString();

        lock (SyncLock)
        {
            try
            {
                string targetPath = LogFilePath;
                string? dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(targetPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Fallback attempt to Environment.CurrentDirectory if AppDomain directory was not writable
                try
                {
                    string fallbackPath = Path.Combine(Environment.CurrentDirectory, LogFileName);
                    File.AppendAllText(fallbackPath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Diagnostics must never throw
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"[Broiler.Mail] {line}");
    }
}

/// <summary>Severity levels for diagnostic logging.</summary>
public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3
}
