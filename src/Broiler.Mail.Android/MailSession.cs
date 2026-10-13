using Broiler.Graphics.RenderList;
using Broiler.Mail.Application;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Android;

/// <summary>The single-surface mail UI, independent of the Android activity lifetime.</summary>
internal sealed class MailSession : IDisposable
{
    private readonly StandardQueuedUiDispatcher _dispatcher;
    private readonly AppearanceController _appearance;
    private readonly MailKeyboardNavigation _keyboard;
    private bool _disposed;

    public MailSession(MailApplication application, IUiHost host, Action<Action> post)
    {
        // Always enqueue: inline dispatch can reenter layout while controls are changing.
        _dispatcher = new StandardQueuedUiDispatcher(() => { if (!_disposed) post(Drain); });
        Session = new StandardUiSessionBuilder().WithDispatcher(_dispatcher).Build(host);
        Model = application.CreateViewModel(_dispatcher);
        StandardControlPaint.ApplyTheme(AppearancePolicy.Resolve(Model.Settings.Settings.Theme,
            (host as IUiSystemSettingsHost)?.Settings ?? UiSystemSettings.Default));
        Shell = new MailShellView(Model);
        Shell.Attach(Session);
        _keyboard = Shell.CreateKeyboardNavigation(Session);
        _appearance = new AppearanceController(Session, Model.Settings, host as IUiSystemSettingsHost);
        Session.SetFocus(Shell.NavigationFocus);
    }

    public UiSession Session { get; }
    public MailShellView Shell { get; }
    public MailShellViewModel Model { get; }

    private void Drain()
    {
        if (!_disposed) _dispatcher.Drain();
    }

    public BRenderList? RenderFrame()
    {
        if (_disposed) return null;
        Drain();
        return Session.RenderFrame();
    }

    public void Dispatch(UiInputEvent input)
    {
        if (_disposed) return;
        if (!_keyboard.Handle(input)) Session.DispatchInput(input);
    }

    public bool TryGoBack()
    {
        if (Shell.Menu.IsOpen)
        {
            Shell.Menu.Close();
            Session.ReleaseInputCapture(Shell.Menu);
            return true;
        }
        if (Shell.ActiveDialog is not null || Model.Inbox.IsBusy || Model.Account.IsBusy)
            return _keyboard.Execute(MailCommand.BackOrCancel);
        return Shell.Inbox.GoBackToList();
    }

    // OnPause must not mark the composer as closing: the user may return to it.
    public Task<bool> SaveDraftAsync() => Model.Composer.SaveAsync();
    public Task<bool> PrepareCloseAsync() => Shell.PrepareCloseAsync();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _appearance.Dispose();
        Shell.Dispose();
        Session.Dispose();
    }
}
