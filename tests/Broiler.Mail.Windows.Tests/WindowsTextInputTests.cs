using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Broiler.Graphics.Geometry;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Preview;
using Broiler.Mail.Windows.Services;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.RichEdit.Standard;
using static Broiler.Native.Windows.WindowNative;

namespace Broiler.Mail.Windows.Tests;

/// <summary>
/// UI-10: the IME composition window is placed at the caret in physical client pixels. The caret arrives in
/// device-independent pixels; the input context is read back through IMM32, so the order of the style and
/// position fields in the struct is checked too.
/// </summary>
[Collection("UI theme")]
public sealed class WindowsTextInputTests
{
    private const uint CfsPoint = 0x0002;
    // A caret in DIPs whose left edge and bottom are not whole pixels at any of the scales below.
    private static readonly BRect Caret = new(101.3, 40.5, 1, 17.25);

    [Theory]
    [InlineData(1.0, 101, 58)]
    [InlineData(1.5, 152, 87)]
    [InlineData(2.0, 203, 116)]
    public void TheCompositionWindowStartsBelowTheCaretInPhysicalPixels(double scale, int x, int y)
    {
        OnWindowThread(window =>
        {
            new WindowsTextInput(() => window, () => scale).PublishCaret(new UiTextCaretInfo(new StandardEdit(), Caret, 0, 0, 0, true));

            var form = ReadCompositionForm(window);
            Assert.Equal(CfsPoint, form.Style);
            Assert.Equal((x, y), (form.X, form.Y));
        });
    }

    [Fact]
    public void AScaleChangeAppliesToTheNextCaretWithoutANewInputHost()
    {
        OnWindowThread(window =>
        {
            double scale = 1.0;
            var input = new WindowsTextInput(() => window, () => scale);
            var edit = new StandardEdit();
            input.PublishCaret(new UiTextCaretInfo(edit, Caret, 0, 0, 0, false));
            Assert.Equal((101, 58), Position(ReadCompositionForm(window)));

            // Moving the window to a 200 % monitor changes the scale the host reports, not the host.
            scale = 2.0;
            input.PublishCaret(new UiTextCaretInfo(edit, Caret, 0, 0, 0, false));
            Assert.Equal((203, 116), Position(ReadCompositionForm(window)));
        });
    }

    [Fact]
    public void BeforeTheWindowExistsACaretIsIgnored()
    {
        // Before WM_CREATE the render window handle is 0. A caret then, a password field's included, is ignored:
        // nothing throws and no position is computed.
        int scaleReads = 0;
        var input = new WindowsTextInput(() => 0, () => { scaleReads++; return 1.5; });
        var password = new StandardEdit { IsPassword = true };
        input.PublishCaret(new UiTextCaretInfo(new StandardEdit(), Caret, 0, 0, 0, false));
        input.PublishCaret(new UiTextCaretInfo(password, Caret, 0, 0, 0, false));
        input.ClearCaret(password);
        input.FollowFocus(new StandardPanel());
        input.FollowFocus(null);
        Assert.Equal(0, scaleReads);
    }

    [Fact]
    public void OnlyAWritableEditorDrawsTheComposition()
    {
        Assert.True(WindowsTextInput.DrawsComposition(new StandardEdit()));
        Assert.True(WindowsTextInput.DrawsComposition(new StandardRichEdit()));
        Assert.False(WindowsTextInput.DrawsComposition(new StandardEdit { IsPassword = true }));
        Assert.False(WindowsTextInput.DrawsComposition(new StandardEdit { IsReadOnly = true }));
        Assert.False(WindowsTextInput.DrawsComposition(new StandardEdit { IsEnabled = false }));
        Assert.False(WindowsTextInput.DrawsComposition(new StandardRichEdit { IsReadOnly = true }));
        Assert.False(WindowsTextInput.DrawsComposition(new StandardListView()));
        Assert.False(WindowsTextInput.DrawsComposition(new StandardButton()));
        Assert.False(WindowsTextInput.DrawsComposition(null));
    }

    [Fact]
    public void FocusOnAnythingButAWritableEditorTurnsTheImeOff()
    {
        OnWindowThread(window =>
        {
            var input = new WindowsTextInput(() => window, () => 1.0);
            var list = new StandardListView();
            var reader = new StandardRichEdit { IsReadOnly = true };
            var body = new StandardRichEdit();

            // Hosting keeps the IME from drawing the composition, so where Broiler.UI draws none it would be
            // invisible: a list, a button, a read-only reader, or nothing focused at all.
            input.FollowFocus(list);
            Assert.False(HasInputContext(window));
            input.FollowFocus(new StandardButton());
            Assert.False(HasInputContext(window));
            input.FollowFocus(null);
            Assert.False(HasInputContext(window));
            input.FollowFocus(body);
            Assert.True(HasInputContext(window));
            input.FollowFocus(reader);
            Assert.False(HasInputContext(window));
            // The read-only reader publishes its caret as it draws; that keeps the IME off.
            input.PublishCaret(new UiTextCaretInfo(reader, Caret, 0, 0, 0, false));
            Assert.False(HasInputContext(window));
            // Focus moving on clears the reader's caret first; the next focus decides.
            input.ClearCaret(reader);
            input.FollowFocus(new StandardEdit());
            Assert.True(HasInputContext(window));
        });
    }

    [Fact]
    public void APasswordFieldTurnsTheImeOffUntilItsCaretIsCleared()
    {
        OnWindowThread(window =>
        {
            var input = new WindowsTextInput(() => window, () => 1.0);
            var password = new StandardEdit { IsPassword = true };
            var other = new StandardEdit();

            // Broiler.UI draws no composition in a password field, and the IME's own window would show the password
            // in plain text. A native password box takes no IME either.
            input.PublishCaret(new UiTextCaretInfo(password, Caret, 0, 0, 0, false));
            Assert.False(HasInputContext(window));
            input.ClearCaret(other);
            Assert.False(HasInputContext(window));
            // Focus leaving the password field clears its caret, which gives the window its IME back.
            input.ClearCaret(password);
            Assert.True(HasInputContext(window));

            // A caret in another field brings the IME back too, placed at that caret.
            input.PublishCaret(new UiTextCaretInfo(password, Caret, 0, 0, 0, false));
            Assert.False(HasInputContext(window));
            input.PublishCaret(new UiTextCaretInfo(other, Caret, 0, 0, 0, false));
            Assert.Equal((101, 58), Position(ReadCompositionForm(window)));
        });
    }

    [Fact]
    public void TheAccountPasswordTakesNoImeAndTheNextFieldGetsItBack()
    {
        using var fixture = HiddenMailWindow.Start();
        fixture.Ui(() => fixture.Window.Shell.Navigation.SelectTab("account"));
        fixture.Layout();
        var account = fixture.Ui(() => fixture.Window.Shell.Navigation.Tabs.Single(tab => tab.Id == "account").Content!);
        StandardEdit Field(string label) => fixture.Ui(() =>
            (StandardEdit)HiddenMailWindow.Descendants(account).OfType<StandardLabel>().Single(item => item.Text == label).Target!);
        var password = Field("Password / app password");
        var user = Field("Username");
        double scale = fixture.Ui(() => fixture.Window.DpiScale);

        // The frame that draws the focused password field turns the window's IME off; keys are still typed.
        fixture.Ui(() => fixture.Window.Session.SetFocus(password));
        fixture.Type("pw");
        fixture.Layout();
        Assert.False(fixture.Ui(() => HasInputContext(fixture.Render)));
        Assert.Equal("pw", fixture.Ui(() => password.Text));

        fixture.Ui(() => fixture.Window.Session.SetFocus(user));
        fixture.Layout();
        Assert.True(fixture.Ui(() => HasInputContext(fixture.Render)));
        AssertWithin(fixture.Ui(() => user.Bounds), fixture.Ui(() => ReadCompositionForm(fixture.Render)), scale);
    }

    [Fact]
    public void TheImeIsOnlyOnWhileAWritableEditorHasFocus()
    {
        using var fixture = HiddenMailWindow.Start();
        // The window opens with the tab strip focused, which draws no composition.
        Assert.False(fixture.Ui(() => HasInputContext(fixture.Render)));
        var (to, _) = NativeInputFidelityTests.StartDraft(fixture);
        Assert.True(fixture.Ui(() => HasInputContext(fixture.Render)));

        var inbox = fixture.Ui(() => fixture.Window.Shell.Navigation.Tabs.Single(tab => tab.Id == "inbox").Content!);
        var list = fixture.Ui(() => HiddenMailWindow.Descendants(inbox).OfType<StandardListView>().Single());
        var reader = fixture.Ui(() => HiddenMailWindow.Descendants(inbox).OfType<StandardRichEdit>().Single(editor => editor.AccessibleName == "Message text"));
        fixture.Ui(() => fixture.Window.Session.SetFocus(list));
        Assert.False(fixture.Ui(() => HasInputContext(fixture.Render)));
        fixture.Ui(() => fixture.Window.Session.SetFocus(reader));
        fixture.Layout();
        Assert.False(fixture.Ui(() => HasInputContext(fixture.Render)));

        // Back in a field of the composer, the IME is on again at once, before the next frame.
        fixture.Ui(() => fixture.Window.Session.SetFocus(to));
        Assert.True(fixture.Ui(() => HasInputContext(fixture.Render)));
        fixture.Ui(() => fixture.Window.Session.SetFocus(fixture.Window.Shell.Navigation));
        Assert.False(fixture.Ui(() => HasInputContext(fixture.Render)));
    }

    [Fact]
    public async Task TheHtmlPreviewTakesNoImeSinceNothingInItDrawsAComposition()
    {
        var result = new TaskCompletionSource<(bool Document, bool PlainText)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var window = new HtmlPreviewWindow(new HtmlPreviewDocument("<p>Agenda</p>", new HashSet<string>()), "Agenda as text", _ => { })
                { ShowInTaskbar = false, Opacity = 0 };
                window.Shown += (_, _) =>
                {
                    // The document has focus when the preview opens; its read-only plain text takes none either.
                    bool document = HasInputContext(window.InputHandle);
                    window.Session.SetFocus(window.PlainTextView.Editor);
                    result.TrySetResult((document, HasInputContext(window.InputHandle)));
                    window.Close();
                };
                window.Run();
            }
            catch (Exception error) { result.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var (document, plainText) = await result.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(document);
        Assert.False(plainText);
    }

    [Fact]
    public void TheComposerPlacesTheCompositionWindowAtTheFocusedFieldsCaret()
    {
        using var fixture = HiddenMailWindow.Start();
        var (to, body) = NativeInputFidelityTests.StartDraft(fixture);
        double scale = fixture.Ui(() => fixture.Window.DpiScale);
        // The frame and the render window share the thread's default input context. A context of the render
        // window's own shows the position is set through the render window, which holds keyboard focus.
        nint own = fixture.Ui(() =>
        {
            nint context = ImmCreateContext();
            Assert.NotEqual(0, context);
            ImmAssociateContext(fixture.Render, context);
            return context;
        });
        Assert.Equal(own, fixture.Ui(() => ContextOf(fixture.Render)));
        Assert.NotEqual(own, fixture.Ui(() => ContextOf(fixture.Frame)));

        // Each frame publishes the focused field's caret; the window's input context must hold it in physical pixels.
        fixture.Type("team@example.test");
        fixture.Layout();
        var inTo = fixture.Ui(() => ReadCompositionForm(fixture.Render));
        AssertWithin(fixture.Ui(() => to.Bounds), inTo, scale);

        fixture.Ui(() => fixture.Window.Session.SetFocus(body));
        fixture.Type("Hi");
        fixture.Layout();
        var inBody = fixture.Ui(() => ReadCompositionForm(fixture.Render));
        AssertWithin(fixture.Ui(() => body.Bounds), inBody, scale);
        fixture.Type(" there");
        fixture.Layout();
        var further = fixture.Ui(() => ReadCompositionForm(fixture.Render));
        Assert.True(further.X > inBody.X, "The composition window follows the caret along the line.");
        Assert.Equal(inBody.Y, further.Y);

        // The window keeps the context until it is destroyed; giving it back its default one lets it be freed now.
        fixture.Ui(() =>
        {
            ImmAssociateContextEx(fixture.Render, 0, 0x0010 /* IACE_DEFAULT */);
            ImmDestroyContext(own);
        });
    }

    private static nint ContextOf(nint window)
    {
        nint context = ImmGetContext(window);
        if (context != 0) ImmReleaseContext(window, context);
        return context;
    }

    private static void AssertWithin(BRect field, CompositionForm form, double scale)
    {
        Assert.Equal(CfsPoint, form.Style);
        Assert.InRange(form.X, (int)Math.Floor(field.Left * scale), (int)Math.Ceiling(field.Right * scale));
        Assert.InRange(form.Y, (int)Math.Floor(field.Top * scale), (int)Math.Ceiling(field.Bottom * scale));
    }

    private static (int X, int Y) Position(CompositionForm form) => (form.X, form.Y);

    private static bool HasInputContext(nint window) => ContextOf(window) != 0;

    private static CompositionForm ReadCompositionForm(nint window)
    {
        nint context = ImmGetContext(window);
        Assert.NotEqual(0, context);
        try
        {
            Assert.True(ImmGetCompositionWindow(context, out var form), "The input context has no composition form.");
            return form;
        }
        finally { ImmReleaseContext(window, context); }
    }

    /// <summary>Runs <paramref name="test"/> with a hidden native window, on its own thread, as the input context requires.</summary>
    private static void OnWindowThread(Action<nint> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            nint window = CreateWindowEx(0, "STATIC", "IME placement fixture", 0x80000000 /* WS_POPUP */, 0, 0, 200, 100, 0, 0, 0, 0);
            try
            {
                Assert.NotEqual(0, window);
                test(window);
            }
            catch (Exception error) { failure = error; }
            finally { if (window != 0) DestroyWindow(window); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionForm { public uint Style; public int X, Y, Left, Top, Right, Bottom; }

    [DllImport("imm32.dll")] private static extern nint ImmGetContext(nint window);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmReleaseContext(nint window, nint context);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmGetCompositionWindow(nint context, out CompositionForm form);
    [DllImport("imm32.dll")] private static extern nint ImmCreateContext();
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmDestroyContext(nint context);
    [DllImport("imm32.dll")] private static extern nint ImmAssociateContext(nint window, nint context);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmAssociateContextEx(nint window, nint context, uint flags);
}
