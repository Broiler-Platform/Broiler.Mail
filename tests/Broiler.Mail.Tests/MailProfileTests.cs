using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class MailProfileTests
{
    [Fact]
    public async Task AdoptionBacksUpRawFilesAndKeepsAccountAndUnfinishedDraftIdentity()
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        await new JsonAccountStore(directory.File("accounts.json")).SaveAsync(account);
        await new JsonSettingsStore(directory.File("settings.json")).SaveAsync(new() { Theme = AppTheme.Dark });
        var draft = new DraftSnapshot { Draft = MailComposition.Create(account) with { PlainText = "unfinished" },
            ToText = "raw incomplete recipient", CcText = "", BccText = "", State = DraftSubmissionState.Editing };
        await new JsonDraftStore(directory.File("drafts.json")).SaveAsync(0, draft);
        byte[] original = await File.ReadAllBytesAsync(directory.File("accounts.json"));
        Guid profileId;
        using (var profile = MailProfile.Open(directory.Root))
        {
            profileId = profile.Manifest.ProfileId;
            Assert.Equal(account, Assert.Single(await new JsonAccountStore(profile.AccountsPath).LoadAsync()));
            Assert.Equal(draft.Draft.Id, (await new JsonDraftStore(profile.DraftsPath).LoadAsync()).Draft!.Draft.Id);
            Assert.Equal("raw incomplete recipient", (await new JsonDraftStore(profile.DraftsPath).LoadAsync()).Draft!.ToText);
            Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(directory.Root, "backups", profile.Manifest.LegacyBackup!, "accounts.json")));
            Assert.Equal(original, await File.ReadAllBytesAsync(profile.AccountsPath));
            Assert.Throws<IOException>(() => MailProfile.Open(directory.Root));
        }
        using var reopened = MailProfile.Open(directory.Root);
        Assert.Equal(profileId, reopened.Manifest.ProfileId);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"SchemaVersion\":999,\"ProfileId\":\"8cd5ad9b-d340-40f7-9245-76cd50928768\",\"CreatedAt\":\"2026-11-02T00:00:00Z\",\"CacheMaximumBytes\":268435456}")]
    public void CorruptOrNewerManifestIsNotReset(string contents)
    {
        using var directory = new TestDirectory(); directory.Create();
        File.WriteAllText(directory.File("profile.json"), contents);
        Assert.Throws<InvalidDataException>(() => MailProfile.Open(directory.Root));
        Assert.Equal(contents, File.ReadAllText(directory.File("profile.json")));
    }

    [Fact]
    public async Task InvalidLegacyDataRefusesAdoptionAndInterruptedAdoptionCanRetry()
    {
        using var directory = new TestDirectory(); directory.Create();
        File.WriteAllText(directory.File("drafts.json"), "broken");
        Assert.Throws<InvalidDataException>(() => MailProfile.Open(directory.Root));
        Assert.False(File.Exists(directory.File("profile.json")));
        Assert.Equal("broken", File.ReadAllText(directory.File("drafts.json")));
        File.Delete(directory.File("drafts.json"));
        await new JsonAccountStore(directory.File("accounts.json")).SaveAsync(TestDirectory.Profile());
        Directory.CreateDirectory(directory.File("backups/legacy-interrupted"));
        File.WriteAllText(directory.File("profile-interrupted.tmp"), "partial manifest");
        using var profile = MailProfile.Open(directory.Root);
        Assert.Single(await new JsonAccountStore(profile.AccountsPath).LoadAsync());
        Assert.True(File.Exists(directory.File("profile.json")));
    }
}
