using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.UI.Forms.Standard;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class FormExperienceTests
{
    [Fact]
    public async Task InvalidAdvancedFieldExpandsAndConnectionCancellationRemainsReachable()
    {
        using var directory = new TestDirectory();
        var profile = TestDirectory.Profile();
        profile = profile with { OutgoingServer = profile.IncomingServer with { Host = "smtp.example.test", Port = 587 } };
        var receiver = new TestMailReceiver { Test = token => Task.Delay(Timeout.InfiniteTimeSpan, token) };
        var model = new AccountProfileViewModel(new JsonAccountStore(directory.File("account.json")),
            new TestCredentialStore(), receiver, new ImmediateUiDispatcher(), profile, null);
        using var surface = new AccountProfileView(model).CreateContent();
        using var session = new StandardUiSessionBuilder().Build(new Host(640, 480));
        session.AddRoot(surface);
        var advanced = Descendants(surface).OfType<FormSection>().Single(s => s.Toggle is not null);
        var handling = Descendants(advanced).OfType<StandardComboBox>().Single();
        handling.SelectedIndex = (int)SentCopyMode.AppendToFolder;
        model.SentCopyMode = SentCopyMode.AppendToFolder;
        advanced.IsExpanded = false;
        await model.SaveAsync();
        Assert.Equal("SentFolder", model.ValidationField);
        Assert.True(advanced.IsExpanded);
        Assert.Same(Descendants(advanced).OfType<StandardEdit>().Single(), session.FocusedElement);
        model.SentCopyMode = SentCopyMode.NotConfigured;
        handling.SelectedIndex = 0;
        var pending = model.TestConnectionAsync();
        session.RenderFrame();
        var cancel = Descendants(surface).OfType<StandardButton>().Single(b => b.Text == "Cancel test");
        Assert.Equal(FeedbackKind.Progress, model.StatusKind);
        Assert.True(cancel.IsEnabled);
        Assert.Equal(UiVisibility.Visible, cancel.Visibility);
        Assert.InRange(cancel.Bounds.Bottom, 1, 480);
        session.SetFocus(cancel);
        cancel.Click();
        await pending;
        Assert.Equal(UiVisibility.Collapsed, cancel.Visibility);
        Assert.Equal("Test connection", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        Assert.Equal(FeedbackKind.Information, model.StatusKind);
    }

    [Fact]
    public async Task InvalidSettingsFocusTheFieldAndSuccessfulRetryPreservesSplitter()
    {
        using var directory = new TestDirectory();
        var store = new JsonSettingsStore(directory.File("settings.json"));
        var model = new SettingsViewModel(store, new ImmediateUiDispatcher(), new() { InboxSplitterFraction = 0.63 }, null);
        using var surface = new SettingsView(model).CreateContent();
        using var session = new StandardUiSessionBuilder().Build(new Host(640, 480));
        session.AddRoot(surface);
        session.RenderFrame();
        var field = Descendants(surface).OfType<FormField>().Single(f => f.Label.Text.StartsWith("Initial window height"));
        var edit = Assert.IsType<StandardEdit>(field.Control);
        edit.Text = model.WindowHeight = "wrong";
        await model.SaveAsync();
        session.RenderFrame();
        Assert.Equal("WindowHeight", model.ValidationField);
        Assert.Equal(FeedbackKind.Error, model.StatusKind);
        Assert.Same(edit, session.FocusedElement);
        Assert.True(edit.IsEnabled);
        Assert.Contains("whole number", field.Error);
        Assert.True(field.GetSemanticNode().State.HasFlag(UiSemanticState.Invalid));
        var scroll = Assert.IsType<FormSurface>(surface).Content.Scroll;
        Assert.InRange(edit.Bounds.Bottom, scroll.ContentBounds.Top, scroll.ContentBounds.Bottom + 1);
        edit.Text = model.WindowHeight = "720";
        Assert.Empty(field.Error);
        await model.SaveAsync();
        Assert.Null(model.ValidationField);
        Assert.Equal(FeedbackKind.Success, model.StatusKind);
        Assert.Equal(0.63, model.Settings.InboxSplitterFraction);
        Assert.Equal(720, model.Settings.WindowHeight);
    }

    private static IEnumerable<UiElement> Descendants(UiElement element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var nested in Descendants(child)) yield return nested;
    }

    private sealed class Host(int width, int height) : IUiHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
