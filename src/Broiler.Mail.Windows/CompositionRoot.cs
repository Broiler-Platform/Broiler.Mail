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
        var credentials = new WindowsCredentialStore();
        return new MailApplication(
            new JsonAccountStore(Path.Combine(dataDirectory, "accounts.json")),
            new JsonSettingsStore(Path.Combine(dataDirectory, "settings.json")),
            new ImapMailReceiver(credentials),
            new SmtpMailSender(credentials), credentials,
            new JsonDraftStore(Path.Combine(dataDirectory, "drafts.json")), new ImapSentCopyWriter(credentials),
            new SmtpConnectionTester(credentials));
    }
}
