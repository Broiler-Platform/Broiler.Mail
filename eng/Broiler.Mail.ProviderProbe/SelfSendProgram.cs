using System.Text.Json;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.ProviderProbe;

internal static partial class Program
{
    private static async Task<AccountProfile> LoadSelectedAsync(string profile, CancellationToken token)
    {
        if (File.Exists(Path.Combine(profile, "account-collection.json")))
        {
            var state = await new JsonAccountCollectionStore(profile).LoadStateAsync(token);
            return state.Accounts.Single(row => row.Id == state.SelectedAccountId);
        }
        return (await new JsonAccountStore(Path.Combine(profile, "accounts.json")).LoadAsync(token)).Single();
    }
    private static async Task<int> SendSelfAsync(string profile, Guid attempt, string expected, string output)
    {
        // This mode is never invoked by the default/read-only probe or by CI.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var token = timeout.Token;
        var stages = new List<Stage>(); AccountProfile? account = null;
        try
        {
            account = await LoadSelectedAsync(profile, token);
            var credentials = new ReadOnlyCredentials();
            var status = await SelfSendAcceptance.SendAsync(account, expected, attempt, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "self-send-attempts"), new SmtpMailSender(credentials), token);
            stages.Add(new("single-self-smtp-submission", status == SubmissionStatus.Accepted ? "passed" : "failed", status == SubmissionStatus.Accepted ? null : status.ToString()));
            if (status == SubmissionStatus.Accepted)
            {
                var receiver = new ImapMailReceiver(credentials); bool received = false;
                for (int index = 0; index < 6 && !received; index++)
                {
                    if (index > 0) await Task.Delay(TimeSpan.FromSeconds(2), token);
                    var page = await receiver.GetInboxAsync(account, 50, cancellationToken: token);
                    foreach (var row in page.Messages.Where(row => row.Subject == SelfSendAcceptance.Draft(account, attempt).Subject))
                    {
                        var source = await receiver.GetSourceAsync(account, row.Key, token);
                        try
                        {
                            var parsed = MimeParser.Parse(source.Bytes);
                            received = parsed.MessageId?.Trim('<', '>') == attempt.ToString("N") + "@broiler.mail";
                        }
                        finally { Array.Clear(source.Bytes); }
                        if (received) break;
                    }
                }
                stages.Add(new("self-message-inbox-observation", received ? "passed" : "not-observed"));
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { stages.Add(new("self-send-probe", "failed", error is MailConnectionException connection ? connection.Failure.ToString() : error.GetType().Name)); }
        var report = new Report(1, DateTimeOffset.UtcNow, System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            account?.IncomingServer.Host, account?.OutgoingServer?.Host, expected, stages,
            $"Explicitly approved single plain-text self-message attempt {attempt:D}; no other recipient, no SMTP retry, no Sent APPEND, flags, moves or deletion. Outcome is retained in the one-shot attempt journal. Inbox observation uses read-only EXAMINE/BODY.PEEK; no private message content is recorded.");
        string destination = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string json = JsonSerializer.Serialize(report, ProbeJson.Default.Report); await File.WriteAllTextAsync(destination, json); Console.WriteLine(json);
        return stages.Count == 2 && stages.All(stage => stage.Status == "passed") ? 0 : 1;
    }
}
