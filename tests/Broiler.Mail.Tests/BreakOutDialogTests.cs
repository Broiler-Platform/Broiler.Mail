// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class BreakOutDialogTests
{
    [Fact]
    public void DialogBreaksOutAutomaticallyWhenHostSupportsWindowHosting()
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var dispatcher = new TestQueueDispatcher();
        var receiver = new TestMailReceiver();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher),
            new ComposerViewModel(dispatcher: dispatcher));
        var shell = new MailShellView(model);
        var host = new TestWindowHost(1100, 720);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
        shell.Attach(session);

        Assert.Null(shell.ActiveDialog);

        shell.ShowView("compose");

        Assert.NotNull(shell.ActiveDialog);
        Assert.True(shell.ActiveDialog.IsBrokenOut);
        Assert.Single(host.Requests);
        Assert.Equal(UiHostWindowChrome.Owner, host.Requests[0].Chrome);
        Assert.True(host.HostWindows[0].Activated);
        Assert.NotNull(shell.ActiveDialog.Session);
        Assert.NotSame(session, shell.ActiveDialog.Session);
        Assert.Same(shell.ActiveDialog.Session, host.HostWindows[0].BoundSession);
        Assert.NotNull(shell.ActiveDialog.Session.FocusedElement);
        Assert.True(shell.ActiveDialog.Session.FocusedElement.IsDescendantOf(shell.ActiveDialog));

        shell.CloseDialog();
        Assert.Null(shell.ActiveDialog);
    }

    [Fact]
    public void AccountDialogBreaksOutAutomaticallyWhenHostSupportsWindowHosting()
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var dispatcher = new TestQueueDispatcher();
        var receiver = new TestMailReceiver();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher),
            new ComposerViewModel(dispatcher: dispatcher));
        var shell = new MailShellView(model);
        var host = new TestWindowHost(1100, 720);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
        shell.Attach(session);

        shell.ShowView("account");

        Assert.NotNull(shell.ActiveDialog);
        Assert.True(shell.ActiveDialog.IsBrokenOut);
        Assert.Single(host.Requests);
        Assert.Equal(UiHostWindowChrome.Owner, host.Requests[0].Chrome);
        Assert.True(host.HostWindows[0].Activated);
        Assert.NotNull(shell.ActiveDialog.Session);
        Assert.NotSame(session, shell.ActiveDialog.Session);

        shell.CloseDialog();
        Assert.Null(shell.ActiveDialog);
    }

    [Fact]
    public void DialogRemainsSubwindowWhenHostDoesNotSupportWindowHosting()
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var dispatcher = new TestQueueDispatcher();
        var receiver = new TestMailReceiver();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher),
            new ComposerViewModel(dispatcher: dispatcher));
        var shell = new MailShellView(model);
        var host = new SimpleTestHost(1100, 720);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
        shell.Attach(session);

        shell.ShowView("compose");

        Assert.NotNull(shell.ActiveDialog);
        Assert.False(shell.ActiveDialog.IsBrokenOut);
        Assert.Same(session, shell.ActiveDialog.Session);

        shell.CloseDialog();
        Assert.Null(shell.ActiveDialog);
    }

    [Fact]
    public async Task BrokenOutMailboxOptionsReopensAfterBodyReplacement()
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var dispatcher = new TestQueueDispatcher();
        var key1 = new MailMessageKey(account.Id, "INBOX", 7, 1);
        var key2 = new MailMessageKey(account.Id, "INBOX", 7, 2);
        var messages = new[]
        {
            new MailMessageSummary { Key = key1, Sender = "a@example.test", Subject = "First" },
            new MailMessageSummary { Key = key2, Sender = "b@example.test", Subject = "Second" },
        };
        var receiver = new SourceMailReceiver(messages);
        var cache = new SqliteMailCache(directory.File("mail.db"));
        var exports = new FileMessageSourceExports(directory.Root);
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher, cache, sourceExports: exports),
            new ComposerViewModel(dispatcher: dispatcher));
        using var shell = new MailShellView(model);
        var host = new TestWindowHost(1100, 720);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
        shell.Attach(session);

        await model.Inbox.CacheLoadTask;
        dispatcher.Drain();
        await model.Inbox.ReceiveAsync();
        dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
        await model.Inbox.SelectAsync(key1);
        dispatcher.DrainUntil(() => !model.Inbox.IsBusy);

        // The message's own commands, its .eml export among them, are in the reader and open no dialog; the
        // inbox's remaining dialog breaks out before and after the reader replaces that export with the body.
        Assert.False(shell.ShowView("message-actions"));
        Assert.True(shell.ShowView("mailbox-options"));
        Assert.NotNull(shell.ActiveDialog);
        Assert.True(shell.ActiveDialog.IsBrokenOut);
        Assert.True(shell.CloseDialog());
        Assert.Null(shell.ActiveDialog);

        await model.Inbox.SelectAsync(key2);
        dispatcher.DrainUntil(() => !model.Inbox.IsBusy);

        Assert.False(shell.ShowView("message-actions"));
        Assert.True(shell.ShowView("mailbox-options"));
        Assert.NotNull(shell.ActiveDialog);
        Assert.True(shell.ActiveDialog.IsBrokenOut);
        Assert.True(shell.CloseDialog());
    }

    private sealed class SourceMailReceiver(MailMessageSummary[] messages) : IMailReceiver, IMailSourceReceiver
    {
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MailInboxPage(messages, null));

        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MailMessageBody(key, "Body " + key.Uid));

        public Task<MailMessageSource> GetSourceAsync(AccountProfile account, MailMessageKey key, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MailMessageSource(key, "Subject: Test\r\n\r\nBody"u8.ToArray()));

        public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SimpleTestHost(int width, int height) : IUiHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1.0;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }

    private sealed class TestHostWindow : IUiHostWindow
    {
        public UiSession? BoundSession { get; private set; }
        public bool Activated { get; private set; }
        public string Title { get; private set; } = string.Empty;
        public event EventHandler? CloseRequested { add { } remove { } }
        public BSize ViewportSize => new(800, 600);
        public double Scale => 1.0;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
        public void Bind(UiSession session) => BoundSession = session;
        public void Activate() => Activated = true;
        public void SetTitle(string title) => Title = title;
        public void Dispose() { }
    }

    private sealed class TestWindowHost(int width, int height) : IUiHost, IUiWindowHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1.0;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }

        public List<UiHostWindowRequest> Requests { get; } = [];
        public List<TestHostWindow> HostWindows { get; } = [];

        public IUiHostWindow CreateHostWindow(UiHostWindowRequest request)
        {
            Requests.Add(request);
            var win = new TestHostWindow();
            HostWindows.Add(win);
            return win;
        }
    }
}
