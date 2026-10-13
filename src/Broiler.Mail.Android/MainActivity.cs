using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using Android.Widget;
using Broiler.Graphics.Color;
using Broiler.Hosting.Android;
using Broiler.UI;
using Broiler.UI.Edit;

namespace Broiler.Mail.Android;

[Activity(Label = "Broiler Mail", Theme = "@android:style/Theme.Material.Light.NoActionBar",
    MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
        ConfigChanges.SmallestScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.UiMode |
        ConfigChanges.Density | ConfigChanges.FontScale,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public sealed class MainActivity : Activity
{
    private AndroidBroilerView? _view;
    private MailAndroidUiHost? _host;
    private MailSession? _mail;
    private bool _destroyed;
    private bool _closing;
    private bool _resumed;
    private AlertDialog? _passwordDialog;
    private UiEdit? _passwordOwner;

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.SetSoftInputMode(SoftInput.AdjustResize);
        SetContentView(new TextView(this) { Text = "Opening Broiler Mail…" });
        try
        {
            // NoBackupFilesDir also excludes data from automatic backup on newer Android versions.
            var directory = NoBackupFilesDir?.AbsolutePath
                ?? throw new IOException("Private application storage is unavailable.");
            var application = CompositionRoot.Create(directory);
            await application.InitializeAsync();
            if (_destroyed) return;

            Broiler.Graphics.Text.BSystemFonts.InstallFontFileResolver();
            var view = _view = new AndroidBroilerView(this, BColor.White);
            _host = new MailAndroidUiHost(_view, Resources!.Configuration!, NotifyCaretChanged);
            // View.Post always queues, unlike PostToUiThread's inline main-thread path.
            _mail = new MailSession(application, _host, action => { if (!_destroyed) view.Post(action); });
            _mail.Session.SemanticChanged += (_, change) =>
            {
                // Password controls intentionally publish no readable caret/text to platform IMEs.
                if (change.Change == UiSemanticChangeKind.FocusChanged &&
                    _mail?.Session.FocusedElement is UiEdit { IsPassword: true } password)
                    RequestPasswordEntry(password);
            };
            _view.RenderFrame = _mail.RenderFrame;
            _view.DispatchInput = _mail.Dispatch;
            _view.GetSession = () => _mail?.Session;
            var content = new MailInsetLayout(this);
            content.AddView(_view, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            SetContentView(content);
            ApplySystemBars();
            _view.SetResumed(_resumed);
        }
        catch (Exception error)
        {
            // Startup failures must not leak paths, account data, or secrets into Android logs.
            global::Android.Util.Log.Error("Broiler.Mail", $"Startup failed ({error.GetType().Name}).");
            if (!_destroyed) SetContentView(new TextView(this)
                { Text = "Broiler Mail could not start. Close and reopen the app to retry." });
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        _resumed = true;
        _view?.SetResumed(true);
    }

    protected override void OnPause()
    {
        _resumed = false;
        _view?.SetResumed(false);
        if (_mail is { } mail) _ = SaveDraftAsync(mail);
        base.OnPause();
    }

    private async Task SaveDraftAsync(MailSession mail)
    {
        try { await mail.SaveDraftAsync(); }
        catch (Exception) { /* The draft journal reports save failures in the composer. */ }
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        _host?.Refresh(newConfig);
        ApplySystemBars();
        _view?.InvalidateFrame();
    }

    public override bool DispatchKeyEvent(KeyEvent? e) =>
        _passwordDialog is null && e is not null && _view?.ProcessKeyEvent(e) == true || base.DispatchKeyEvent(e);

    private void ApplySystemBars()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && Window?.InsetsController is { } controller)
            controller.SetSystemBarsAppearance((int)WindowInsetsControllerAppearance.LightStatusBars |
                (int)WindowInsetsControllerAppearance.LightNavigationBars,
                (int)WindowInsetsControllerAppearance.LightStatusBars | (int)WindowInsetsControllerAppearance.LightNavigationBars);
    }

    private void NotifyCaretChanged(UiTextCaretInfo? caret)
    {
        if (caret?.Owner is not UiEdit { IsPassword: true } owner)
        {
            _view?.NotifyCaretChanged(caret);
            return;
        }
        RequestPasswordEntry(owner);
    }

    private void RequestPasswordEntry(UiEdit owner)
    {
        // The shared SurfaceView advertises ordinary text to the IME. Use Android's password
        // editor for secrets so suggestions/personalized learning are disabled at entry time.
        if (_passwordOwner == owner || _passwordDialog is not null) return;
        _view?.NotifyCaretChanged(null);
        _passwordOwner = owner;
        _view?.Post(() =>
        {
            if (_destroyed || owner.IsDisposed || _mail?.Session.FocusedElement != owner)
            { _passwordOwner = null; return; }
            var editor = new EditText(this)
            {
                InputType = global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextVariationPassword,
                ImeOptions = (global::Android.Views.InputMethods.ImeAction)global::Android.Views.InputMethods.ImeFlags.NoExtractUi,
                Text = owner.Text,
            };
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                editor.ImeOptions |= (global::Android.Views.InputMethods.ImeAction)global::Android.Views.InputMethods.ImeFlags.NoPersonalizedLearning;
            editor.SetFilters([new global::Android.Text.InputFilterLengthFilter(1280)]);
            _passwordDialog = new AlertDialog.Builder(this)!.SetTitle("Password / app password")!
                .SetView(editor)!.SetPositiveButton("Use password", (_, _) =>
                {
                    if (!owner.IsDisposed) owner.Text = editor.Text ?? "";
                })!.SetNegativeButton("Cancel", (_, _) => { })!.Create();
            _passwordDialog!.DismissEvent += (_, _) =>
            {
                _mail?.Session.SetFocus(null);
                editor.Text = "";
                editor.Dispose();
                _passwordDialog = null;
                _passwordOwner = null;
            };
            _passwordDialog.Show();
            editor.RequestFocus();
            _passwordDialog.Window?.SetSoftInputMode(SoftInput.StateAlwaysVisible);
        });
    }

    public override async void OnBackPressed()
    {
        if (_closing) return;
        if (_mail is null) { Finish(); return; }
        if (_mail.TryGoBack()) { _view?.InvalidateFrame(); return; }
        _closing = true;
        try
        {
            if (await _mail.PrepareCloseAsync()) Finish();
            else Toast.MakeText(this, "Finish sending or save the draft before closing.", ToastLength.Long)?.Show();
        }
        catch (Exception)
        {
            Toast.MakeText(this, "The draft could not be saved. Please try again.", ToastLength.Long)?.Show();
        }
        finally { _closing = false; }
    }

    protected override void OnDestroy()
    {
        _destroyed = true;
        _passwordDialog?.Dismiss();
        _view?.SetResumed(false);
        _mail?.Dispose();
        _host?.Dispose();
        _mail = null;
        _host = null;
        _view = null;
        base.OnDestroy();
    }
}
