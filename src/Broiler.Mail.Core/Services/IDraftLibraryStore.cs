using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>Atomic operations over the active draft and a bounded library. Never sends or retries mail.</summary>
public interface IDraftLibraryStore : IDraftStore
{
    Task<DraftStoreState> ParkAsync(long expectedRevision, CancellationToken token = default);
    Task<DraftStoreState> ActivateAsync(long expectedRevision, Guid id, CancellationToken token = default);
    Task<DraftStoreState> RemoveSavedAsync(long expectedRevision, Guid id, CancellationToken token = default);
}
