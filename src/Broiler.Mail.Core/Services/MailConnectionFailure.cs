namespace Broiler.Mail.Core.Services;

/// <summary>
/// What kind of problem stopped a connection test, so callers can react without matching message text.
/// The message itself stays fixed app text; the kind never carries server response text either.
/// </summary>
public enum MailConnectionFailure
{
    /// <summary>Not classified; the default for existing callers.</summary>
    Unspecified,
    /// <summary>The saved account cannot be tested yet, for example without a saved server or while disabled. Found before any network use.</summary>
    Setup,
    /// <summary>The account or the server needs a sign-in method that is not available, such as OAuth only.</summary>
    UnsupportedSignIn,
    /// <summary>No password is saved for these exact server details.</summary>
    MissingPassword,
    /// <summary>The protected credential store could not be read.</summary>
    CredentialStore,
    /// <summary>The server could not be reached.</summary>
    Unreachable,
    /// <summary>The server did not answer before the deadline.</summary>
    Timeout,
    /// <summary>The encrypted connection could not be verified, including TLS against a plain-text port.</summary>
    TlsVerification,
    /// <summary>Required STARTTLS is not offered, so nothing secret was sent.</summary>
    TlsUnavailable,
    /// <summary>The server offers no sign-in on this connection.</summary>
    AuthenticationUnavailable,
    /// <summary>The server rejected the username or password.</summary>
    AuthenticationRejected,
    /// <summary>The server refused the connection or a sign-in step.</summary>
    ServerRefused,
    /// <summary>The connection ended or the server's reply could not be understood.</summary>
    Interrupted,
}
