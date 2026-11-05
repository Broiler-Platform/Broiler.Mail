namespace Broiler.Mail.Core.Accounts;

/// <summary>Configuration equality across persistence boundaries, including recreated sender-override collections.</summary>
public static class AccountProfileConfiguration
{
    public static bool Matches(AccountProfile? expected, AccountProfile? actual) => expected == actual ||
        expected is not null && actual is not null &&
        (expected with { SenderIdentities = null }) == (actual with { SenderIdentities = null }) &&
        (expected.SenderIdentities ?? []).SequenceEqual(actual.SenderIdentities ?? []);
}
