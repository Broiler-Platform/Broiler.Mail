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

/// <summary>Original-source receivers driven by delegates, counting each call; a null delegate refuses the call.</summary>
internal sealed class TestSourceReceiver : IMailSourceReceiver, IStreamingMailSourceReceiver
{
    public int SourceCalls { get; private set; }
    public int CopySourceCalls { get; private set; }
    public Func<MailMessageKey, CancellationToken, Task<MailMessageSource>>? Source { get; set; }
    public Func<MailMessageKey, Stream, int, Action<int, int>?, CancellationToken, Task<int>>? CopySource { get; set; }
    public Task<MailMessageSource> GetSourceAsync(AccountProfile account, MailMessageKey message, CancellationToken token = default)
    {
        SourceCalls++;
        return (Source ?? throw new InvalidOperationException("No buffered source in this test."))(message, token);
    }
    public Task<int> CopySourceAsync(AccountProfile account, MailMessageKey message, Stream destination, int maximumBytes,
        Action<int, int>? progress = null, CancellationToken token = default)
    {
        CopySourceCalls++;
        return (CopySource ?? throw new InvalidOperationException("No streamed source in this test."))(message, destination, maximumBytes, progress, token);
    }
    /// <summary>A receiver whose both paths return <paramref name="bytes"/>.</summary>
    public static TestSourceReceiver Returning(byte[] bytes) => new()
    {
        Source = (key, _) => Task.FromResult(new MailMessageSource(key, bytes)),
        CopySource = async (_, destination, maximum, progress, token) =>
        {
            if (bytes.Length > maximum) throw new IOException("Too large.");
            await destination.WriteAsync(bytes, token);
            progress?.Invoke(bytes.Length, bytes.Length);
            return bytes.Length;
        },
    };
}