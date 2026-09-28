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
        RequireText(profile.DisplayName, "Display name", 200);
        RequireText(profile.EmailAddress, "Email address", 320);
        if (!MailAddress.TryCreate(profile.EmailAddress, out var address) || address.Address != profile.EmailAddress)
            throw new ArgumentException("Enter an email address without a display name.");
        ValidateServer(profile.IncomingServer);
        if (profile.OutgoingServer is not null)
            ValidateServer(profile.OutgoingServer);
    }

    public static void Validate(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.Theme))
            throw new ArgumentException("Choose a supported theme.");
        if (settings.WindowWidth is < 640 or > 7680 || settings.WindowHeight is < 480 or > 4320)
            throw new ArgumentException("Window size must be 640–7680 pixels wide and 480–4320 pixels high.");
    }

    private static void ValidateServer(MailServerSettings server)
    {
        ArgumentNullException.ThrowIfNull(server);
        RequireText(server.Host, "Server host", 253);
        if (Uri.CheckHostName(server.Host) == UriHostNameType.Unknown || server.Host.Contains(':') && !System.Net.IPAddress.TryParse(server.Host, out _))
            throw new ArgumentException("Enter a server hostname or IP address, without a URL or port.");
        if (server.Port is < 1 or > 65535)
            throw new ArgumentException("Server port must be between 1 and 65535.");
        RequireText(server.UserName, "Username", 320);
        if (!Enum.IsDefined(server.Security) || !Enum.IsDefined(server.Authentication))
            throw new ArgumentException("Unsupported connection security or authentication method.");
    }

    private static void RequireText(string? value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
            throw new ArgumentException($"{name} is required, must contain no control characters, and may have at most {maximumLength} characters.");
    }
}
