using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;

namespace Broiler.Mail.ProviderProbe;

internal static partial class Program
{
    private static async Task<int> ReadHeaderSnapshotAsync(string profile, string output)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // Reserve a new sanitized report before contacting the provider. Never open/adopt/migrate the profile.
        string destination = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        AccountProfile? account = null; var stages = new List<Stage>(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            account = await LoadSelectedAsync(profile, deadline.Token); stages.Add(new("load-existing-account", "passed"));
            var receiver = new ImapMailReceiver(new ReadOnlyCredentials());
            var snapshot = await receiver.GetMailboxSnapshotAsync(account, "INBOX", deadline.Token);
            if (snapshot.Mailbox.AccountId != account.Id || snapshot.Mailbox.MailboxId != "INBOX" || snapshot.Messages.Count > MailMailboxSnapshot.MaximumMessages || snapshot.Messages.Any(row => !snapshot.Mailbox.Contains(row.Key)) || snapshot.Messages.Select(row => row.Key.Uid).Distinct().Count() != snapshot.Messages.Count)
                throw new InvalidDataException("Invalid complete snapshot identity.");
            stages.Add(new("imap-complete-header-snapshot", "passed", Count: snapshot.Messages.Count));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { stages.Add(new(account is null ? "load-existing-account" : "imap-complete-header-snapshot", "failed", error is MailConnectionException connection ? connection.Failure.ToString() : error is OperationCanceledException ? "TimeoutOrCanceled" : error.GetType().Name)); }
        var report = new Report(1, DateTimeOffset.UtcNow, System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, account?.IncomingServer.Host, null,
            account is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.Id.ToString()))).ToLowerInvariant(), stages,
            "Explicit read-only INBOX full-header observation only: EXAMINE/UID SEARCH ALL/header FETCH with before/after census validation, platform TLS and OS credential reads. No bodies, source, attachments, SMTP, send, flags, moves, deletion, APPEND, profile/cache adoption/migration/write or credential writes. Only provider host, one-way account fingerprint, coarse outcomes and count are recorded; no addresses, headers, UIDs, UID hashes or transcripts.");
        string json = JsonSerializer.Serialize(report, ProbeJson.Default.Report); await file.WriteAsync(Encoding.UTF8.GetBytes(json)); await file.FlushAsync(); file.Flush(flushToDisk: true); Console.WriteLine(json);
        return stages.All(stage => stage.Status == "passed") ? 0 : 1;
    }
}
