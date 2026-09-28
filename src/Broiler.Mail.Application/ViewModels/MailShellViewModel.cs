using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Application.ViewModels;

public sealed class MailShellViewModel
{
    public MailShellViewModel(AccountProfileViewModel account, SettingsViewModel settings, InboxViewModel inbox)
    {
        Account = account;
        Settings = settings;
        Inbox = inbox;
        Inbox.SetAccount(account.Profile);
        Account.Changed += (_, _) => Inbox.SetAccount(Account.Profile);
    }
    public string Title => "Broiler.Mail";
    public AccountProfile? CurrentAccount => Account.Profile;
    public string Status => Inbox.Status;
    public SettingsViewModel Settings { get; }
    public AccountProfileViewModel Account { get; }
    public InboxViewModel Inbox { get; }
}
