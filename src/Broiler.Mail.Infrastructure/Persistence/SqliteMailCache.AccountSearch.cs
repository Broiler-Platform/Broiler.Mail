using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Broiler.Mail.Core.Accounts;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Infrastructure.Persistence;

public sealed partial class SqliteMailCache
{
    private static string AccountMailboxSnapshot(SqliteConnection connection, AccountId account, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(account.ToString()));
        using var command = connection.CreateCommand(); command.CommandText = "SELECT mailbox,epoch FROM mailbox_heads WHERE account=$a ORDER BY mailbox COLLATE BINARY";
        command.Parameters.AddWithValue("$a", account.ToString()); using var reader = command.ExecuteReader();
        int count = 0; Span<byte> number = stackalloc byte[4];
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            string name = reader.GetString(0); long epoch = reader.GetInt64(1);
            if (++count > 512 || name.Length is <1 or >1024 || name.Any(char.IsControl) || epoch is <1 or >uint.MaxValue)
                throw new InvalidDataException("All-folder search supports at most 512 bounded saved mailbox heads; use a current-folder search or repair invalid metadata.");
            byte[] bytes = Encoding.UTF8.GetBytes(name); BinaryPrimitives.WriteUInt32BigEndian(number,(uint)bytes.Length); hash.AppendData(number); hash.AppendData(bytes);
            BinaryPrimitives.WriteUInt32BigEndian(number,(uint)epoch); hash.AppendData(number);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
