using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Android;
using Broiler.Mail.Application;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class AndroidMailSessionTests
{
    [Fact]
    public async Task SetupAndFormsStayInsidePhoneViewportAndBackReturnsToInbox()
    {
        using var directory = new TestDirectory();
        var application = CreateApplication(directory);
        await application.InitializeAsync();
        var posts = new Queue<Action>();
        using var mail = new MailSession(application, new Host(), posts.Enqueue);
        try
        {
            Assert.Equal("account", mail.Shell.ActiveViewId);
            Assert.True(mail.TryGoBack());
            Assert.Equal("inbox", mail.Shell.ActiveViewId);
            Assert.False(mail.TryGoBack());
            foreach (var view in new[] { "account", "settings", "compose" })
            {
                mail.Shell.ShowView(view);
                mail.RenderFrame();
                var dialog = Assert.IsType<Broiler.UI.Dialog.Standard.StandardDialog>(mail.Shell.ActiveDialog);
                Assert.False(dialog.IsBrokenOut);
                Assert.InRange(dialog.Placement.Right, 1, 360);
                Assert.InRange(dialog.Placement.Bottom, 1, 640);
                Assert.True(mail.TryGoBack());
            }
        }
        finally { StandardControlPaint.ApplyTheme(StandardThemeTokens.Light); }
    }

    [Fact]
    public async Task PauseSavesDraftWithoutDisablingFurtherEditsAndRestartRecoversIt()
    {
        using var directory = new TestDirectory();
        var application = CreateApplication(directory);
        await application.Accounts.SaveAsync(TestDirectory.Profile());
        await application.InitializeAsync();
        using (var mail = new MailSession(application, new Host(), _ => { }))
        {
            Assert.True(mail.Model.Composer.StartNew());
            mail.Model.Composer.Edit("", "", "", "Before pause", "Draft body");
            Assert.True(await mail.SaveDraftAsync());
            mail.Model.Composer.Edit("", "", "", "After resume", "Draft body");
            Assert.True(await mail.SaveDraftAsync());
        }
        var reopened = CreateApplication(directory);
        await reopened.InitializeAsync();
        Assert.Equal("After resume", reopened.LoadedDraft.Draft?.Draft.Subject);
    }

    private static MailApplication CreateApplication(TestDirectory directory)
    {
        var credentials = new TestCredentialStore();
        return new(new JsonAccountStore(directory.File("accounts.json")),
            new JsonSettingsStore(directory.File("settings.json")), new TestMailReceiver(),
            new SmtpMailSender(credentials), credentials, new JsonDraftStore(directory.File("drafts.json")));
    }

    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(360, 640);
        public double Scale => 3;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
