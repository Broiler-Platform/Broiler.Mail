using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.Persistence;

/// <summary>Coalesces immutable edit snapshots through one writer. Flushing never waits on a UI dispatcher.</summary>
public sealed class DraftJournal(IDraftStore store, DraftStoreState initial)
{
    private readonly object _gate = new();
    private DraftSnapshot? _latest = initial.Draft;
    private long _revision = initial.Revision;
    private long _version, _savedVersion;
    private bool _running;
    private Task _worker = Task.CompletedTask;
    private string? _error;
    public event EventHandler? Changed;
    public bool IsPersistent => store.IsPersistent;
    public bool IsSaved { get { lock (_gate) return _version == _savedVersion; } }
    public string? Error { get { lock (_gate) return _error; } }

    public void Update(DraftSnapshot? snapshot, bool save = true)
    {
        lock (_gate)
        {
            _latest = snapshot;
            _version++;
            if (save) StartWorker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> FlushAsync()
    {
        Task worker;
        lock (_gate) { StartWorker(); worker = _worker; }
        while (true)
        {
            await worker.ConfigureAwait(false);
            lock (_gate)
            {
                if (_error is not null) return false;
                if (_savedVersion == _version) return true;
                StartWorker(); worker = _worker;
            }
        }
    }

    private void StartWorker()
    {
        if (_running || _savedVersion == _version) return;
        _running = true;
        _error = null;
        _worker = Task.Run(WriteAsync);
    }

    private async Task WriteAsync()
    {
        while (true)
        {
            DraftSnapshot? snapshot;
            long version, revision;
            lock (_gate)
            {
                if (_savedVersion == _version) { _running = false; return; }
                snapshot = _latest; version = _version; revision = _revision;
            }
            try
            {
                var saved = await store.SaveAsync(revision, snapshot).ConfigureAwait(false);
                lock (_gate) { _revision = saved.Revision; _savedVersion = version; }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                lock (_gate)
                {
                    _error = error is DraftConflictException ? error.Message : "Draft not saved. Check disk access and available space, then retry Save draft. Your edits remain open.";
                    _running = false;
                }
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
