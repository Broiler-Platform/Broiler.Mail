using Broiler.Mail.Core.Messages;
using Broiler.UI;

namespace Broiler.Mail.Application.Preview;

/// <summary>Creates a message-scoped surface owned and disposed by the reader on its UI thread.</summary>
public interface IInlineHtmlPreviewHost
{
    /// <summary>Returns passive, resource-restricted HTML, or throws to retain the text reader.</summary>
    UiElement CreateInlineContent(MailMessageBody message);
}

/// <summary>Creates a reader-owned surface for a local original without a server message identity.</summary>
public interface ILocalOriginalHtmlHost
{
    /// <summary>Render passive HTML with external links and all image requests disabled.</summary>
    UiElement CreateLocalOriginalContent(string html);
}

/// <summary>Optional controls and recoverable managed failure notification for one surface.</summary>
public interface IInlineHtmlReader
{
    event EventHandler? ViewChanged;
    event EventHandler? RenderingFailed;
    double Zoom { get; }
    bool CanZoomIn { get; }
    bool CanZoomOut { get; }
    void ZoomBy(int direction);
    void ResetZoom();
}

/// <summary>Reader-scoped shortcuts, offered before ordinary control input.</summary>
public interface IInlineReaderInput
{
    bool HandleReaderInput(UiInputEvent input);
}

public sealed record RemoteImageState(int Total, int Loaded, bool IsLoading, bool CanLoad, string Status);

/// <summary>Explicit permission applies only to this reader surface; never load automatically.</summary>
public interface IInlineRemoteImages
{
    event EventHandler? RemoteImagesChanged;
    RemoteImageState RemoteImages { get; }
    Task LoadRemoteImagesAsync();
    void CancelRemoteImages();
}

/// <summary>Optional per-surface permission reset. Drops approved remote images and cancels pending fetches; never starts another request.</summary>
public interface IInlineRemoteImageReset
{
    void BlockRemoteImages();
}
