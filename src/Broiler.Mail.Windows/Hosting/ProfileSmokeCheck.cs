using System.Text;
using System.Diagnostics;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.Mail.Windows.Services;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>Published-binary serialization and credential check using only a fresh synthetic account.</summary>
internal static class ProfileSmokeCheck
{
    private static AccountProfile FixtureProfile(AccountId id) => new()
    {
        Id = id, DisplayName = "Published smoke fixture", EmailAddress = "smoke@example.test",
        FolderOverrides = new() { Sent = "Envoyes", Drafts = "Brouillons", Archive = "Archive", Trash = "Bin", Junk = "Spam" },
        IncomingServer = new() { Host = "imap.example.test", Port = 993, UserName = "smoke@example.test" },
    };

    private static string FixtureDirectory(AccountId id)
    {
        if (id.Value == Guid.Empty) throw new ArgumentException("Synthetic account ID is required.", nameof(id));
        string smokeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Broiler.Mail.Smoke")) + Path.DirectorySeparatorChar;
        string directory = Path.GetFullPath(Path.Combine(smokeRoot, id.Value.ToString("N")));
        if (!directory.StartsWith(smokeRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Smoke directory must remain under the temporary smoke root.");
        return directory;
    }

    // The child accepts only a GUID; callers cannot point this diagnostic at an arbitrary profile.
    public static async Task ReopenAsync(AccountId id, CancellationToken cancellationToken = default)
    {
        var expected = FixtureProfile(id);
        string directory = FixtureDirectory(id);
        using var profile = MailProfile.Open(directory);
        if (profile.Manifest.DownloadPolicy != OfflineDownloadPolicy.HeadersOnly || profile.Manifest.CacheMaximumBytes != 64 * 1024 * 1024)
            throw new InvalidOperationException("Published storage policy/quota restart fixture failed.");
        var restored = await new JsonAccountStore(Path.Combine(directory, "accounts.json")).LoadAsync(cancellationToken);
        if (restored.Count != 1 || restored[0] != expected ||
            await new JsonSettingsStore(Path.Combine(directory, "settings.json")).LoadAsync(cancellationToken) != ExpectedSettings ||
            await new WindowsCredentialStore().ReadAsync(CredentialKey.For(expected, MailProtocol.Imap), cancellationToken) != Secret)
            throw new InvalidOperationException("Published profile/credential restart fixture failed.");
        var cache = new SqliteMailCache(profile.CachePath, profile.Manifest.CacheMaximumBytes);
        var mailbox = await cache.GetLatestMailboxAsync(id, "INBOX", cancellationToken);
        var savedBody = mailbox is null ? null : await cache.ReadBodyAsync(mailbox.Key, 1, cancellationToken);
        if (savedBody?.PlainText != "Persisted offline fixture" || savedBody.Composition?.MessageId != "<offline@example.test>" ||
            savedBody.EmbeddedImages["fixture"].Data.Length != 3)
            throw new InvalidOperationException("Published offline cache restart fixture failed.");
        await FolderNavigationSmokeCheck.RunAsync(expected, cache);
    }

    private const string Secret = "synthetic-published-smoke-only";
    private static ApplicationSettings ExpectedSettings => new() { Theme = AppTheme.Dark, WindowWidth = 1200 };

    public static async Task RunAsync(bool separateProcess = false)
    {
        var profile = FixtureProfile(AccountId.New());
        string directory = FixtureDirectory(profile.Id);
        var key = CredentialKey.For(profile, MailProtocol.Imap);
        var credentials = new WindowsCredentialStore();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await SavedOriginalLibrarySmokeCheck.RunAsync(Path.Combine(directory, "saved-original-library-smoke"), profile);
            await UnifiedSavedInboxSmokeCheck.RunAsync(Path.Combine(directory, "unified-saved-inbox-smoke"), profile);
            await AccountSearchSmokeCheck.RunAsync(Path.Combine(directory, "account-search-smoke"), profile);
            await MailboxSnapshotSmokeCheck.RunAsync(Path.Combine(directory, "mailbox-snapshot-smoke"), profile);
            await AccountReceiveQueueSmokeCheck.RunAsync(Path.Combine(directory, "account-receive-queue-smoke"), profile);
            await CacheRecoverySmokeCheck.RunAsync(Path.Combine(directory, "cache-recovery-smoke"), profile);
            await DraftLibrarySmokeCheck.RunAsync(Path.Combine(directory, "draft-library-smoke"), profile);
            await RecipientBookSmokeCheck.RunAsync(Path.Combine(directory, "recipient-book-smoke"), profile);
            await ReceivedAttachmentSmokeCheck.RunAsync(Path.Combine(directory, "received-attachment-smoke"), profile);
            await OutboxSmokeCheck.RunAsync(Path.Combine(directory, "outbox-smoke"), profile);
            await OriginalSourceSmokeCheck.RunAsync(Path.Combine(directory, "original-source-smoke"), profile);
            await OrphanMailSmokeCheck.RunAsync(Path.Combine(directory, "orphan-mail-smoke"), profile);
            await AccountMailCleanupSmokeCheck.RunAsync(Path.Combine(directory, "account-mail-cleanup-smoke"), profile);
            await AccountNavigationSmokeCheck.RunAsync(Path.Combine(directory, "account-navigation-smoke"), profile);
            await OfflineHeaderSmokeCheck.RunAsync(Path.Combine(directory, "offline-header-smoke"), profile);
            await OfflineFolderSmokeCheck.RunAsync(Path.Combine(directory, "offline-folder-smoke"), profile);
            await SenderIdentitySmokeCheck.RunAsync(Path.Combine(directory, "sender-identity-smoke"), profile);
            await SignatureSmokeCheck.RunAsync(Path.Combine(directory, "signature-smoke"), profile);
            await SearchPagingSmokeCheck.RunAsync(Path.Combine(directory, "search-paging-smoke.db"), profile);
            await PermanentDeleteSmokeCheck.RunAsync(Path.Combine(directory, "delete-smoke.db"), profile);
            await MoveWorkflowSmokeCheck.RunAsync(Path.Combine(directory, "move-ui-smoke.db"), profile);
            await AccountSelectionSmokeCheck.RunAsync(Path.Combine(directory, "account-selection-smoke"));
            string accounts = Path.Combine(directory, "accounts.json");
            string settings = Path.Combine(directory, "settings.json");
            await new JsonAccountStore(accounts).SaveAsync(profile, timeout.Token);
            await new JsonSettingsStore(settings).SaveAsync(ExpectedSettings, timeout.Token);
            await credentials.WriteAsync(key, Secret, timeout.Token);
            using (var storedProfile = MailProfile.Open(directory))
            {
                var cache = new SqliteMailCache(storedProfile.CachePath, storedProfile.Manifest.CacheMaximumBytes);
                var mailbox = new MailboxKey(profile.Id, "INBOX", 7);
                var messageKey = new MailMessageKey(profile.Id, "INBOX", 7, 1);
                await cache.SaveSummariesAsync(mailbox, [new() { Key = messageKey, Sender = "Synthetic sender", Subject = "Offline fixture" }], DateTimeOffset.UtcNow, timeout.Token);
                await cache.SaveBodyAsync(mailbox, new(messageKey, "Persisted offline fixture", "<p>Persisted offline fixture</p>")
                {
                    Composition = new() { MessageId = "<offline@example.test>", From = ["sender@example.test"] },
                    EmbeddedImages = new Dictionary<string, MailEmbeddedImage> { ["fixture"] = new("fixture", "image/png", [1, 2, 3]) },
                }, timeout.Token);
                var archiveKey = new MailMessageKey(profile.Id, "Archive", 7, 1);
                var archive = new MailboxKey(profile.Id, "Archive", 7);
                await cache.SaveSummariesAsync(archive, [new() { Key = archiveKey, Sender = "Archive sender", Subject = "Archive fixture" }], DateTimeOffset.UtcNow, timeout.Token);
                await cache.SaveBodyAsync(archive, new(archiveKey, "Persisted archive fixture"), timeout.Token);
                await new ProfileStorageService(storedProfile, cache).SetPolicyAsync(new(64 * 1024 * 1024, OfflineDownloadPolicy.HeadersOnly), timeout.Token);
                // Recreate the legacy v1 layout in this fresh synthetic fixture only.
                // Reopen must upgrade it with bodies and account/mailbox identities intact.
                using var legacy = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                { DataSource = storedProfile.CachePath, Pooling = false }.ToString());
                legacy.Open(); using var downgrade = legacy.CreateCommand();
                downgrade.CommandText = "DROP TABLE mailbox_snapshots; DROP TABLE mailbox_snapshot_uids; DROP TRIGGER search_insert; DROP TRIGGER search_update; DROP TRIGGER search_delete; DROP TABLE cache_search_state; ALTER TABLE messages DROP COLUMN is_starred; PRAGMA user_version=1;";
                downgrade.ExecuteNonQuery();
            }

            // New service instances exercise reload through the generated JSON metadata after trimming.
            await ReopenAsync(profile.Id, timeout.Token);
            if (separateProcess)
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--profile-smoke-reopen");
                start.ArgumentList.Add(profile.Id.Value.ToString("N"));
                using var child = Process.Start(start) ?? throw new IOException("Cannot start profile reopen fixture.");
                try
                {
                    await child.WaitForExitAsync(timeout.Token);
                    if (child.ExitCode != 0) throw new InvalidOperationException("Published profile second-process reopen failed.");
                }
                finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
            }

            using var mime = new MemoryStream(Encoding.UTF8.GetBytes(
                "From: sender@example.test\r\nTo: smoke@example.test\r\nSubject: Published fixture\r\n" +
                "MIME-Version: 1.0\r\nContent-Type: multipart/alternative; boundary=smoke\r\n\r\n" +
                "--smoke\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nPlain fixture\r\n" +
                "--smoke\r\nContent-Type: text/html; charset=utf-8\r\n\r\n<p>HTML fixture</p>\r\n--smoke--\r\n"));
            var parsed = await MimeParser.ParseAsync(mime, timeout.Token);
            if (parsed.Subject != "Published fixture" || parsed.TextBody?.Contains("Plain fixture") != true ||
                parsed.HtmlBody?.Contains("HTML fixture") != true)
                throw new InvalidOperationException("Published multipart MIME fixture failed.");
            await VerifyMaintenanceAsync(directory, profile.Id, timeout.Token);
        }
        finally
        {
            // Never reuse the cancelled operation token for credential cleanup.
            try { await credentials.DeleteAsync(key); }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        }
    }

    private static async Task VerifyMaintenanceAsync(string directory, AccountId accountId, CancellationToken token)
    {
        using var profile = MailProfile.Open(directory);
        var cache = new SqliteMailCache(profile.CachePath, profile.Manifest.CacheMaximumBytes);
        var storage = new ProfileStorageService(profile, cache);
        var collection = new JsonAccountCollectionStore(directory);
        var secondAccount = FixtureProfile(AccountId.New()) with { DisplayName = "Second collection fixture", IsEnabled = false };
        await collection.SaveAsync(secondAccount, token);
        await collection.SelectAsync(secondAccount.Id, token);
        await collection.ReorderAsync([secondAccount.Id, accountId], token);
        var reopenedCollection = await new JsonAccountCollectionStore(directory).LoadStateAsync(token);
        if (reopenedCollection.Accounts.Length != 2 || reopenedCollection.SelectedAccountId != secondAccount.Id ||
            reopenedCollection.Accounts[0] != secondAccount)
            throw new InvalidOperationException("Published account collection reopen failed.");
        byte[] attachmentBytes = new byte[4 * 1024 * 1024];
        attachmentBytes[0] = 255;
        attachmentBytes[^1] = 46;
        var draft = new DraftSnapshot { Draft = MailComposition.Create(FixtureProfile(accountId)) with
            { PlainText = "Backup draft fixture", Attachments = [new("fixture.bin", attachmentBytes)] },
            ToText = "raw recipient", CcText = "", BccText = "", State = DraftSubmissionState.Editing };
        await new JsonDraftStore(profile.DraftsPath).SaveAsync(0, draft, token);
        string backup = await storage.CreateBackupAsync(Path.Combine(directory, "backups", "verified-smoke"), token);
        var cleared = await storage.ClearDownloadedMailAsync(Path.Combine(directory, "backups", "before-clear-smoke"), token);
        if ((await storage.GetInfoAsync(token)).HeaderCount != 0 ||
            (await new JsonDraftStore(profile.DraftsPath).LoadAsync(token)).Draft?.Draft.Id != draft.Draft.Id ||
            await new WindowsCredentialStore().ReadAsync(CredentialKey.For(FixtureProfile(accountId), MailProtocol.Imap), token) != Secret)
            throw new InvalidOperationException("Published cleanup did not preserve local draft/credential state.");
        string restored = await storage.RestoreBackupAsync(backup, Path.Combine(directory, "restored-smoke"), token);
        using var restoredProfile = MailProfile.Open(restored);
        var restoredDraft = await new JsonDraftStore(restoredProfile.DraftsPath).LoadAsync(token);
        if (restoredDraft.Draft?.Draft.Attachments?.SingleOrDefault() is not { FileName: "fixture.bin" } attachment ||
            !attachment.Content.Span.SequenceEqual(attachmentBytes))
            throw new InvalidOperationException("Published backup did not preserve draft attachment bytes.");
        var restoredCollection = await new JsonAccountCollectionStore(restored).LoadStateAsync(token);
        if (restoredCollection.Accounts.Length != 2 || restoredCollection.SelectedAccountId != secondAccount.Id ||
            restoredCollection.Accounts[0] != secondAccount)
            throw new InvalidOperationException("Published account collection backup/restore failed.");
        if (restoredProfile.Manifest.DownloadPolicy != OfflineDownloadPolicy.HeadersOnly || restoredProfile.Manifest.CacheMaximumBytes != 64 * 1024 * 1024)
            throw new InvalidOperationException("Published backup did not preserve storage policy/quota.");
        var restoredCache = new SqliteMailCache(restoredProfile.CachePath, restoredProfile.Manifest.CacheMaximumBytes);
        var body = await restoredCache.ReadBodyAsync(new(accountId, "INBOX", 7), 1, token);
        if (body?.PlainText != "Persisted offline fixture" || !Directory.Exists(cleared.BackupDirectory))
            throw new InvalidOperationException("Published verified backup/restore fixture failed.");
        var removalCredentials = new WindowsCredentialStore();
        var secondImapKey = CredentialKey.For(secondAccount, MailProtocol.Imap);
        // Exercise cleanup of an old SMTP slot after outgoing settings were removed.
        var secondSmtpKey = CredentialKey.For(secondAccount with { OutgoingServer = secondAccount.IncomingServer }, MailProtocol.Smtp);
        try
        {
            await removalCredentials.WriteAsync(secondImapKey, "synthetic removal IMAP", token);
            await removalCredentials.WriteAsync(secondSmtpKey, "synthetic removal SMTP", token);
            var secondMailbox = new MailboxKey(secondAccount.Id, "INBOX", 7);
            await cache.SaveSummariesAsync(secondMailbox, [new() { Key = new(secondAccount.Id, "INBOX", 7, 1), Sender = "Synthetic", Subject = "Retained removal fixture" }], DateTimeOffset.UtcNow, token);
            await cache.SaveBodyAsync(secondMailbox, new(new(secondAccount.Id, "INBOX", 7, 1), "Kept after removal"), token);
            string removalBackup = await AccountSelectionSmokeCheck.RunRemovalAsync(collection, storage, cache, removalCredentials, secondAccount.Id);
            if (await removalCredentials.ReadAsync(secondImapKey, token) is not null || await removalCredentials.ReadAsync(secondSmtpKey, token) is not null ||
                await removalCredentials.ReadAsync(CredentialKey.For(FixtureProfile(accountId), MailProtocol.Imap), token) != Secret ||
                (await cache.ReadBodyAsync(secondMailbox, 1, token))?.PlainText != "Kept after removal")
                throw new InvalidOperationException("Published account removal changed another credential or lost local mail.");
            string removalRestored = await storage.RestoreBackupAsync(removalBackup, Path.Combine(directory, "account-removal-restored"), token);
            if ((await new JsonAccountCollectionStore(removalRestored).LoadStateAsync(token)).SelectedAccountId != secondAccount.Id)
                throw new InvalidOperationException("Published removed-account recovery snapshot failed.");
        }
        finally
        {
            try { await removalCredentials.DeleteAsync(CredentialKey.ForDeletion(secondAccount.Id, MailProtocol.Imap)); }
            finally { await removalCredentials.DeleteAsync(CredentialKey.ForDeletion(secondAccount.Id, MailProtocol.Smtp)); }
        }
    }
}
