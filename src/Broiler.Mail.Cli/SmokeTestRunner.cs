// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Cli;

internal static class SmokeTestRunner
{
    public static void Run()
    {
        string isolatedPath = Path.Combine(Path.GetTempPath(), "Broiler.Mail.Smoke", Guid.NewGuid().ToString("N"));
        var credentials = new MemoryCredentialStore();
        var application = new MailApplication(
            new JsonAccountStore(Path.Combine(isolatedPath, "accounts.json")),
            new JsonSettingsStore(Path.Combine(isolatedPath, "settings.json")),
            new ImapMailReceiver(credentials),
            new SmtpMailSender(credentials),
            credentials,
            new JsonDraftStore(Path.Combine(isolatedPath, "drafts.json")),
            new ImapSentCopyWriter(credentials),
            new SmtpConnectionTester(credentials));

        application.InitializeAsync().GetAwaiter().GetResult();
        using var shell = application.CreateShell();

        using var session = new StandardUiSessionBuilder().Build(new HeadlessHost());
        shell.Attach(session);
        foreach (string tabId in new[] { "inbox", "account", "settings", "compose" })
        {
            if (!shell.ShowView(tabId))
                throw new InvalidOperationException($"Missing shell view: {tabId}");
            _ = session.RenderFrame();
        }
    }

    private sealed class HeadlessHost : IUiHost
    {
        public BSize ViewportSize => new(1100, 720);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
