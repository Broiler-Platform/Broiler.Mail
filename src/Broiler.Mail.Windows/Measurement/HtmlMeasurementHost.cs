using System.Runtime.InteropServices;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Preview;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Measurement;

/// <summary>Owns the standalone HTML window used by explicit demo benchmarks.</summary>
internal sealed class HtmlMeasurementHost(Func<StandardThemeTokens?> currentTheme) : IDisposable
{
    private readonly object _gate = new();
    private HtmlPreviewWindow? _window;
    private Task _finished = Task.CompletedTask;
    private StandardThemeTokens? _theme;
    private bool _disposed;
    private int _generation;

    internal HtmlPreviewWindow? Window { get { lock (_gate) return _window; } }

    internal async Task<HtmlPreviewWindow?> OpenAsync(MailMessageBody message, PreviewMeasurement? measurement = null)
    {
        if (message.HtmlText is not { } html) return null;
        var document = HtmlPreviewPolicy.Create(html, message.EmbeddedImages);
        Task previous;
        int generation;
        lock (_gate)
        {
            if (_disposed) return null;
            generation = ++_generation;
            previous = _finished;
            CloseWindow();
        }

        await previous.ConfigureAwait(false);
        var ready = new TaskCompletionSource<HtmlPreviewWindow?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_disposed || generation != _generation) return null;
            _finished = finished.Task;
        }

        try
        {
            var thread = new Thread(() => Run()) { IsBackground = true, Name = "Broiler.Mail HTML measurement" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        catch
        {
            finished.TrySetResult();
            throw;
        }
        return await ready.Task.ConfigureAwait(false);

        void Run()
        {
            try
            {
                StandardThemeTokens? theme;
                lock (_gate) theme = _theme;
                theme ??= currentTheme();
                using var window = new HtmlPreviewWindow(document, message.PlainText, _ => { },
                    theme: theme, title: "Broiler.Mail HTML measurement", measurement: measurement);
                lock (_gate)
                {
                    if (_disposed || generation != _generation) return;
                    _window = window;
                    _ = window.NativeHandle;
                    if (_theme is { } latest && !ReferenceEquals(latest, theme)) window.ApplyTheme(latest);
                }
                window.Shown += async (_, _) =>
                {
                    try
                    {
                        await window.InitializeAsync();
                        ready.TrySetResult(await window.Loaded.Task ? window : null);
                    }
                    catch (Exception error) { ready.TrySetException(error); }
                };
                window.Run();
            }
            catch (Exception error) { ready.TrySetException(error); }
            finally
            {
                lock (_gate) _window = null;
                ready.TrySetResult(null);
                finished.TrySetResult();
            }
        }
    }

    internal void ApplyTheme(StandardThemeTokens theme)
    {
        lock (_gate)
        {
            _theme = theme;
            if (_window is { IsDisposed: false } window)
                try { window.ApplyTheme(theme); } catch (InvalidOperationException) { }
        }
    }

    internal void Close()
    {
        lock (_gate)
        {
            _generation++;
            CloseWindow();
        }
    }

    private void CloseWindow()
    {
        if (_window is { IsDisposed: false } window)
            try { window.CloseWindow(); } catch (Exception error) when (error is InvalidOperationException or COMException) { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            CloseWindow();
        }
    }
}
