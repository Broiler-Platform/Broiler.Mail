// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   3
// Annotated:        3/3
// Exempt:           0
// Human-reviewed:   0/3
// IP risk:          Low
// Security risk:    High
// Criteria:         3/3
// Resource impact:  1/10 max
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Application;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.Mail.Windows.Services;
using System.Diagnostics;

namespace Broiler.Mail.Windows;

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=B958AC
// Broiler-Falsified-If: the default data directory resolves to a location shared between Windows users instead of the current user's local application data
// Broiler-Human:        PENDING
internal static class CompositionRoot
{
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=1; Fingerprint=3777B3
    // Broiler-Falsified-If: the returned path lies outside the current user's LocalApplicationData folder
    // Broiler-Human:        PENDING
    public static string DefaultDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Broiler.Mail");

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=1091D3
    // Broiler-Falsified-If: the accounts, settings or drafts store is given a file outside the data directory passed in
    // Broiler-Human:        PENDING
    public static MailApplication CreateApplication(string? dataDirectory = null)
    {
        dataDirectory ??= DefaultDataDirectory;
        var profile = MailProfile.Open(dataDirectory);
        var credentials = new WindowsCredentialStore();
        try
        {
            var cache = new SqliteMailCache(profile.CachePath, profile.Manifest.CacheMaximumBytes);
            cache.RepairAsync().GetAwaiter().GetResult();
            var accounts = new JsonAccountCollectionStore(profile.DirectoryPath);
            var storage = new ProfileStorageService(profile, cache);
            return new MailApplication(
                accounts,
                new JsonSettingsStore(profile.SettingsPath),
                new ImapMailReceiver(credentials, new MutationIntentJournal(profile.CachePath)),
                new SmtpMailSender(credentials), credentials,
                new JsonDraftStore(profile.DraftsPath), new ImapSentCopyWriter(credentials),
                new SmtpConnectionTester(credentials), cache, profile,
                storage, OpenProfile, new AccountRemovalService(accounts, storage, credentials), new JsonRecipientBook(Path.Combine(profile.DirectoryPath, "recipients.json")), new JsonOfflineFolderStore(Path.Combine(profile.DirectoryPath, "offline-folders.json")), new JsonAccountNavigationStore(Path.Combine(profile.DirectoryPath, "account-navigation.json")), new FileMessageSourceExports(profile.DirectoryPath, storage));
        }
        catch { profile.Dispose(); throw; }
    }

    private static void OpenProfile(string directory)
    {
        string executable = Path.Combine(AppContext.BaseDirectory, "Broiler.Mail.Windows.exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        start.ArgumentList.Add("--data-directory"); start.ArgumentList.Add(directory);
        using var process = Process.Start(start) ?? throw new IOException("A new Mail window could not be started.");
    }
}
