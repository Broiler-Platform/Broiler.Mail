using System.Net.Mail;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Settings;

namespace Broiler.Mail.Core.Validation;

public static class ConfigurationValidator
{
    public static void Validate(AccountProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Id.Value == Guid.Empty)
            throw new ArgumentException("The account must have an identity.");
        RequireText(profile.DisplayName, "Display name", 200, "DisplayName");
        RequireText(profile.EmailAddress, "Email address", 320, "EmailAddress");
        if (!MailAddress.TryCreate(profile.EmailAddress, out var address) || address.Address != profile.EmailAddress)
            throw new ConfigurationValidationException("EmailAddress", "Enter an email address without a display name.");
        ValidateServer(profile.IncomingServer, "IncomingServer");
        if (profile.OutgoingServer is not null)
            ValidateServer(profile.OutgoingServer, "OutgoingServer");
        if (!Enum.IsDefined(profile.SentCopyMode)) throw new ArgumentException("Choose a supported Sent-copy mode.");
        if (profile.SentCopyMode == SentCopyMode.AppendToFolder)
            RequireText(profile.SentFolder, "Sent folder path", 512, "SentFolder");
        else if (profile.SentFolder is not null)
            throw new ArgumentException("A Sent folder path is only used when appending a copy.");
    }

    public static void Validate(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.Theme))
            throw new ArgumentException("Choose a supported theme.");
        if (settings.WindowWidth is < 640 or > 7680)
            throw new ConfigurationValidationException("WindowWidth", "Window width must be between 640 and 7680 pixels.");
        if (settings.WindowHeight is < 480 or > 4320)
            throw new ConfigurationValidationException("WindowHeight", "Window height must be between 480 and 4320 pixels.");
        if (!double.IsFinite(settings.InboxSplitterFraction) || settings.InboxSplitterFraction is < 0.05 or > 0.95)
            throw new ArgumentException("Inbox splitter position must be between 5% and 95%.");
    }

    private static void ValidateServer(MailServerSettings server, string field)
    {
        ArgumentNullException.ThrowIfNull(server);
        RequireText(server.Host, "Server host", 253, field + ".Host");
        if (Uri.CheckHostName(server.Host) == UriHostNameType.Unknown || server.Host.Contains(':') && !System.Net.IPAddress.TryParse(server.Host, out _))
            throw new ConfigurationValidationException(field + ".Host", "Enter a server hostname or IP address, without a URL or port.");
        if (server.Port is < 1 or > 65535)
            throw new ConfigurationValidationException(field + ".Port", "Server port must be between 1 and 65535.");
        RequireText(server.UserName, "Username", 320, field + ".UserName");
        if (!Enum.IsDefined(server.Security) || !Enum.IsDefined(server.Authentication))
            throw new ArgumentException("Unsupported connection security or authentication method.");
    }

    private static void RequireText(string? value, string name, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
            throw new ConfigurationValidationException(field, $"{name} is required, must contain no control characters, and may have at most {maximumLength} characters.");
    }
}
