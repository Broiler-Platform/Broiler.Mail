using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.Mail.Windows.Services;

namespace Broiler.Mail.ProviderProbe;

/// <summary>Explicit opt-in live read-only probe. Never instantiated by application startup or CI.</summary>
internal static partial class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (OperatingSystem.IsWindows() && args is ["--read-only-header-snapshot", "--profile", var snapshotProfile, "--output", var snapshotOutput])
            return await ReadHeaderSnapshotAsync(snapshotProfile, snapshotOutput);
        if (OperatingSystem.IsWindows() && args is ["--plan-self-send", "--profile", var planProfile, "--attempt", var planId] && Guid.TryParse(planId, out var planned))
        {
            var selected = await LoadSelectedAsync(planProfile, default);
            Console.WriteLine($"Attempt: {planned:D}\nAccount snapshot: {SelfSendAcceptance.Fingerprint(selected)}\nRecipient: configured account itself\nSubject: {SelfSendAcceptance.Draft(selected, planned).Subject}\nBody: This is a Broiler.Mail acceptance test sent to the configured account itself. No reply is needed. Test ID: {planned:D}");
            return 0;
        }
        if (OperatingSystem.IsWindows() && args is ["--send-to-self", "--profile", var sendProfile, "--attempt", var sendId, "--expected-account", var expected, "--output", var sendOutput] && Guid.TryParse(sendId, out var attempt))
            return await SendSelfAsync(sendProfile, attempt, expected, sendOutput);
        if (args is not ["--read-only", "--profile", var profile, "--output", var output] || !OperatingSystem.IsWindows())
        { Console.Error.WriteLine("Windows only: --read-only --profile <directory> --output <sanitized-json>. No message is sent in read-only mode. Self-message modes require a separately approved plan and one-shot attempt."); return 2; }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var stages = new List<Stage>(); AccountProfile? account = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var token = deadline.Token;
        try
        {
            // Do not open/adopt/migrate the profile, repair the cache, or create a collection.
            if (File.Exists(Path.Combine(profile, "account-collection.json")))
            {
                var state = await new JsonAccountCollectionStore(profile).LoadStateAsync(token);
                account = state.Accounts.Single(row => row.Id == state.SelectedAccountId);
            }
            else account = (await new JsonAccountStore(Path.Combine(profile, "accounts.json")).LoadAsync(token)).Single();
            stages.Add(new("load-existing-account", "passed"));
        }
        catch (Exception error) when (error is not OutOfMemoryException) { stages.Add(new("load-existing-account", "failed", error.GetType().Name)); }
        if (account is not null)
        {
            var credentials = new ReadOnlyCredentials(); var receiver = new ImapMailReceiver(credentials);
            MailInboxPage? page = null;
            await Check("imap-tls-auth", () => receiver.TestConnectionAsync(account, token));
            await CheckCount("imap-folder-discovery", async () => { var folders = await receiver.GetFoldersAsync(account, token); return folders.Count; });
            await CheckCount("imap-examine-header-page", async () =>
            {
                page = await receiver.GetInboxAsync(account, 3, cancellationToken: token);
                if (page.Messages.Any(row => row.Key.AccountId != account.Id || row.Key.MailboxId != "INBOX" || row.Key.Uid == 0 || row.Key.UidValidity == 0)) throw new InvalidDataException();
                return page.Messages.Count;
            });
            if (page?.Messages.FirstOrDefault() is {} message)
            {
                await Check("imap-body-peek", async () =>
                {
                    var body = await receiver.GetBodyAsync(account, message.Key, token);
                    if (body.Key != message.Key) throw new InvalidDataException();
                });
                await Check("imap-original-peek-size-verification", async () =>
                {
                    var source = await receiver.GetSourceAsync(account, message.Key, token);
                    try { if (source.Key != message.Key || source.Bytes.Length is < 1 or > MailMessageSource.MaximumBytes) throw new InvalidDataException(); }
                    finally { Array.Clear(source.Bytes); }
                });
            }
            else
            {
                stages.Add(new("imap-body-peek", "not-run", "NoHeaderAvailable"));
                stages.Add(new("imap-original-peek-size-verification", "not-run", "NoHeaderAvailable"));
            }
            await Check("smtp-tls-auth-quit", () => new SmtpConnectionTester(credentials).TestConnectionAsync(account, token));
        }
        var report = new Report(1, DateTimeOffset.UtcNow, System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            account?.IncomingServer.Host, account?.OutgoingServer?.Host,
            account is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.Id.ToString()))).ToLowerInvariant(),
            stages, "Read-only EXAMINE/BODY.PEEK and SMTP authentication/QUIT only; no send, flags, moves, deletion, APPEND, cache/profile or credential writes. No credentials, addresses, message headers/bodies, UIDs or protocol transcripts are recorded.");
        string json = JsonSerializer.Serialize(report, ProbeJson.Default.Report);
        string destination = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, json); Console.WriteLine(json);
        return stages.All(stage => stage.Status == "passed") ? 0 : 1;

        async Task Check(string name, Func<Task> action)
        { await CheckCount(name, async () => { await action(); return (int?)null; }); }
        async Task CheckCount(string name, Func<Task<int?>> action)
        {
            try { int? count = await action(); stages.Add(new(name, "passed", Count: count)); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { stages.Add(new(name, "failed", error is MailConnectionException connection ? connection.Failure.ToString() : error is OperationCanceledException ? "TimeoutOrCanceled" : error.GetType().Name)); }
        }
    }
    private sealed class ReadOnlyCredentials : ICredentialStore
    {
        private readonly WindowsCredentialStore _store = new();
        public Task<string?> ReadAsync(CredentialKey key, CancellationToken token = default) => _store.ReadAsync(key, token);
        public Task WriteAsync(CredentialKey key, string secret, CancellationToken token = default) => throw new InvalidOperationException("Probe cannot write credentials.");
        public Task DeleteAsync(CredentialKey key, CancellationToken token = default) => throw new InvalidOperationException("Probe cannot remove credentials.");
    }
}
internal sealed record Stage(string Name, string Status, string? Failure = null, int? Count = null);
internal sealed record Report(int SchemaVersion, DateTimeOffset CheckedAtUtc, string Runtime, string? ImapHost, string? SmtpHost, string? AccountFingerprint, IReadOnlyList<Stage> Stages, string Scope);
[JsonSerializable(typeof(Report))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class ProbeJson : JsonSerializerContext;
