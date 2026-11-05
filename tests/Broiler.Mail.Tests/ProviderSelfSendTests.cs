using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.ProviderProbe;

namespace Broiler.Mail.Tests;

public sealed class ProviderSelfSendTests
{
    [Theory]
    [InlineData(SubmissionStatus.Accepted)]
    [InlineData(SubmissionStatus.Unknown)]
    [InlineData(SubmissionStatus.Rejected)]
    public async Task EveryOutcomePreventsASecondAttemptAndHasOnlyTheAccountItselfAsRecipient(SubmissionStatus outcome)
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var attempt = Guid.NewGuid();
        var sender = new Sender(outcome); string fingerprint = SelfSendAcceptance.Fingerprint(account);
        Assert.Equal(outcome, await SelfSendAcceptance.SendAsync(account, fingerprint, attempt, directory.Root, sender));
        Assert.Equal(1, sender.Calls); var draft = Assert.IsType<MailDraft>(sender.Draft);
        Assert.Equal(account.EmailAddress, draft.FromAddress); Assert.Equal(account.EmailAddress, Assert.Single(draft.To)); Assert.Empty(draft.Cc); Assert.Empty(draft.Bcc);
        Assert.Equal(attempt, draft.Id); Assert.Contains(attempt.ToString("N"), draft.Subject);
        await Assert.ThrowsAsync<IOException>(() => SelfSendAcceptance.SendAsync(account, fingerprint, attempt, directory.Root, sender)); Assert.Equal(1, sender.Calls);
    }
    [Fact]
    public async Task AccountChangeAndCanceledWorkNeverReachTheSender()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var sender = new Sender(SubmissionStatus.Accepted);
        string fingerprint = SelfSendAcceptance.Fingerprint(account);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SelfSendAcceptance.SendAsync(account with { EmailAddress = "other@example.test" }, fingerprint, Guid.NewGuid(), directory.Root, sender));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SelfSendAcceptance.SendAsync(account, fingerprint, Guid.NewGuid(), directory.Root, sender, new(true)));
        Assert.Equal(0, sender.Calls); Assert.False(Directory.Exists(directory.Root));
    }
    [Fact]
    public async Task InterruptedAttemptLeavesDurableStartAndCannotBeReplayed()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var attempt = Guid.NewGuid(); var sender = new Sender(SubmissionStatus.Accepted) { Fail = true };
        string fingerprint = SelfSendAcceptance.Fingerprint(account);
        await Assert.ThrowsAsync<IOException>(() => SelfSendAcceptance.SendAsync(account, fingerprint, attempt, directory.Root, sender));
        Assert.True(File.Exists(directory.File(attempt.ToString("N") + ".started")));
        await Assert.ThrowsAsync<IOException>(() => SelfSendAcceptance.SendAsync(account, fingerprint, attempt, directory.Root, sender)); Assert.Equal(1, sender.Calls);
    }
    private sealed class Sender(SubmissionStatus status) : IMailSender
    {
        public int Calls; public MailDraft? Draft; public bool Fail;
        public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken token = default)
        { Calls++; Draft = draft; if (Fail) throw new IOException("Simulated interruption."); return Task.FromResult(new SendResult(status)); }
    }
}
