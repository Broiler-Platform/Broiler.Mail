using Broiler.UI;

namespace Broiler.Mail.Windows.Hosting;

internal sealed class WindowsUiDispatcher(Func<Action, bool> postToWindow) : IUiDispatcher
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;

    public bool CheckAccess() => Environment.CurrentManagedThreadId == _threadId;

    public void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (CheckAccess())
            callback();
        else if (!postToWindow(callback))
            throw new ObjectDisposedException(nameof(WindowsMailWindow));
    }
}
