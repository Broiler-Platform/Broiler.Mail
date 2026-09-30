// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   5
// Annotated:        5/5
// Exempt:           0
// Human-reviewed:   0/5
// IP risk:          Low
// Security risk:    High
// Criteria:         5/4
// Resource impact:  2/10 max
// Unverified:       5
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Net.Mail;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Settings;

namespace Broiler.Mail.Core.Validation;

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=DCF2A3
// Broiler-Falsified-If: an undefined TransportSecurity or AuthenticationMethod value in a saved profile passes validation
// Broiler-Human:        PENDING
public static class ConfigurationValidator
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=8FB9B1
    // Broiler-Falsified-If: a profile whose EmailAddress carries a display name, such as Eve <eve@example.com>, passes Validate
    // Broiler-Human:        PENDING
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
        if (!Enum.IsDefined(profile.SentCopyMode)) throw new ArgumentException("Choose a supported Sent-copy mode.");
        if (profile.SentCopyMode == SentCopyMode.AppendToFolder)
            RequireText(profile.SentFolder, "Sent folder path", 512);
        else if (profile.SentFolder is not null)
            throw new ArgumentException("A Sent folder path is only used when appending a copy.");
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=0; Fingerprint=959F38
    // Broiler-Falsified-If: a settings file with a window width above 7680 or a height above 4320 passes Validate
    // Broiler-Human:        PENDING
    public static void Validate(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.Theme))
            throw new ArgumentException("Choose a supported theme.");
        if (settings.WindowWidth is < 640 or > 7680 || settings.WindowHeight is < 480 or > 4320)
            throw new ArgumentException("Window size must be 640–7680 pixels wide and 480–4320 pixels high.");
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=6884FA
    // Broiler-Falsified-If: a host written with a port or scheme, such as mail.example.com:993 or imap://mail.example.com, passes ValidateServer
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=194725
    // Broiler-Falsified-If: a value containing CR or LF within the length limit is accepted without an ArgumentException
    // Broiler-Human:        PENDING
    private static void RequireText(string? value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
            throw new ArgumentException($"{name} is required, must contain no control characters, and may have at most {maximumLength} characters.");
    }
}
