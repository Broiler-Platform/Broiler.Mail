namespace Broiler.Mail.Core.Services;

/// <summary>Local repair of already-confirmed server changes. Never repeats a network mutation.</summary>
public interface IMailCacheRecovery
{
    int PendingRecoveryCount { get; }
    Task<int> GetPendingRecoveryCountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MailUncertainAction>> ReadUncertainActionsAsync(CancellationToken cancellationToken = default);
    Task DismissUncertainActionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MailCacheRecoveryReport> RepairAsync(CancellationToken cancellationToken = default);
}
public sealed record MailCacheRecoveryReport(int Repaired, int Pending);

public sealed record MailUncertainAction(Guid Id, Broiler.Mail.Core.Messages.MailMessageKey Source, string? DestinationMailbox, string Description);
