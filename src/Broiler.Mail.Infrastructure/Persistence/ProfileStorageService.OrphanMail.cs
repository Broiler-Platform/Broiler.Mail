using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Infrastructure.Persistence;

public sealed partial class ProfileStorageService
{
    public Task<OrphanDownloadedMailPage> FindOrphanDownloadedMailAsync(CancellationToken token = default)
        => cache.WithExclusiveConnectionAsync(connection =>
        {
            using var locks = AcquireConfigurationLocks(token);
            var configured = new JsonAccountCollectionStore(ProfileDirectory).ReadExistingAccountsAsync(token).GetAwaiter().GetResult();
            using var command = connection.CreateCommand();
            string[] parameters = configured.Select((account,index) =>
            { string name = "$a" + index; command.Parameters.AddWithValue(name, account.Id.ToString()); return name; }).ToArray();
            command.CommandText = "SELECT account FROM (SELECT account FROM messages UNION SELECT account FROM mailbox_heads) " +
                (parameters.Length == 0 ? "" : "WHERE account NOT IN (" + string.Join(',',parameters) + ") ") + "ORDER BY account LIMIT 65";
            var ids = new List<AccountId>();
            using (var rows = command.ExecuteReader())
                while (rows.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (!Guid.TryParseExact(rows.GetString(0), "D", out var id) || id == Guid.Empty || rows.GetString(0) != id.ToString("D"))
                        throw new InvalidDataException("Downloaded mail has an invalid account identity; repair the profile before cleanup.");
                    ids.Add(new(id));
                }
            return new OrphanDownloadedMailPage(Array.AsReadOnly(ids.Take(64).Select(id => ReadAccountMailInfo(connection,id)).ToArray()), ids.Count > 64);
        }, token);
}
