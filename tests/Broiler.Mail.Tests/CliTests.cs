// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Mail.Cli;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Tests;

public sealed class CliTests
{
    [Fact]
    public void SmokeTestRunnerSucceedsHeadlessly()
    {
        var exception = Record.Exception(SmokeTestRunner.Run);
        Assert.Null(exception);
    }

    [Fact]
    public async Task MemoryCredentialStoreStoresAndDeletesCredentials()
    {
        var store = new MemoryCredentialStore();
        var profile = TestDirectory.Profile();
        var key = CredentialKey.For(profile, MailProtocol.Imap);

        Assert.Null(await store.ReadAsync(key));

        await store.WriteAsync(key, "my-secret-password");
        Assert.Equal("my-secret-password", await store.ReadAsync(key));

        // Different server binding returns null
        var differentProfile = profile with { IncomingServer = profile.IncomingServer with { Host = "different.example.test" } };
        var differentKey = CredentialKey.For(differentProfile, MailProtocol.Imap);
        Assert.Null(await store.ReadAsync(differentKey));

        await store.DeleteAsync(key);
        Assert.Null(await store.ReadAsync(key));
    }

    [Fact]
    public void MacintoshEncoding10000IsSupported()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var encoding = System.Text.Encoding.GetEncoding(10000);
        Assert.NotNull(encoding);
        Assert.Equal("macintosh", encoding.WebName);

        // MIME message with macintosh charset in body and headers decodes properly
        string raw = "From: =?macintosh?Q?Sender_Desk?= <sender@example.test>\r\n" +
                     "Subject: =?macintosh?Q?Macintosh_Subject?=\r\n" +
                     "Content-Type: text/plain; charset=macintosh\r\n\r\n" +
                     "Macintosh body content";
        var message = Broiler.Mail.Infrastructure.Mime.MimeParser.Parse(System.Text.Encoding.ASCII.GetBytes(raw));
        Assert.Equal("Macintosh Subject", message.Subject);
        var fromHeader = Broiler.Mail.Infrastructure.Mime.Encodings.Rfc2047Decoder.Decode(message.RawHeaders.First(h => h.Key.Equals("From", StringComparison.OrdinalIgnoreCase)).Value);
        Assert.Contains("Sender Desk", fromHeader);
        Assert.Equal("Macintosh body content", message.TextBody);
    }
}
