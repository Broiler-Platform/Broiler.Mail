using Broiler.Mail.Application;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.Mail.Windows.Services;

namespace Broiler.Mail.Windows;

internal static class CompositionRoot
{
    public static string DefaultDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Broiler.Mail");

    public static MailApplication CreateApplication(string? dataDirectory = null)
    {
        dataDirectory ??= DefaultDataDirectory;
        var credentials = new WindowsCredentialStore();
        return new MailApplication(
            new JsonAccountStore(Path.Combine(dataDirectory, "accounts.json")),
            new JsonSettingsStore(Path.Combine(dataDirectory, "settings.json")),
            new ImapMailReceiver(credentials),
            new SmtpMailSender(credentials), credentials);
    }
}
