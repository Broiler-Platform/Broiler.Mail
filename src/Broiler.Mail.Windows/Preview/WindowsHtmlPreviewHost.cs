using System.Diagnostics;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Preview;

namespace Broiler.Mail.Windows.Preview;

internal sealed class WindowsHtmlPreviewHost : IHtmlPreviewHost
{
    private readonly object _gate = new();
    private HtmlPreviewWindow? _window;
    private bool _active, _disposed;
    private int _generation;

    public Task<string> ShowAsync(MailMessageBody message)
    {
        if (message.HtmlText is null) return Task.FromResult("No HTML is available for this message.");
        HtmlPreviewDocument document;
        try { document = HtmlPreviewPolicy.Create(message.HtmlText, message.EmbeddedImages); }
        catch (ArgumentException) { return Task.FromResult("HTML exceeds the preview limits. The text preview remains available."); }
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int generation;
        lock (_gate)
        {
            if (_disposed) return Task.FromResult("Preview host is closed.");
            if (_active) return Task.FromResult("Close the existing HTML preview before opening another.");
            _active = true; generation = ++_generation;
        }
        var thread = new Thread(() =>
        {
            try
            {
                using var window = new HtmlPreviewWindow(document, message.PlainText,
                    uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }),
                    message.HtmlText, message.EmbeddedImages);
                lock (_gate)
                {
                    if (_disposed || generation != _generation) { ready.TrySetResult("Preview canceled."); return; }
                    _window = window;
                    _ = window.NativeHandle;
                }
                string title = "Broiler.Mail — HTML snapshot — " + (message.Composition?.Subject ?? $"Message {message.Key.Uid}");
                window.SetTitle(title);
                window.Shown += async (_, _) =>
                {
                    await window.InitializeAsync();
                    bool loaded = await window.Loaded.Task;
                    string statusText = document.HasEmbeddedImages
                        ? "HTML preview opened. Embedded inline images resolved; remote images blocked."
                        : (document.RemoteImageUrls.Count > 0
                            ? "HTML preview opened. Remote images are blocked; select Load remote images to display them."
                            : "HTML preview opened. Images and active content are blocked.");
                    ready.TrySetResult(loaded ? statusText : "HTML preview unavailable. Text remains available.");
                };
                window.Run();
            }
            catch (Exception) { ready.TrySetResult("HTML preview unavailable. Text remains available."); }
            finally
            {
                lock (_gate) { _window = null; _active = false; }
                ready.TrySetResult("HTML preview closed.");
            }
        }) { IsBackground = true, Name = "Broiler.Mail HTML preview" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    }

    public void Close()
    {
        lock (_gate)
        {
            _generation++;
            if (_window is { IsDisposed: false } window)
                try { window.CloseWindow(); } catch (InvalidOperationException) { }
        }
    }

    public void Dispose() { lock (_gate) _disposed = true; Close(); }
}
