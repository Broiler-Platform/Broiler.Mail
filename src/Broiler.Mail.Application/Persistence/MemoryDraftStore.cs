using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.Persistence;

/// <summary>Explicit demo/headless storage; the production Windows composition uses JsonDraftStore.</summary>
public sealed class MemoryDraftStore : IDraftStore
{
    private readonly object _gate = new();
    private DraftStoreState _state = new(0, null);
    public bool IsPersistent => false;
    public Task<DraftStoreState> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_state);
    }
    public Task<DraftStoreState> SaveAsync(long expectedRevision, DraftSnapshot? draft, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_state.Revision != expectedRevision) throw new DraftConflictException();
            return Task.FromResult(_state = new(_state.Revision + 1, draft));
        }
    }
}
