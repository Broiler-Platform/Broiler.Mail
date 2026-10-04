using Broiler.Mail.Application;
using Broiler.Mail.Application.Persistence;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI.RichEdit.Standard;

namespace Broiler.Mail.Windows.Tests;

/// <summary>
/// UI-05: an IME composition in the composer body survives the autosave and status updates that arrive while it
/// is open, and its commit types the text exactly once. The IME messages are posted to the real render window
/// with an injected composition string, the mechanism Hosting's end-to-end IME test uses; a real IME's input
/// context, candidate window, and conversion stay a manual check.
/// </summary>
public sealed class ComposerImeTests
{
    private const uint WmChar = 0x0102;
    private const uint WmImeStartComposition = 0x010D;
    private const uint WmImeEndComposition = 0x010E;
    private const uint WmImeComposition = 0x010F;
    private const uint GcsCompStr = 0x0008;
    private const uint GcsResultStr = 0x0800;

    [Fact]
    public void ACompositionOpenDuringAutosaveAndStatusUpdatesCommitsOnceIntoTheDraft()
    {
        var store = new GatedDraftStore();
        // Disposed after the finally below, so a failed assertion never leaves the close waiting for a held save.
        using var fixture = StartWith(store);
        try
        {
            var ime = new ImeStrings();
            var (body, composer) = StartTyping(fixture, ime);
            fixture.Type("Hi ");
            Assert.Equal("Saving draft…", fixture.Ui(() => composer.StorageStatus));

            fixture.Post(WmImeStartComposition, 0, 0);
            ime.Composition = "にほん";
            fixture.Post(WmImeComposition, 0, (nint)GcsCompStr);
            fixture.Settle();
            AssertComposing(fixture, body, "にほん", "Hi ");

            // While the composition is open: the held autosave completes and the sender line changes, a draft
            // check reports inline, the account changes, and the inbox receives mail.
            store.Release();
            fixture.WaitUntil(() => composer.StorageStatus == "Draft saved locally.", "the autosave finished");
            fixture.Ui(() => composer.CheckDraft());
            Assert.Equal("Draft fields are valid. No mail was sent.", fixture.Ui(() => composer.Status));
            fixture.Ui(() => composer.SetAccount(fixture.Window.Model.Account.Profile! with { DisplayName = "Renamed inbox" }));
            Assert.Equal("Your draft and its original sender are retained.", fixture.Ui(() => composer.Status));
            var inbox = fixture.Window.Model.Inbox;
            fixture.Ui(() => { _ = inbox.ReceiveAsync(); });
            fixture.WaitUntil(() => inbox.Messages.Count > 0 && !inbox.IsBusy, "the inbox received mail");
            fixture.Layout();
            AssertComposing(fixture, body, "にほん", "Hi ");

            // The IME converts, then commits. Windows follows the commit with a WM_CHAR copy of each character.
            ime.Composition = "日本";
            fixture.Post(WmImeComposition, 0, (nint)GcsCompStr);
            fixture.Settle();
            AssertComposing(fixture, body, "日本", "Hi ");
            ime.Composition = "";
            ime.Result = "日本";
            fixture.Post(WmImeComposition, 0, (nint)GcsResultStr);
            fixture.Post(WmChar, '日', 1);
            fixture.Post(WmChar, '本', 1);
            fixture.Post(WmImeEndComposition, 0, 0);
            fixture.Type("!");

            Assert.Equal("", fixture.Ui(() => body.CompositionText));
            Assert.False(fixture.Ui(() => fixture.Window.InputBridge!.IsComposing));
            Assert.Equal("Hi 日本!", fixture.Ui(() => body.GetPlainText()));
            Assert.Equal("Hi 日本!", fixture.Ui(() => composer.PlainText));
            fixture.WaitUntil(() => composer.StorageStatus == "Draft saved locally.", "the commit was saved");
            Assert.Equal("Hi 日本!", store.Saved?.Draft.PlainText);
        }
        finally { store.Release(); }
    }

    [Fact]
    public void ACompositionCancelledAfterAnAutosaveLeavesTheDraftAsItWas()
    {
        var store = new GatedDraftStore();
        using var fixture = StartWith(store);
        try
        {
            var ime = new ImeStrings();
            var (body, composer) = StartTyping(fixture, ime);
            fixture.Type("Hi");

            fixture.Post(WmImeStartComposition, 0, 0);
            ime.Composition = "かな";
            fixture.Post(WmImeComposition, 0, (nint)GcsCompStr);
            fixture.Settle();
            store.Release();
            fixture.WaitUntil(() => composer.StorageStatus == "Draft saved locally.", "the autosave finished");
            fixture.Layout();
            AssertComposing(fixture, body, "かな", "Hi");

            // Escape in the IME ends the composition without a result.
            fixture.Post(WmImeEndComposition, 0, 0);
            fixture.Type("!");

            Assert.Equal("", fixture.Ui(() => body.CompositionText));
            Assert.Equal("Hi!", fixture.Ui(() => body.GetPlainText()));
            Assert.Equal("Hi!", fixture.Ui(() => composer.PlainText));
        }
        finally { store.Release(); }
    }

    private static HiddenMailWindow StartWith(IDraftStore store) => HiddenMailWindow.Start(() =>
    {
        var demo = DemoApplication.Create();
        return new MailApplication(demo.Accounts, demo.Settings, demo.Receiver, demo.Sender, demo.Credentials, store);
    });

    /// <summary>Starts a draft addressed to one recipient, focuses its body, and lets the IME strings answer.</summary>
    private static (StandardRichEdit Body, ComposerViewModel Composer) StartTyping(HiddenMailWindow fixture, ImeStrings ime)
    {
        var (_, body) = NativeInputFidelityTests.StartDraft(fixture);
        fixture.Type("team@example.test");
        fixture.Ui(() =>
        {
            fixture.Window.Session.SetFocus(body);
            fixture.Window.InputBridge!.CompositionStringProvider = (_, index) =>
                index == GcsResultStr ? ime.Result : index == GcsCompStr ? ime.Composition : "";
        });
        return (body, fixture.Window.Model.Composer);
    }

    private static void AssertComposing(HiddenMailWindow fixture, StandardRichEdit body, string composition, string text)
    {
        Assert.Same(body, fixture.Ui(() => fixture.Window.Session.FocusedElement));
        Assert.True(fixture.Ui(() => fixture.Window.InputBridge!.IsComposing));
        Assert.Equal(composition, fixture.Ui(() => body.CompositionText));
        // The preedit is shown, not typed: neither the document nor the draft contains it yet.
        Assert.Equal(text, fixture.Ui(() => body.GetPlainText()));
        Assert.Equal(text, fixture.Ui(() => fixture.Window.Model.Composer.PlainText));
    }

    /// <summary>What the injected IME reports; read on the window thread when a posted message arrives.</summary>
    private sealed class ImeStrings
    {
        public volatile string Composition = "";
        public volatile string Result = "";
    }

    /// <summary>Holds every save until released, so an autosave can complete while a composition is open.</summary>
    private sealed class GatedDraftStore : IDraftStore
    {
        private readonly MemoryDraftStore _inner = new();
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsPersistent => true;
        public DraftSnapshot? Saved { get; private set; }
        public void Release() => _gate.TrySetResult();
        public Task<DraftStoreState> LoadAsync(CancellationToken cancellationToken = default) => _inner.LoadAsync(cancellationToken);
        public async Task<DraftStoreState> SaveAsync(long expectedRevision, DraftSnapshot? draft, CancellationToken cancellationToken = default)
        {
            await _gate.Task.ConfigureAwait(false);
            var saved = await _inner.SaveAsync(expectedRevision, draft, cancellationToken).ConfigureAwait(false);
            Saved = saved.Draft;
            return saved;
        }
    }
}
