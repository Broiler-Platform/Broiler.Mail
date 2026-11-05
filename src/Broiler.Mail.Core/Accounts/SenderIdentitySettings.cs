namespace Broiler.Mail.Core.Accounts;

/// <summary>Optional presentation defaults for an already configured sender. Null inherits; empty suppresses.</summary>
public sealed record SenderIdentitySettings
{
    public required string Address { get; init; }
    public string? DisplayName { get; init; }
    public string? Signature { get; init; }
    public string? ReplySignature { get; init; }
}
