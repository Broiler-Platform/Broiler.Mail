using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

public interface IDraftStore
{
    bool IsPersistent { get; }
    Task<DraftStoreState> LoadAsync(CancellationToken cancellationToken = default);
    Task<DraftStoreState> SaveAsync(long expectedRevision, DraftSnapshot? draft, CancellationToken cancellationToken = default);
}

public sealed class DraftConflictException() : InvalidOperationException("Another app instance changed the saved draft. Copy your edits before restarting; the newer saved draft was not overwritten.");
