namespace Broiler.Mail.Core.Accounts;

/// <summary>Stable local identity; independent of an address or display name.</summary>
public readonly record struct AccountId(Guid Value)
{
    public static AccountId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
