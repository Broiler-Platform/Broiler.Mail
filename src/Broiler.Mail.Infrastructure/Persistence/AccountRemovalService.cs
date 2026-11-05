using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Infrastructure.Persistence;

public sealed class AccountRemovalService(IAccountCollectionStore accounts, IProfileStorage storage, ICredentialStore credentials) : IAccountRemovalService
{
    public async Task RemoveAsync(AccountProfile expected, bool forgetPasswords, Action<string> backupCreated, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(expected, cancellationToken).ConfigureAwait(false);
        string backup = await storage.CreateBackupAsync(Path.Combine(storage.ProfileDirectory, "backups", "before-account-removal-" + Guid.NewGuid().ToString("N")), cancellationToken).ConfigureAwait(false);
        backupCreated(backup);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureReadyAsync(expected, cancellationToken).ConfigureAwait(false);
        // Forget first: a crash/partial failure leaves the account available for repair/retry.
        // The OS vault and JSON store cannot participate in a single transaction.
        if (forgetPasswords)
        {
            try
            {
                await credentials.DeleteAsync(CredentialKey.ForDeletion(expected.Id, MailProtocol.Imap), cancellationToken).ConfigureAwait(false);
                await credentials.DeleteAsync(CredentialKey.ForDeletion(expected.Id, MailProtocol.Smtp), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { throw new IOException("Password cleanup did not finish. The account remains listed; one password may already be forgotten. You can retry removal.", error); }
        }
        try
        {
            await EnsureReadyAsync(expected, cancellationToken).ConfigureAwait(false);
            await accounts.RemoveIfUnchangedAsync(expected, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { throw new IOException(forgetPasswords ? "The account remains listed. Passwords were forgotten; review the account or retry removal." : "The account remains listed. Passwords and cached mail were kept.", error); }
    }
    private async Task EnsureReadyAsync(AccountProfile expected, CancellationToken cancellationToken)
    {
        var state = await accounts.LoadStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.SelectedAccountId != expected.Id || !AccountProfileConfiguration.Matches(expected, state.Accounts.SingleOrDefault(profile => profile.Id == expected.Id)))
            throw new InvalidOperationException("The selected account changed. Review it before removing it.");
        var drafts = await new JsonDraftStore(Path.Combine(storage.ProfileDirectory, "drafts.json")).LoadAsync(cancellationToken).ConfigureAwait(false);
        if (drafts.Draft?.Draft.AccountId == expected.Id || (drafts.SavedDrafts ?? []).Any(row => row.Draft.AccountId == expected.Id))
            throw new InvalidOperationException("Resolve this account's retained draft or submission record before removing it.");
        var journal = new MutationIntentJournal(Path.Combine(storage.ProfileDirectory, "mail-cache.db"));
        if (await journal.BlocksAccountRemovalAsync(expected.Id, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Resolve this account's active, confirmed or uncertain mail changes before removing it. Repair or review them in Local mail storage; no server operation was repeated.");
    }

}
