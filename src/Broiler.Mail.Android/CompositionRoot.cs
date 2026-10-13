using Broiler.Mail.Application;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Android;

internal static class CompositionRoot
{
    public static MailApplication Create(string directory)
    {
        var credentials = new AndroidCredentialStore(Path.Combine(directory, "credentials"));
        return new MailApplication(
            new JsonAccountStore(Path.Combine(directory, "accounts.json")),
            new JsonSettingsStore(Path.Combine(directory, "settings.json")),
            new ImapMailReceiver(credentials), new SmtpMailSender(credentials), credentials,
            new JsonDraftStore(Path.Combine(directory, "drafts.json")),
            new ImapSentCopyWriter(credentials), new SmtpConnectionTester(credentials));
    }
}
