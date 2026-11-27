using Broiler.Mail.Application;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Android;

internal static class CompositionRoot
{
    public static MailApplication Create(string directory)
    {
        var credentials = new AndroidCredentialStore(Path.Combine(directory, "credentials"));
        var receiver = new ImapMailReceiver(credentials);
        return new MailApplication(
            new JsonAccountStore(Path.Combine(directory, "accounts.json")),
            new JsonSettingsStore(Path.Combine(directory, "settings.json")),
            receiver, new SmtpMailSender(credentials), credentials,
            new JsonDraftStore(Path.Combine(directory, "drafts.json")),
            new ImapSentCopyWriter(credentials), new SmtpConnectionTester(credentials),
            // No saved-original store on Android yet: attachments are read from one streamed download.
            attachmentReader: new MessageAttachmentReader(receiver, receiver));
    }
}
