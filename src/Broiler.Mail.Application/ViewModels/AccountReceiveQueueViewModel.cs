using Broiler.Mail.Application.Persistence;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI;

namespace Broiler.Mail.Application.ViewModels;

public enum AccountReceiveState { Idle, Queued, Receiving, Canceling, Completed, Failed, Canceled, ConfigurationChanged }
public sealed record AccountReceiveJobStatus(AccountId AccountId, string DisplayName, string Address, AccountReceiveState State, int SavedHeaders, DateTimeOffset? FinishedAt, string Detail);

/// <summary>Explicit, session-only first-page receive queue. Two workers, one outstanding job per configured account.
/// No timers, retries, body fetches, submissions or selected-account/draft changes.</summary>
public sealed class AccountReceiveQueueViewModel(CoordinatedAccountCollectionStore store, IMailReceiver receiver, IMailCache cache, IUiDispatcher dispatcher, Func<bool>? canQueue = null) : IDisposable
{
    public const int MaximumConcurrentReceives = 2;
    private readonly object _sync = new();
    private readonly Queue<Work> _pending = new();
    private readonly Dictionary<AccountId, Work> _jobs = [];
    private AccountProfile[] _profiles = [];
    private bool _disposed;
    private int _running;
    private int _notificationPending;
    private string _notice = "Queue explicitly. Each job saves at most 50 INBOX headers; saved bodies are retained. No polling, automatic retry, body download or sending.";
    private TaskCompletionSource _idle = CompletedSource();
    public event EventHandler? Changed;
    public bool HasWork { get { lock (_sync) return _jobs.Values.Any(work => work.Active); } }
    public int PendingCount { get { lock (_sync) return _pending.Count; } }
    public Task WhenIdle { get { lock (_sync) return _idle.Task; } }
    public IReadOnlyList<AccountReceiveJobStatus> Jobs
    {
        get { lock (_sync) return _profiles.Select(profile => _jobs.TryGetValue(profile.Id, out var work)
            ? new(profile.Id, profile.DisplayName, profile.EmailAddress, work.State, work.Saved, work.Finished, work.Detail)
            : new AccountReceiveJobStatus(profile.Id, profile.DisplayName, profile.EmailAddress, AccountReceiveState.Idle, 0, null, profile.IsEnabled ? "Not queued." : "Account disabled; no connection will start.")).ToArray(); }
    }
    public string Notice { get { lock (_sync) return _notice + $" Queued: {_pending.Count}; active/canceling: {_running}. Jobs are not persisted or replayed after restart. Use the reader's saved headers to inspect results."; } }
    public bool CanQueue => !_disposed && (canQueue?.Invoke() ?? true);
    public bool CanQueueAll => CanQueue && Jobs.Any(job => CanQueueAccount(job.AccountId));
    public bool CanQueueAccount(AccountId id)
    { if (!CanQueue) return false; lock (_sync) return _profiles.Any(profile => profile.Id == id && profile.IsEnabled) && (!_jobs.TryGetValue(id, out var work) || !work.Active); }
    public bool CanCancel(AccountId id) { lock (_sync) return !_disposed && _jobs.TryGetValue(id, out var work) && work.Active && !work.CancelRequested; }
    public bool CanCancelAll { get { lock (_sync) return !_disposed && _jobs.Values.Any(work => work.Active && !work.CancelRequested); } }

    public void UpdateAccounts(IReadOnlyList<AccountProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count > 32 || profiles.Any(profile => profile is null || profile.Id.Value == Guid.Empty) || profiles.Select(profile => profile.Id).Distinct().Count() != profiles.Count) throw new ArgumentException("Use at most 32 unique configured accounts.");
        var frozen = profiles.Select(profile => profile with { SenderIdentities = profile.SenderIdentities is null ? null : Array.AsReadOnly(profile.SenderIdentities.ToArray()) }).ToArray();
        var canceled = new List<Work>();
        lock (_sync)
        {
            if (_disposed || _profiles.Length == frozen.Length && _profiles.Zip(frozen).All(pair => AccountProfileConfiguration.Matches(pair.First, pair.Second))) return;
            _profiles = frozen;
            foreach (var work in _jobs.Values.ToArray())
            {
                var actual = frozen.SingleOrDefault(profile => profile.Id == work.Profile.Id);
                if (actual is not null && AccountProfileConfiguration.Matches(work.Profile, actual)) continue;
                if (work.Active) { RequestCancel(work, changed: true); canceled.Add(work); if (!work.Active && actual is null) _jobs.Remove(work.Profile.Id); }
                else if (actual is null) _jobs.Remove(work.Profile.Id);
                else { work.State = AccountReceiveState.ConfigurationChanged; work.Detail = "Saved configuration changed after this job. Queue explicitly with current settings."; }
            }
            Schedule(); CompleteIdle();
        }
        CancelTokens(canceled); Notify();
    }
    public bool QueueAccount(AccountId id)
    {
        if (!CanQueue) return false; bool added;
        lock (_sync) { if (_disposed) return false; var profile = _profiles.SingleOrDefault(profile => profile.Id == id); added = profile is { IsEnabled: true } && Enqueue(profile); if (added) _notice = "One account receive queued explicitly."; Schedule(); }
        if (added) Notify(); return added;
    }
    public int QueueAllEnabled()
    {
        if (!CanQueue) return 0; int added = 0;
        lock (_sync)
        {
            if (_disposed) return 0;
            foreach (var profile in _profiles.Where(profile => profile.IsEnabled)) if (Enqueue(profile)) added++;
            _notice = $"Last request queued {added} enabled accounts in configured order. Disabled accounts are skipped and outstanding jobs coalesced."; Schedule();
        }
        Notify(); return added;
    }
    private bool Enqueue(AccountProfile profile)
    {
        if (_jobs.TryGetValue(profile.Id, out var previous) && previous.Active || !_jobs.ContainsKey(profile.Id) && _jobs.Count >= 32 + MaximumConcurrentReceives) return false;
        if (!_jobs.Values.Any(work => work.Active)) _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = new Work(profile); _jobs[profile.Id] = work; _pending.Enqueue(work); return true;
    }
    public void Cancel(AccountId id) => CancelWhere(work => work.Profile.Id == id);
    public void CancelAll() => CancelWhere(_ => true);
    private void CancelWhere(Func<Work, bool> predicate)
    {
        Work[] canceled;
        lock (_sync) { canceled = _jobs.Values.Where(work => work.Active && !work.CancelRequested && predicate(work)).ToArray(); foreach (var work in canceled) RequestCancel(work, changed: false); Schedule(); CompleteIdle(); }
        CancelTokens(canceled); Notify();
    }
    private static void RequestCancel(Work work, bool changed)
    {
        work.CancelRequested = true; work.Changed |= changed;
        work.State = work.Started ? AccountReceiveState.Canceling : changed ? AccountReceiveState.ConfigurationChanged : AccountReceiveState.Canceled;
        work.Detail = work.Started ? "Cancel requested; waiting for this account's operation to finish. A committed save may remain." : changed ? "Configuration changed before dispatch; nothing received." : "Canceled before dispatch; nothing received.";
        if (!work.Started) work.Finished = DateTimeOffset.UtcNow;
    }
    private static void CancelTokens(IEnumerable<Work> works)
    {
        foreach (var work in works)
        {
            try { work.Cancellation.Cancel(); } catch (Exception error) when (error is ObjectDisposedException or AggregateException) { }
            if (!work.Started) work.Cancellation.Dispose();
        }
    }
    private void Schedule()
    {
        // Remove canceled/replaced entries even when both slots are held; repeated cancel/requeue cannot grow a tombstone backlog.
        int waiting = _pending.Count;
        for (int index = 0; index < waiting; index++)
        {
            var pending = _pending.Dequeue();
            if (pending.State == AccountReceiveState.Queued && _jobs.TryGetValue(pending.Profile.Id, out var current) && current == pending) _pending.Enqueue(pending);
        }
        while (!_disposed && _running < MaximumConcurrentReceives && _pending.TryDequeue(out var work))
        {
            if (work.State != AccountReceiveState.Queued || !_jobs.TryGetValue(work.Profile.Id, out var current) || current != work) continue;
            work.Started = true; work.State = AccountReceiveState.Receiving; work.Detail = "Receiving one bounded INBOX header page; no body fetch or send."; _running++;
            _ = Task.Run(() => Run(work));
        }
    }
    private async Task Run(Work work)
    {
        var token = work.Cancellation.Token; var state = AccountReceiveState.Completed; string detail = "Header page saved. Older cached mail and bodies were retained; this is not a full-folder observation."; int saved = 0;
        try
        {
            await store.WithStableProfileAsync(work.Profile, _ => Task.CompletedTask, token).ConfigureAwait(false);
            var page = await receiver.GetInboxAsync(work.Profile, InboxViewModel.PageSize, cancellationToken: token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var mailbox = page?.Mailbox;
            if (page?.Messages is null || mailbox is null || mailbox.AccountId != work.Profile.Id || mailbox.MailboxId != "INBOX" || page.Messages.Count > InboxViewModel.PageSize || page.Messages.Any(row => row is null || !mailbox.Contains(row.Key)) || page.Messages.Select(row => row.Key.Uid).Distinct().Count() != page.Messages.Count)
                throw new InvalidDataException("Receive returned another account/mailbox or invalid page.");
            var rows = page.Messages.ToArray(); var observed = DateTimeOffset.UtcNow;
            await store.WithStableProfileAsync(work.Profile, cancellation => cache.SaveSummariesAsync(mailbox, rows, observed, cancellation), token).ConfigureAwait(false);
            saved = rows.Length; token.ThrowIfCancellationRequested();
        }
        catch (AccountReceiveConfigurationChangedException) { state = AccountReceiveState.ConfigurationChanged; detail = "Saved account configuration changed, was disabled or removed. No receive result was published; queue again with current saved settings."; }
        catch (OperationCanceledException) { state = AccountReceiveState.Canceled; detail = "Receive canceled. Any save committed immediately before cancellation remains; inspect saved headers before queueing again."; }
        catch (MailConnectionException error) { state = AccountReceiveState.Failed; detail = error.Message + " No automatic retry; other account jobs continue."; }
        catch (Exception error) when (error is not OutOfMemoryException) { state = AccountReceiveState.Failed; detail = "Receive/save did not finish (" + error.GetType().Name + "). Existing cached mail was retained; no automatic retry. Inspect saved headers before retrying."; }
        finally
        {
            lock (_sync)
            {
                work.State = work.Changed ? AccountReceiveState.ConfigurationChanged : state; work.Detail = work.Changed ? "Saved configuration changed; canceled this job. Any earlier committed cache save remains under its original account." : detail;
                work.Saved = saved; work.Finished = DateTimeOffset.UtcNow; _running--;
                if (!_profiles.Any(profile => profile.Id == work.Profile.Id)) _jobs.Remove(work.Profile.Id);
                Schedule(); CompleteIdle();
            }
            work.Cancellation.Dispose(); Notify();
        }
    }
    private void CompleteIdle() { if (!_jobs.Values.Any(work => work.Active)) _idle.TrySetResult(); }
    private void Notify()
    {
        if (Interlocked.Exchange(ref _notificationPending, 1) != 0) return;
        try { dispatcher.Post(() => { Volatile.Write(ref _notificationPending, 0); lock (_sync) { if (_disposed) return; } Changed?.Invoke(this, EventArgs.Empty); }); }
        catch (ObjectDisposedException) { Volatile.Write(ref _notificationPending, 0); }
    }
    public void Dispose()
    {
        Work[] canceled;
        lock (_sync) { if (_disposed) return; _disposed = true; canceled = _jobs.Values.Where(work => work.Active).ToArray(); foreach (var work in canceled) RequestCancel(work, changed: false); _pending.Clear(); CompleteIdle(); }
        CancelTokens(canceled);
    }
    private static TaskCompletionSource CompletedSource() { var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); source.SetResult(); return source; }
    private sealed class Work(AccountProfile profile)
    {
        public readonly AccountProfile Profile = profile;
        public readonly CancellationTokenSource Cancellation = new();
        public AccountReceiveState State = AccountReceiveState.Queued;
        public bool Started, CancelRequested, Changed;
        public int Saved;
        public DateTimeOffset? Finished;
        public string Detail = "Queued explicitly; waiting for one of two receive slots.";
        public bool Active => State is AccountReceiveState.Queued or AccountReceiveState.Receiving or AccountReceiveState.Canceling;
    }
}
