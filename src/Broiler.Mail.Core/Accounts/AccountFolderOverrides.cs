namespace Broiler.Mail.Core.Accounts;

/// <summary>Exact mailbox identities for navigation roles. Null means server discovery.</summary>
public sealed record AccountFolderOverrides
{
    public string? Sent { get; init; }
    public string? Drafts { get; init; }
    public string? Archive { get; init; }
    public string? Trash { get; init; }
    public string? Junk { get; init; }
}
