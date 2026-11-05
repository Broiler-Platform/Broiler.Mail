using System.Text.Json.Nodes;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class AccountCollectionTests
{
    [Fact]
    public async Task ImportsWithoutChangingLegacyBytesAndPersistsOrderSelectionAndDisabledIdentity()
    {
        using var directory = new TestDirectory(); var first = TestDirectory.Profile(); var second = TestDirectory.Profile();
        string legacy = directory.File("accounts.json"); await new JsonAccountStore(legacy).SaveAsync(first);
        byte[] original = File.ReadAllBytes(legacy);
        var store = new JsonAccountCollectionStore(directory.Root);
        Assert.Equal(first.Id, (await store.LoadStateAsync()).SelectedAccountId);
        await store.SaveAsync(second); await store.SelectAsync(second.Id); await store.ReorderAsync([second.Id, first.Id]);
        second = second with { DisplayName = "Edited work identity", IsEnabled = false }; await store.SaveAsync(second);
        var reopened = await new JsonAccountCollectionStore(directory.Root).LoadStateAsync();
        Assert.Equal([second, first], reopened.Accounts); Assert.Equal(second.Id, reopened.SelectedAccountId);
        Assert.Equal(original, File.ReadAllBytes(legacy));
    }

    [Fact]
    public async Task ConcurrentStoreInstancesDoNotLoseNewAccounts()
    {
        using var directory = new TestDirectory(); var profiles = Enumerable.Range(0, 12).Select(_ => TestDirectory.Profile()).ToArray();
        await Task.WhenAll(profiles.Select(profile => new JsonAccountCollectionStore(directory.Root).SaveAsync(profile)));
        var state = await new JsonAccountCollectionStore(directory.Root).LoadStateAsync();
        Assert.Equal(profiles.Select(p => p.Id).OrderBy(id => id.Value), state.Accounts.Select(p => p.Id).OrderBy(id => id.Value));
        Assert.Contains(state.Accounts, p => p.Id == state.SelectedAccountId);
    }

    [Fact]
    public async Task RemovingSelectedAccountKeepsOthersAndEmptyCollectionDoesNotReimportLegacy()
    {
        using var directory = new TestDirectory(); var first = TestDirectory.Profile(); var second = TestDirectory.Profile();
        await new JsonAccountStore(directory.File("accounts.json")).SaveAsync(first);
        var store = new JsonAccountCollectionStore(directory.Root); await store.SaveAsync(second); await store.SelectAsync(second.Id);
        await store.RemoveAsync(second.Id); Assert.Equal(first.Id, (await store.LoadStateAsync()).SelectedAccountId);
        await store.RemoveAsync(first.Id); await store.RemoveAsync(AccountId.New());
        var reopened = await new JsonAccountCollectionStore(directory.Root).LoadStateAsync();
        Assert.Empty(reopened.Accounts); Assert.Null(reopened.SelectedAccountId);
        Assert.Single(await new JsonAccountStore(directory.File("accounts.json")).LoadAsync());
    }

    [Theory]
    [InlineData("future")]
    [InlineData("duplicate")]
    [InlineData("selected")]
    [InlineData("unknown")]
    public async Task InvalidCollectionIsNeverResetOnReadOrWrite(string kind)
    {
        using var directory = new TestDirectory(); var store = new JsonAccountCollectionStore(directory.Root);
        await store.SaveAsync(TestDirectory.Profile()); string path = directory.File("account-collection.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        if (kind == "future") json["schemaVersion"] = 99;
        else if (kind == "duplicate") json["data"]!["accounts"]!.AsArray().Add(json["data"]!["accounts"]![0]!.DeepClone());
        else if (kind == "selected") json["data"]!["selectedAccountId"]!["value"] = Guid.NewGuid().ToString();
        else json["data"]!["unknownField"] = true;
        await File.WriteAllTextAsync(path, json.ToJsonString()); byte[] original = File.ReadAllBytes(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadStateAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(TestDirectory.Profile()));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task InvalidOrderUnknownSelectionAndCancellationPreserveCurrentFile()
    {
        using var directory = new TestDirectory(); var store = new JsonAccountCollectionStore(directory.Root); var profile = TestDirectory.Profile();
        await store.SaveAsync(profile); string path = directory.File("account-collection.json"); byte[] original = File.ReadAllBytes(path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReorderAsync([profile.Id, profile.Id]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SelectAsync(AccountId.New()));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(TestDirectory.Profile(), canceled.Token));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task CorruptLegacyImportLeavesSourceUntouchedAndDoesNotPublishACollection()
    {
        using var directory = new TestDirectory(); directory.Create(); string path = directory.File("accounts.json"); await File.WriteAllTextAsync(path, "{corrupt");
        await Assert.ThrowsAsync<InvalidDataException>(() => new JsonAccountCollectionStore(directory.Root).LoadStateAsync());
        Assert.Equal("{corrupt", File.ReadAllText(path)); Assert.False(File.Exists(directory.File("account-collection.json")));
    }

    [Fact]
    public async Task AccountLimitRefusesAdditionalIdentityWithoutEvictingExistingAccounts()
    {
        using var directory = new TestDirectory(); var store = new JsonAccountCollectionStore(directory.Root);
        for (int i = 0; i < JsonAccountCollectionStore.MaximumAccounts; i++) await store.SaveAsync(TestDirectory.Profile());
        byte[] original = File.ReadAllBytes(directory.File("account-collection.json"));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(TestDirectory.Profile()));
        Assert.Equal(original, File.ReadAllBytes(directory.File("account-collection.json")));
    }
}
