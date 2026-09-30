// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   8
// Annotated:        8/8
// Exempt:           8
// Human-reviewed:   0/8
// IP risk:          Low
// Security risk:    High
// Criteria:         7/7
// Resource impact:  4/10 max
// Unverified:       8
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.Persistence;

/// <summary>Coalesces immutable edit snapshots through one writer. Flushing never waits on a UI dispatcher.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=1817C8
// Broiler-Falsified-If: an edit made while an older snapshot is being saved is reported as saved although the store holds only the older snapshot
// Broiler-Human:        PENDING
public sealed class DraftJournal(IDraftStore store, DraftStoreState initial)
{
    private readonly object _gate = new();
    private DraftSnapshot? _latest = initial.Draft;
    private long _revision = initial.Revision;
    private long _version, _savedVersion;
    private bool _running;
    private Task _worker = Task.CompletedTask;
    private string? _error;
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=8C487F
    // Broiler-Human:        PENDING
    public event EventHandler? Changed;
    public bool IsPersistent => store.IsPersistent;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=4ABDDC
    // Broiler-Falsified-If: IsSaved reports true after an Update with save disabled whose snapshot the store has not written
    // Broiler-Human:        PENDING
    public bool IsSaved { get { lock (_gate) return _version == _savedVersion; } }
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=797535
    // Broiler-Falsified-If: Error still returns the previous failure message after a retry has started and the store accepted the write
    // Broiler-Human:        PENDING
    public string? Error { get { lock (_gate) return _error; } }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=CB9BC3
    // Broiler-Falsified-If: an Update arriving while the worker is saving an older snapshot is never written, leaving the store with the older draft
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=792312
    // Broiler-Falsified-If: FlushAsync returns true while a snapshot passed to Update has not been written by the store
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=447205
    // Broiler-Falsified-If: two WriteAsync loops run at once and the second save fails with a draft conflict against this instance's own write
    // Broiler-Human:        PENDING
    private void StartWorker()
    {
        if (_running || _savedVersion == _version) return;
        _running = true;
        _error = null;
        _worker = Task.Run(WriteAsync);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=A99010
    // Broiler-Falsified-If: an edit made while SaveAsync is in flight is marked saved because the saved version takes the current version rather than the version that was written
    // Broiler-Human:        PENDING
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
