// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   4
// Annotated:        4/4
// Exempt:           4
// Human-reviewed:   0/4
// IP risk:          Low
// Security risk:    High
// Criteria:         4/4
// Resource impact:  8/10 max
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Diagnostics;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Preview;

/// <summary>
/// Shows one HTML preview window at a time, each on its own STA thread. A request for another
/// message closes the open window first; closing or replacing a preview that is still opening
/// cancels it before its window appears.
/// </summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=8; Fingerprint=5F4FCA
// Broiler-Falsified-If: a URI whose scheme is not http or https is passed to Process.Start with shell execution by the preview's open-external callback
// Broiler-Human:        PENDING
internal sealed class WindowsHtmlPreviewHost(Func<bool>? isDark = null) : IHtmlPreviewHost
{
    private const string Unavailable = "HTML preview unavailable. The text preview remains available.";
    private readonly object _gate = new();
    private HtmlPreviewWindow? _window;
    private Task _finished = Task.CompletedTask;
    private MailMessageKey? _current;
    private bool _disposed;
    private int _generation;

    public event EventHandler<HtmlPreviewChange>? Changed;

    public MailMessageKey? Current { get { lock (_gate) return _current; } }

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=8; Fingerprint=B20A8C
    // Broiler-Falsified-If: a Close or Dispose that runs before the preview thread publishes its window leaves that window open and shown
    // Broiler-Human:        PENDING
    public async Task<string> ShowAsync(MailMessageBody message)
    {
        if (message.HtmlText is null) return "No HTML is available for this message.";
        HtmlPreviewDocument document;
        try { document = HtmlPreviewPolicy.Create(message.HtmlText, message.EmbeddedImages); }
        catch (ArgumentException) { return "HTML exceeds the preview limits. The text preview remains available."; }
        Task previous;
        int generation;
        lock (_gate)
        {
            if (_disposed) return "Preview host is closed.";
            generation = ++_generation;
            previous = _finished;
            CloseWindowLocked();
        }

        // The previous window closes on its own thread; wait for it so two previews never overlap.
        await previous.ConfigureAwait(false);
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_disposed || generation != _generation) return "Preview canceled.";
            _current = message.Key;
            _finished = finished.Task;
        }

        var thread = new Thread(() =>
        {
            bool opened = false;
            try
            {
                string title = "Broiler.Mail — HTML snapshot — " + (message.Composition?.Subject ?? $"Message {message.Key.Uid}");
                using var window = new HtmlPreviewWindow(document, message.PlainText,
                    uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }),
                    message.HtmlText, message.EmbeddedImages, dark: isDark?.Invoke() == true, title: title);
                lock (_gate)
                {
                    if (!_disposed && generation == _generation)
                    {
                        _window = window;
                        _ = window.NativeHandle;
                    }
                }
                if (_window != window)
                {
                    Raise(message.Key, HtmlPreviewPhase.Canceled, "Preview canceled.");
                    ready.TrySetResult("Preview canceled.");
                    return;
                }
                window.Shown += async (_, _) =>
                {
                    await window.InitializeAsync();
                    bool loaded = await window.Loaded.Task;
                    string statusText = document.HasEmbeddedImages
                        ? "HTML preview open in its own window. Embedded inline images resolved; remote images blocked."
                        : (document.RemoteImageUrls.Count > 0
                            ? "HTML preview open in its own window. Remote images are blocked; select Load remote images there to display them."
                            : "HTML preview open in its own window. Images and active content are blocked.");
                    if (loaded)
                    {
                        opened = true;
                        Raise(message.Key, HtmlPreviewPhase.Open, statusText);
                    }
                    else Raise(message.Key, HtmlPreviewPhase.Unavailable, Unavailable);
                    ready.TrySetResult(loaded ? statusText : Unavailable);
                };
                window.Run();
            }
            catch (Exception)
            {
                Raise(message.Key, HtmlPreviewPhase.Unavailable, Unavailable);
                ready.TrySetResult(Unavailable);
            }
            finally
            {
                lock (_gate)
                {
                    _window = null;
                    _current = null;
                }
                if (opened) Raise(message.Key, HtmlPreviewPhase.Closed, "HTML preview closed. The text preview remains here.");
                ready.TrySetResult("HTML preview closed.");
                finished.TrySetResult();
            }
        }) { IsBackground = true, Name = "Broiler.Mail HTML preview" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await ready.Task.ConfigureAwait(false);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=2A0B85
    // Broiler-Falsified-If: a preview window already published in _window is never closed after Close is called
    // Broiler-Human:        PENDING
    public void Close()
    {
        lock (_gate)
        {
            _generation++;
            CloseWindowLocked();
        }
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=3163DB
    // Broiler-Falsified-If: a ShowAsync call made after Dispose opens a preview window
    // Broiler-Human:        PENDING
    public void Dispose() { lock (_gate) _disposed = true; Close(); }

    /// <summary>Passes a theme change to the open preview window, if any, which applies it on its own thread.</summary>
    public void ApplyTheme(StandardThemeTokens theme)
    {
        lock (_gate)
        {
            if (_window is { IsDisposed: false } window)
                try { window.ApplyTheme(theme); } catch (InvalidOperationException) { }
        }
    }

    private void CloseWindowLocked()
    {
        if (_window is { IsDisposed: false } window)
            try { window.CloseWindow(); } catch (InvalidOperationException) { }
    }

    // Listeners marshal to their own threads; a failing listener must not end the preview thread.
    private void Raise(MailMessageKey message, HtmlPreviewPhase phase, string text)
    {
        try { Changed?.Invoke(this, new HtmlPreviewChange(message, phase, text)); }
        catch (Exception) { }
    }
}
