// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   12
// Annotated:        0/12
// Exempt:           5
// Human-reviewed:   0/12
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       12
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.RichEdit.Standard;

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
        shell.ShowView(options.InitialView);
        var driver = new DemoScenarioDriver(model, dispatcher, Steps(options.Scenario, model, shell));
        model.Inbox.Changed += driver.OnInboxChanged;
        dispatcher.Post(driver.RunNext);
        return driver;
    }

    private static IEnumerable<Func<Task>> Steps(DemoScenario scenario, MailShellViewModel model, MailShellView shell)
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
            // A second receive while reading: it fails, or new mail arrives above the open message.
            case DemoScenario.ReceiveError:
            case DemoScenario.NewMail:
                yield return inbox.ReceiveAsync;
                yield return () => Select(inbox, 55);
                yield return inbox.ReceiveAsync;
                break;
            case DemoScenario.ReceiveCanceled:
                yield return inbox.ReceiveAsync;
                yield return () => Select(inbox, 55);
                yield return () =>
                {
                    // Canceled before the server answers, as the Cancel button or Escape does.
                    var receiving = inbox.ReceiveAsync();
                    inbox.Cancel();
                    return receiving;
                };
                break;
            case DemoScenario.LoadError:
                yield return inbox.ReceiveAsync;
                yield return () => Select(inbox, 55);
                yield return inbox.LoadOlderAsync;
                break;
            case DemoScenario.HtmlOnly:
            case DemoScenario.LongHtml:
                yield return inbox.ReceiveAsync;
                yield return () => Select(inbox, 54);
                break;
            case DemoScenario.SaveError:
                yield return () => model.Settings.SaveAsync();
                break;
            case DemoScenario.InvalidSetup:
                yield return () =>
                {
                    // Typed into the field, as a user would; the view copies fields to the model, not back.
                    Field(shell, "account", "Email address").Text = "Demo Reader <reader@example.test>";
                    return model.Account.SaveAsync();
                };
                break;
            case DemoScenario.TestCanceled:
                yield return () =>
                {
                    var test = model.Account.TestConnectionAsync();
                    model.Account.CancelConnectionTest();
                    return test;
                };
                break;
            case DemoScenario.SmtpTestFailed:
            case DemoScenario.SmtpTestPassed:
                // The same command as Test SMTP sign-in; the demo tester answers without a network.
                yield return () => model.Account.TestOutgoingConnectionAsync();
                break;
            case DemoScenario.DraftConflict:
                yield return () =>
                {
                    var body = Descendants(Tab(shell, "compose")).OfType<StandardRichEdit>().Single();
                    body.SetPlainText(body.GetPlainText() + "\n\nOne more line typed after another window saved this draft.");
                    return model.Composer.SaveAsync();
                };
                break;
            case DemoScenario.SendRejected:
                yield return () => model.Composer.SendAsync();
                break;
            case DemoScenario.DraftInvalid:
                yield return () =>
                {
                    // A recipient typed without its @, then checked, as a user would.
                    Field(shell, "compose", "To").Text = "team.example.test";
                    model.Composer.CheckDraft();
                    return Task.CompletedTask;
                };
                break;
            case DemoScenario.LargeDraft:
            case DemoScenario.SendUnknown:
            case DemoScenario.SentCopyFailed:
                // The recovered draft is the fixture; the composer opens on it without any command.
                break;
        }
    }

    private static UiElement Tab(MailShellView shell, string id) => shell.GetContent(id);

    private static StandardEdit Field(MailShellView shell, string tab, string label) =>
        Descendants(Tab(shell, tab)).OfType<StandardLabel>().Where(item => item.Text == label).Select(item => item.Target).OfType<StandardEdit>().Single();

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
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
