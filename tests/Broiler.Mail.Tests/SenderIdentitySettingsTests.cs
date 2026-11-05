using System.Text;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Validation;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class SenderIdentitySettingsTests
{
    [Fact]
    public async Task PerAddressSettingsRoundTripWithoutAffectingAnotherAccount()
    {
        using var directory = new TestDirectory(); var store = new JsonAccountCollectionStore(directory.Root);
        var account = Profile() with { SenderIdentities = [new() { Address = "alias@example.test", DisplayName = "Alias Name", Signature = "Alias signature", ReplySignature = "" }] };
        var other = TestDirectory.Profile(); await store.SaveAsync(account); await store.SaveAsync(other);
        var rows = await new JsonAccountCollectionStore(directory.Root).LoadAsync(); var restored = rows.Single(row => row.Id == account.Id);
        Assert.Equal("Alias Name", restored.SenderDisplayName("alias@example.test")); Assert.Equal("Alias signature", restored.SenderSignature("alias@example.test", false));
        Assert.Equal("", restored.SenderSignature("alias@example.test", true)); Assert.Null(rows.Single(row => row.Id == other.Id).SenderIdentities);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    [InlineData("name-control")]
    [InlineData("name-length")]
    [InlineData("name-unicode")]
    [InlineData("signature-control")]
    [InlineData("reply-length")]
    [InlineData("null-row")]
    public void InvalidIdentitySettingsAreRefused(string kind)
    {
        var row = new SenderIdentitySettings { Address = "alias@example.test" };
        row = kind switch
        {
            "foreign" => row with { Address = "foreign@example.test" }, "name-control" => row with { DisplayName = "Name\r\nBcc: hidden@example.test" },
            "name-length" => row with { DisplayName = new string('x', 201) }, "name-unicode" => row with { DisplayName = "\ud800" },
            "signature-control" => row with { Signature = "bad\0" }, "reply-length" => row with { ReplySignature = new string('x', 4001) }, _ => row,
        };
        var account = Profile() with { SenderIdentities = kind == "duplicate" ? [row, row] : kind == "null-row" ? [null!] : [row] };
        Assert.Throws<ConfigurationValidationException>(() => ConfigurationValidator.Validate(account));
    }

    [Theory]
    [InlineData("Support, Europe")]
    [InlineData("Help \"Desk\" \\ Team")]
    [InlineData("Müller 🐣")]
    [InlineData("=?utf-8?Q?Desk?=, Extra")]
    public void DisplayNameIsPinnedAndSerializedAsOneMailbox(string name)
    {
        var account = Profile() with { SenderIdentities = [new() { Address = "alias@example.test", DisplayName = name }] };
        var draft = MailComposition.Create(account, "alias@example.test") with { To = ["to@example.test"], Bcc = ["private@example.test"] };
        Assert.Equal(name, draft.FromDisplayName);
        string text = Encoding.UTF8.GetString(MimeSerializer.Serialize(account with { DisplayName = "Renamed account", SenderIdentities = null }, draft));
        Assert.Equal("alias@example.test", MimeParser.Parse(Encoding.UTF8.GetBytes(text)).From.Single()); Assert.DoesNotContain("Renamed account", text); Assert.DoesNotContain("Bcc:", text);
        if (name == "Support, Europe") Assert.Contains("From: \"Support, Europe\" <alias@example.test>", text);
    }

    [Fact]
    public void EmptyDisplayNameOmitsNameWhileLegacyDraftUsesAccountName()
    {
        var account = Profile() with { SenderIdentities = [new() { Address = "alias@example.test", DisplayName = "" }] };
        var draft = MailComposition.Create(account, "alias@example.test");
        Assert.Contains("From: alias@example.test\r\n", Encoding.UTF8.GetString(MimeSerializer.Serialize(account, draft)));
        Assert.Contains("From: Account Name <alias@example.test>", Encoding.UTF8.GetString(MimeSerializer.Serialize(account, draft with { FromDisplayName = null })));
        Assert.Throws<ConfigurationValidationException>(() => MimeSerializer.Serialize(account, draft with { FromDisplayName = "bad\nname" }));
    }

    [Theory]
    [InlineData(false, null, null, "Account signature", "Account signature")]
    [InlineData(false, "Alias signature", null, "Alias signature", "Alias signature")]
    [InlineData(true, "Alias signature", null, "Alias signature", "Account reply")]
    [InlineData(true, "Alias signature", "Alias reply", "Alias signature", "Alias reply")]
    [InlineData(false, "", null, "", "")]
    [InlineData(true, "", "", "", "")]
    public void SignatureInheritanceAndSuppressionAreExplicit(bool separate, string? signature, string? reply, string expectedNew, string expectedReply)
    {
        var account = Profile() with { UseSeparateReplySignature = separate, SenderIdentities = [new() { Address = "alias@example.test", Signature = signature, ReplySignature = reply }] };
        Assert.Equal(expectedNew, account.SenderSignature("alias@example.test", false)); Assert.Equal(expectedReply, account.SenderSignature("alias@example.test", true));
        Assert.Equal("Account signature", account.SenderSignature(account.EmailAddress, false));
    }

    [Theory]
    [InlineData(CompositionKind.Forward)]
    [InlineData(CompositionKind.Reply)]
    [InlineData(CompositionKind.ReplyAll)]
    public void SelectedOrReceivingAliasUsesTheCorrectSignature(CompositionKind kind)
    {
        var account = Profile() with { ReplyFromReceivingAlias = true, SenderIdentities = [new() { Address = "alias@example.test", DisplayName = "Alias Name", Signature = "Alias new", ReplySignature = "Alias reply" }] };
        using var model = new ComposerViewModel(); model.SetAccount(account);
        if (kind == CompositionKind.Forward) model.SelectSender("alias@example.test");
        var body = new MailMessageBody(new(account.Id, "INBOX", 7, 1), "Original") { Composition = new() { From = ["other@example.test"], To = ["ALIAS@example.test"], MessageId = "parent@example.test" } };
        Assert.True(model.StartFromMessage(body, kind)); Assert.Equal("alias@example.test", model.FromAddress); Assert.Equal("Alias Name", model.FromDisplayName);
        Assert.Contains(kind == CompositionKind.Forward ? "-- \nAlias new" : "-- \nAlias reply", model.PlainText);
        Assert.DoesNotContain("-- \nAccount signature", model.PlainText);
    }

    [Fact]
    public async Task RestartAndLibraryResumeNeverReapplyChangedNamesOrSignatures()
    {
        using var directory = new TestDirectory(); var account = Profile() with { DefaultSenderAddress = "alias@example.test", SenderIdentities = [new() { Address = "alias@example.test", DisplayName = "Original Name", Signature = "Original signature" }] };
        var store = new JsonDraftStore(directory.File("drafts.json")); Guid id; string body;
        using (var model = new ComposerViewModel(store)) { model.SetAccount(account); model.StartNew(); id = model.DraftId!.Value; body = model.PlainText; Assert.True(await model.ParkDraftAsync()); }
        using var restored = new ComposerViewModel(store, await store.LoadAsync()); restored.SetAccount(account with { SenderIdentities = [new() { Address = "alias@example.test", DisplayName = "Changed Name", Signature = "Changed signature" }] });
        Assert.True(await restored.ResumeDraftAsync(id)); Assert.Equal("Original Name", restored.FromDisplayName); Assert.Equal(body, restored.PlainText); Assert.Equal(id, restored.DraftId);
        restored.Edit("to@example.test", "", "", restored.Subject, restored.PlainText); Assert.Equal("Original Name", restored.BuildDraft().FromDisplayName); Assert.True(await restored.SaveAsync());
    }

    [Fact]
    public void LongUnicodeNameUsesBoundedEncodedWordsWithoutSplittingScalars()
    {
        string name = string.Concat(Enumerable.Repeat("🐣", 100));
        var account = Profile() with { SenderIdentities = [new() { Address = "alias@example.test", DisplayName = name }] };
        string text = Encoding.UTF8.GetString(MimeSerializer.Serialize(account, MailComposition.Create(account, "alias@example.test")));
        var parsed = MimeParser.Parse(Encoding.UTF8.GetBytes(text)); Assert.Equal("alias@example.test", parsed.From.Single());
        string header = parsed.RawHeaders.Single(pair => pair.Key.Equals("From", StringComparison.OrdinalIgnoreCase)).Value;
        foreach (System.Text.RegularExpressions.Match word in System.Text.RegularExpressions.Regex.Matches(header, @"=\?utf-8\?Q\?.*?\?=")) Assert.InRange(word.Length, 1, 75);
        Assert.Contains(name, Broiler.Mail.Infrastructure.Mime.Encodings.Rfc2047Decoder.Decode(header));
    }

    internal static AccountProfile Profile() => TestDirectory.Profile() with { DisplayName = "Account Name", SenderAliases = "alias@example.test", Signature = "Account signature", ReplySignature = "Account reply" };
}
