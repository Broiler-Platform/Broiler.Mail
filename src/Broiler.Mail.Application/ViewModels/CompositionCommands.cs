using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Application.ViewModels;

/// <summary>Where keyboard focus belongs after a composition request.</summary>
public enum CompositionFocus { Recipients, Body }

/// <summary>Raised when the composer should be shown, whether a draft was started or an existing one is kept.</summary>
public sealed class CompositionRequestedEventArgs(CompositionKind? kind, bool started, CompositionFocus focus) : EventArgs
{
    /// <summary>Null for a new message.</summary>
    public CompositionKind? Kind { get; } = kind;
    /// <summary>False when an existing draft was retained instead of starting another.</summary>
    public bool Started { get; } = started;
    public CompositionFocus Focus { get; } = focus;
}

/// <summary>
/// New, Reply, Reply all, and Forward, shared by the reader and the composer so both start drafts
/// identically. An existing draft is never replaced: the request reveals it, and the composer's
/// status explains that it was retained.
/// </summary>
public sealed class CompositionCommands
{
    private readonly ComposerViewModel _composer;
    private readonly InboxViewModel _inbox;

    public CompositionCommands(ComposerViewModel composer, InboxViewModel inbox)
    {
        _composer = composer;
        _inbox = inbox;
        composer.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        inbox.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the composer or the open message changes command availability.</summary>
    public event EventHandler? Changed;
    public event EventHandler<CompositionRequestedEventArgs>? Requested;

    /// <summary>True when a draft can be started, or when an existing draft can be revealed instead.</summary>
    public bool CanCompose => _composer.CanStart || _composer.HasDraft;
    /// <summary>Reply and Forward also need the open message's loaded reply headers.</summary>
    public bool CanRespond => CanCompose && !_inbox.IsBusy && _inbox.Body?.Composition is not null;

    public bool StartNew() => Start(null);
    public bool Respond(CompositionKind kind) => Start(kind);

    /// <summary>
    /// True while a request starts a draft and before <see cref="Requested"/> has moved focus to it,
    /// so the composer does not first hand focus on from the start buttons it hides.
    /// </summary>
    internal bool IsStarting { get; private set; }

    private bool Start(CompositionKind? kind)
    {
        if (kind is null ? !CanCompose : !CanRespond) return false;
        bool started;
        IsStarting = true;
        try { started = kind is null ? _composer.StartNew() : _composer.StartFromMessage(_inbox.Body!, kind.Value); }
        finally { IsStarting = false; }
        if (!started && !_composer.HasDraft) return false;
        // Replies already have recipients, so writing starts in the body; an existing draft resumes there too.
        var focus = started && kind is null or CompositionKind.Forward ? CompositionFocus.Recipients : CompositionFocus.Body;
        Requested?.Invoke(this, new(kind, started, focus));
        return started;
    }
}
