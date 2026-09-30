using Broiler.Mail.Application;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Windows.Hosting;
using static Broiler.Native.Windows.WindowNative;

namespace Broiler.Mail.Windows.Tests;

public sealed class DraftWindowCloseTests
{
    [Fact]
    public async Task NativeCloseWaitsForDraftFlushBeforeDestroyingWindow()
    {
        var store = new BlockingDraftStore();
        var ready = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var demo = DemoApplication.Create();
                var app = new MailApplication(demo.Accounts, demo.Settings, demo.Receiver, demo.Sender, demo.Credentials, store);
                app.InitializeAsync().GetAwaiter().GetResult();
                using var window = new WindowsMailWindow(app, demo: true);
                window.CloseRequested += (_, _) => requested.TrySetResult();
                window.Show();
                ShowWindow(window.NativeHandle, 0); // Keep the native integration fixture hidden.
                ready.SetResult(window.NativeHandle);
                while (GetMessage(out MSG message, nint.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
                finished.SetResult();
            }
            catch (Exception error) { ready.TrySetException(error); finished.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        nint handle = 0;
        try
        {
            handle = await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(PostMessage(handle, 0x0010, 0, 0)); // WM_CLOSE
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(finished.Task.IsCompleted);
            store.Release.TrySetResult();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(DraftSubmissionState.Unknown, (await store.LoadAsync()).Draft!.State);
        }
        finally
        {
            store.Release.TrySetResult();
            if (handle != 0 && !finished.Task.IsCompleted) PostMessage(handle, 0x0010, 0, 0);
        }
    }

    private sealed class BlockingDraftStore : IDraftStore
    {
        private DraftStoreState _state = new(1, new()
        {
            Draft = new() { AccountId = AccountId.New(), FromAddress = "fixture@example.test", PlainText = "Unfinished fixture" },
            ToText = "recipient@example.test", CcText = "", BccText = "", State = DraftSubmissionState.Sending,
        });
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsPersistent => true;
        public Task<DraftStoreState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_state);
        public async Task<DraftStoreState> SaveAsync(long expectedRevision, DraftSnapshot? draft, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task;
            if (_state.Revision != expectedRevision) throw new DraftConflictException();
            return _state = new(expectedRevision + 1, draft);
        }
    }
}
