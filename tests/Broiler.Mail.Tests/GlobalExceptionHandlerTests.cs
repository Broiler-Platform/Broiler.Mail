// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Mail.Core.Diagnostics;
using Xunit;

namespace Broiler.Mail.Tests;

public sealed class GlobalExceptionHandlerTests : IDisposable
{
    private readonly string _tempDir;

    public GlobalExceptionHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BroilerExceptionTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        GlobalExceptionHandler.CustomLogDirectory = _tempDir;
        GlobalExceptionHandler.LogFileName = "test_exceptions.log";
        GlobalExceptionHandler.Filter = null;
        GlobalExceptionHandler.EnableFirstChanceLogging = true;
    }

    public void Dispose()
    {
        GlobalExceptionHandler.Uninstall();
        GlobalExceptionHandler.CustomLogDirectory = null;
        GlobalExceptionHandler.LogFileName = "exceptions.log";
        GlobalExceptionHandler.Filter = null;
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void LogException_WritesVerboseExceptionDetailsToFile()
    {
        try
        {
            throw new InvalidOperationException("Detailed test message with state.")
            {
                Data = { ["UserId"] = 12345, ["Action"] = "SelectMail" }
            };
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.LogException(ex, "TestContext.SelectMail");
        }

        string logPath = GlobalExceptionHandler.LogFilePath;
        Assert.True(File.Exists(logPath));

        string content = File.ReadAllText(logPath);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("Detailed test message with state.", content);
        Assert.Contains("TestContext.SelectMail", content);
        Assert.Contains("UserId: 12345", content);
        Assert.Contains("Action: SelectMail", content);
        Assert.Contains("Stack Trace:", content);
        Assert.Contains("Thread ID:", content);
        Assert.Contains("Process:", content);
        Assert.Contains("OS Version:", content);
    }

    [Fact]
    public void LogException_UnwrapsInnerAndAggregateExceptions()
    {
        var inner1 = new ArgumentNullException("paramA", "Param cannot be null");
        var inner2 = new FormatException("Format is invalid");
        var aggregate = new AggregateException("Aggregate failure", inner1, inner2);

        GlobalExceptionHandler.LogException(aggregate, "BatchOperation");

        string logPath = GlobalExceptionHandler.LogFilePath;
        Assert.True(File.Exists(logPath));

        string content = File.ReadAllText(logPath);
        Assert.Contains("Aggregate failure", content);
        Assert.Contains("ArgumentNullException", content);
        Assert.Contains("Param cannot be null", content);
        Assert.Contains("FormatException", content);
        Assert.Contains("Format is invalid", content);
        Assert.Contains("Flattened Aggregate Inner Exceptions (2)", content);
    }

    [Fact]
    public void CustomFilter_CanSuppressOrAllowExceptions()
    {
        // Filter out exceptions with message containing "IgnoreMe"
        GlobalExceptionHandler.Filter = (ex, ctx) => !ex.Message.Contains("IgnoreMe");

        var ignored = new InvalidOperationException("Please IgnoreMe now");
        var logged = new InvalidOperationException("Keep this one");

        GlobalExceptionHandler.LogException(ignored, "IgnoredContext");
        GlobalExceptionHandler.LogException(logged, "LoggedContext");

        string logPath = GlobalExceptionHandler.LogFilePath;
        Assert.True(File.Exists(logPath));

        string content = File.ReadAllText(logPath);
        Assert.DoesNotContain("Please IgnoreMe now", content);
        Assert.Contains("Keep this one", content);
    }

    [Fact]
    public void DefaultFilter_SuppressesCancellationsInFirstChanceMode()
    {
        var cancelEx = new OperationCanceledException("Operation was canceled");
        var context = new ExceptionFilterContext(ExceptionSource.FirstChance);

        bool shouldLog = GlobalExceptionHandler.DefaultFilter(cancelEx, context);
        Assert.False(shouldLog);
    }

    [Fact]
    public void DefaultFilter_HandlesComExceptionsInFirstChanceMode()
    {
        var uiaEx = new System.Runtime.InteropServices.COMException("Element not available", unchecked((int)0x80040201));
        var genericComEx = new System.Runtime.InteropServices.COMException("Unspecified failure", unchecked((int)0x80004005));
        var context = new ExceptionFilterContext(ExceptionSource.FirstChance);

        Assert.False(GlobalExceptionHandler.DefaultFilter(uiaEx, context));
        Assert.True(GlobalExceptionHandler.DefaultFilter(genericComEx, context));
    }

    [Fact]
    public void ConcurrentLogging_DoesNotThrowAndPreservesEntries()
    {
        const int count = 20;
        Parallel.For(0, count, i =>
        {
            var ex = new InvalidOperationException($"Concurrent exception #{i}");
            GlobalExceptionHandler.LogException(ex, $"ThreadContext_{i}");
        });

        string logPath = GlobalExceptionHandler.LogFilePath;
        Assert.True(File.Exists(logPath));

        string content = File.ReadAllText(logPath);
        for (int i = 0; i < count; i++)
        {
            Assert.Contains($"Concurrent exception #{i}", content);
            Assert.Contains($"ThreadContext_{i}", content);
        }
    }

    [Fact]
    public void InstallAndUninstall_AreIdempotent()
    {
        GlobalExceptionHandler.Install();
        GlobalExceptionHandler.Install(); // Second call is safe

        GlobalExceptionHandler.Uninstall();
        GlobalExceptionHandler.Uninstall(); // Second call is safe
    }

    [Fact]
    public void MailLogger_WritesEntriesWithTimestampAndLevel()
    {
        MailLogger.CustomLogDirectory = _tempDir;
        MailLogger.LogFileName = "Broiler.Mail.log";

        MailLogger.Info("Preview", "Starting image render pipeline.");
        MailLogger.Warning("Preview", "Suspicious element encountered.");
        MailLogger.Error("Preview", "Failed to parse malformed CID.", new FormatException("Invalid hex character"));

        string logPath = MailLogger.LogFilePath;
        Assert.True(File.Exists(logPath));

        string content = File.ReadAllText(logPath);
        Assert.Contains("[INFO ] [Preview] Starting image render pipeline.", content);
        Assert.Contains("[WARN ] [Preview] Suspicious element encountered.", content);
        Assert.Contains("[ERROR] [Preview] Failed to parse malformed CID.", content);
        Assert.Contains("FormatException: Invalid hex character", content);

        MailLogger.CustomLogDirectory = null;
    }
}
