// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Cli;

internal sealed class MemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<(AccountId, MailProtocol), (string Binding, string Secret)> _values = [];

    public Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_values.TryGetValue((key.AccountId, key.Protocol), out var value) && value.Binding == key.Binding ? value.Secret : null);
    }

    public Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _values[(key.AccountId, key.Protocol)] = (key.Binding, secret);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _values.Remove((key.AccountId, key.Protocol));
        return Task.CompletedTask;
    }
}
