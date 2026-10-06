// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   3
// Annotated:        3/3
// Exempt:           7
// Human-reviewed:   0/3
// IP risk:          Low
// Security risk:    Low
// Criteria:         2/0
// Resource impact:  3/10 max
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Application.ViewModels;

// Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=2CB11B
// Broiler-Falsified-If: after the account profile is saved with new settings, Inbox or Composer still holds the previous AccountProfile
// Broiler-Human:        PENDING
public sealed class MailShellViewModel
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=0805D1
    // Broiler-Falsified-If: a later Account.Changed leaves Inbox or Composer on the profile that was current at construction
    // Broiler-Human:        PENDING
    public MailShellViewModel(AccountProfileViewModel account, SettingsViewModel settings, InboxViewModel inbox, ComposerViewModel? composer = null)
    {
        Account = account;
        Settings = settings;
        Inbox = inbox;
        Composer = composer ?? new ComposerViewModel();
        // The remembered split applies to this session; the host saves it again with the window layout.
        Inbox.SplitterFraction = settings.Settings.InboxSplitterFraction;
        Compose = new CompositionCommands(Composer, Inbox);
        Composer.SetAccount(account.Profile);
        Inbox.SetAccount(account.Profile);
        Account.Changed += (_, _) => { Inbox.SetAccount(Account.Profile); Composer.SetAccount(Account.Profile); };
    }
    public string Title => "Broiler.Mail";
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=6B5558
    // Broiler-Human:        PENDING
    public AccountProfile? CurrentAccount => Account.Profile;
    public string Status => Inbox.Status;
    public SettingsViewModel Settings { get; }
    public AccountProfileViewModel Account { get; }
    public InboxViewModel Inbox { get; }
    public ComposerViewModel Composer { get; }
    /// <summary>New, Reply, Reply all, and Forward, shared by the reader and the composer.</summary>
    public CompositionCommands Compose { get; }
}
