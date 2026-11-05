using System.Security.Cryptography;
using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.ProviderProbe;

/// <summary>A single explicitly approved self-message attempt; a durable start prevents reuse even after unknown outcomes.</summary>
internal static class SelfSendAcceptance
{
    internal static string Fingerprint(AccountProfile account) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\0", account.Id, account.EmailAddress, account.IncomingServer.Host, account.IncomingServer.Port,
            account.IncomingServer.UserName, account.IncomingServer.Security, account.OutgoingServer?.Host,
            account.OutgoingServer?.Port, account.OutgoingServer?.UserName, account.OutgoingServer?.Security, account.IsEnabled)))).ToLowerInvariant();
    internal static MailDraft Draft(AccountProfile account, Guid attempt) => new()
    {
        Id = attempt, AccountId = account.Id, FromAddress = account.EmailAddress, FromDisplayName = "Broiler.Mail acceptance check",
        To = [account.EmailAddress], Subject = "Broiler.Mail acceptance check " + attempt.ToString("N"), SubmissionDate = DateTimeOffset.UtcNow,
        PlainText = "This is a Broiler.Mail acceptance test sent to the configured account itself.\r\nNo reply is needed.\r\nTest ID: " + attempt.ToString("D") + "\r\n"
    };
    internal static async Task<SubmissionStatus> SendAsync(AccountProfile account, string expectedFingerprint, Guid attempt, string journalDirectory,
        IMailSender sender, CancellationToken token = default)
    {
        if (attempt == Guid.Empty || !string.Equals(Fingerprint(account), expectedFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The approved account snapshot or attempt is no longer valid.");
        var draft = Draft(account, attempt); MailComposition.ValidateDraft(account, draft); token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(journalDirectory);
        string prefix = Path.Combine(Path.GetFullPath(journalDirectory), attempt.ToString("N"));
        await using (var journal = new FileStream(prefix + ".started", FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await journal.WriteAsync(Encoding.UTF8.GetBytes("broiler-self-send-v1\n" + expectedFingerprint + "\n" + DateTimeOffset.UtcNow.ToString("O")), token);
            await journal.FlushAsync(token); journal.Flush(flushToDisk: true);
        }
        token.ThrowIfCancellationRequested();
        var result = await sender.SendAsync(account, draft, token); // Exactly one call; no automatic retry.
        await using (var receipt = new FileStream(prefix + ".result", FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await receipt.WriteAsync(Encoding.ASCII.GetBytes(result.Status.ToString()), CancellationToken.None);
            receipt.Flush(flushToDisk: true);
        }
        return result.Status;
    }
}
