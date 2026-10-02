using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.UI;

namespace Broiler.Mail.Windows;

/// <summary>
/// Drives a gallery scenario through the same view-model commands a user would issue, one step
/// after the inbox becomes idle, so fixtures exercise real state transitions instead of injected state.
/// </summary>
internal sealed class DemoScenarioDriver
{
    private readonly MailShellViewModel _model;
    private readonly IUiDispatcher _dispatcher;
    private readonly Queue<Func<Task>> _steps;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _waiting;

    private DemoScenarioDriver(MailShellViewModel model, IUiDispatcher dispatcher, IEnumerable<Func<Task>> steps)
    {
        _model = model;
        _dispatcher = dispatcher;
        _steps = new(steps);
    }

    /// <summary>Completes after the last step has been issued and the inbox is idle again.</summary>
    public Task Completion => _completion.Task;

    public static DemoScenarioDriver Start(DemoOptions options, MailShellViewModel model, MailShellView shell, IUiDispatcher dispatcher)
    {
        shell.Navigation.SelectTab(options.InitialTab);
        var driver = new DemoScenarioDriver(model, dispatcher, Steps(options.Scenario, model));
        model.Inbox.Changed += driver.OnInboxChanged;
        dispatcher.Post(driver.RunNext);
        return driver;
    }

    private static IEnumerable<Func<Task>> Steps(DemoScenario scenario, MailShellViewModel model)
    {
        var inbox = model.Inbox;
        switch (scenario)
        {
            case DemoScenario.Inbox:
            case DemoScenario.LongMessage:
            case DemoScenario.BodyError:
                yield return inbox.ReceiveAsync;
                yield return () => Select(inbox, 55);
                break;
            case DemoScenario.Empty:
                yield return inbox.ReceiveAsync;
                break;
            case DemoScenario.LargeInbox:
                yield return inbox.ReceiveAsync;
                for (int page = 1; page * InboxViewModel.PageSize < InboxViewModel.MaximumLoadedMessages; page++)
                    yield return inbox.LoadOlderAsync;
                yield return () => inbox.Messages.Count > 0 ? inbox.SelectAsync(inbox.Messages[0].Key) : Task.CompletedTask;
                break;
            case DemoScenario.ReceiveError:
                yield return inbox.ReceiveAsync;
                yield return () => Select(inbox, 55);
                yield return inbox.ReceiveAsync;
                break;
            case DemoScenario.HtmlOnly:
                yield return inbox.ReceiveAsync;
                yield return () => Select(inbox, 54);
                break;
            case DemoScenario.SaveError:
                yield return () => model.Settings.SaveAsync();
                break;
            case DemoScenario.LargeDraft:
            case DemoScenario.SendUnknown:
                // The recovered draft is the fixture; the composer opens on it without any command.
                break;
        }
    }

    private static Task Select(InboxViewModel inbox, uint uid) =>
        inbox.Messages.FirstOrDefault(message => message.Key.Uid == uid) is { } message ? inbox.SelectAsync(message.Key) : Task.CompletedTask;

    private void OnInboxChanged(object? sender, EventArgs e)
    {
        if (!_waiting || _model.Inbox.IsBusy) return;
        _waiting = false;
        _dispatcher.Post(RunNext);
    }

    private void RunNext()
    {
        if (!_steps.TryDequeue(out var step))
        {
            _model.Inbox.Changed -= OnInboxChanged;
            _completion.TrySetResult();
            return;
        }
        _waiting = true;
        Task started;
        try { started = step(); }
        catch (Exception error) { Fail(error); return; }
        if (_waiting && !_model.Inbox.IsBusy)
        {
            // Steps without inbox work, such as a settings save, advance once their own task
            // ends; posting keeps the next step behind the result that task queued.
            _waiting = false;
            _ = started.ContinueWith(task =>
            {
                if (task.IsFaulted) Fail(task.Exception!);
                else try { _dispatcher.Post(RunNext); } catch (ObjectDisposedException) { /* The window has closed. */ }
            }, TaskScheduler.Default);
        }
        else _ = started.ContinueWith(task => Fail(task.Exception!), TaskContinuationOptions.OnlyOnFaulted);
    }

    private void Fail(Exception error)
    {
        _model.Inbox.Changed -= OnInboxChanged;
        _completion.TrySetException(error);
    }
}
