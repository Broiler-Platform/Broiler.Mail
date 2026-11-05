// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   12
// Annotated:        4/12
// Exempt:           8
// Human-reviewed:   0/12
// IP risk:          Low
// Security risk:    High
// Criteria:         4/4
// Resource impact:  8/10 max
// Unverified:       12
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Diagnostics;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Preview;

/// <summary>Creates inline reader surfaces owned by the inbox or saved originals view.</summary>
internal sealed class WindowsHtmlPreviewHost(Func<StandardThemeTokens?>? currentTheme = null) : IInlineHtmlPreviewHost, ILocalOriginalHtmlHost, IDisposable
{
    internal Func<Broiler.Graphics.Rendering.IBroilerRenderer?> RendererProvider { get; set; } = () => null;
    internal Func<double> ScaleProvider { get; set; } = () => 1;
    private WeakReference<ScrollableHtmlView>? _inlineSurface;
    private WeakReference<ScrollableHtmlView>? _localSurface;
    internal Func<RemoteImageBroker> CreateImageBroker { get; set; } = () => new RemoteImageBroker();

    public Broiler.UI.UiElement CreateInlineContent(MailMessageBody message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var document = HtmlPreviewPolicy.Create(message.HtmlText ?? throw new ArgumentException("No HTML is available."), message.EmbeddedImages);
        var surface = new ScrollableHtmlView(document.Html, RendererProvider, target =>
        {
            if (HtmlPreviewPolicy.TryExternalLink(target, out var uri) && document.ExternalLinks.Contains(uri!.AbsoluteUri))
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }, ScaleProvider, target => HtmlPreviewPolicy.TryExternalLink(target, out var uri) && document.ExternalLinks.Contains(uri!.AbsoluteUri), recoverManagedFailures: true);
        surface.ApplyReaderTheme(ReaderTheme());
        surface.ConfigureRemoteImages(message.HtmlText!, message.EmbeddedImages, document.RemoteImageUrls.Count, CreateImageBroker);
        _inlineSurface = new(surface);
        return surface;
    }

    public Broiler.UI.UiElement CreateLocalOriginalContent(string html)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (html.Length > Broiler.Mail.Core.Services.LocalOriginalPreview.MaximumHtmlCharacters)
            throw new ArgumentException("HTML exceeds the local original preview limit.", nameof(html));
        var document = HtmlPreviewPolicy.Create(html, embeddedImages: null, textAndFormattingOnly: true);
        var surface = new ScrollableHtmlView(document.Html, RendererProvider, _ => { }, ScaleProvider,
            canOpenLink: _ => false, recoverManagedFailures: true);
        surface.Scroll.AccessibleName = "Local original HTML";
        surface.Scroll.FocusWhenScrollable = true;
        surface.ApplyReaderTheme(ReaderTheme());
        _localSurface = new(surface);
        return surface;
    }

    private StandardThemeTokens ReaderTheme()
    {
        var current = currentTheme?.Invoke();
        return _theme ?? current ?? StandardThemeTokens.Light;
    }

    private StandardThemeTokens? _theme;
    private bool _disposed;

    public void ApplyTheme(StandardThemeTokens theme)
    {
        _theme = theme;
        ApplySurfaceTheme(_inlineSurface, theme);
        ApplySurfaceTheme(_localSurface, theme);
    }

    public void Dispose()
    {
        _disposed = true;
        _inlineSurface = null;
        _localSurface = null;
    }

    private static void ApplySurfaceTheme(WeakReference<ScrollableHtmlView>? reference, StandardThemeTokens theme)
    {
        if (reference is null || !reference.TryGetTarget(out var surface) || surface.IsDisposed) return;
        void UpdateTheme() { if (!surface.IsDisposed) surface.ApplyReaderTheme(theme); }
        if (surface.Session is { } session) session.Dispatcher.Post(UpdateTheme); else UpdateTheme();
    }
}
