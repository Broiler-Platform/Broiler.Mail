using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Tests;

internal sealed class TestCredentialStore : ICredentialStore
{
    private readonly Dictionary<(AccountId, MailProtocol), (string Binding, string Secret)> _values = [];
    public bool FailWrites { get; set; }
    public Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_values.TryGetValue((key.AccountId, key.Protocol), out var value) && value.Binding == key.Binding ? value.Secret : null);
    }
    public Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailWrites) throw new IOException("Credential storage unavailable.");
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

internal sealed class TestMailReceiver : IMailReceiver
{
    public int Calls { get; private set; }
    public Func<CancellationToken, Task> Test { get; set; } = _ => Task.CompletedTask;
    public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Test(cancellationToken);
    }
    public Func<MailInboxCursor?, CancellationToken, Task<MailInboxPage>> Inbox { get; set; } = (_, _) => Task.FromResult(new MailInboxPage([], null));
    public Func<MailMessageKey, CancellationToken, Task<MailMessageBody>> Body { get; set; } = (key, _) => Task.FromResult(new MailMessageBody(key, "Test body"));
    public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default) => Inbox(older, cancellationToken);
    public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey message, CancellationToken cancellationToken = default) => Body(message, cancellationToken);
}

internal sealed class TestOutgoingTester : IOutgoingConnectionTester
{
    public int Calls { get; private set; }
    public AccountProfile? Account { get; private set; }
    public Func<CancellationToken, Task> Test { get; set; } = _ => Task.CompletedTask;
    public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default)
    {
        Calls++;
        Account = account;
        return Test(cancellationToken);
    }
}
