using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>OS-protected secrets bound to account, protocol and server identity. Never log values.</summary>
public interface ICredentialStore
{
    Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default);
    Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default);
    // Removes this account/protocol slot, including a credential bound to older connection details.
    Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default);
}
