using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Messages;

public enum MailFolderRole { Sent, Drafts, Archive, Trash, Junk }
public sealed record MailFolderRoleMapping(MailFolderRole Role, string? MailboxId, bool IsOverride,
    bool IsConfirmed, string? Problem);

/// <summary>Resolve roles without guessing names or silently replacing an invalid override.</summary>
public static class MailFolderRoles
{
    public static IReadOnlyList<MailFolderRoleMapping> Resolve(AccountFolderOverrides overrides, IReadOnlyList<MailFolder>? serverFolders)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var result = new List<MailFolderRoleMapping>();
        foreach (var (role, configured) in new[] { (MailFolderRole.Sent, overrides.Sent), (MailFolderRole.Drafts, overrides.Drafts),
            (MailFolderRole.Archive, overrides.Archive), (MailFolderRole.Trash, overrides.Trash), (MailFolderRole.Junk, overrides.Junk) })
        {
            if (configured is not null)
            {
                string name = string.Equals(configured, "INBOX", StringComparison.OrdinalIgnoreCase) ? "INBOX" : configured;
                var match = serverFolders?.FirstOrDefault(folder => folder.MailboxId == name);
                result.Add(new(role, name, true, match?.IsSelectable == true,
                    serverFolders is null ? "not verified" : match is null ? "not found on server" : !match.IsSelectable ? "not selectable" : null));
                continue;
            }
            var matches = serverFolders?.Where(folder => folder.IsSelectable &&
                folder.Attributes.Contains("\\" + role, StringComparer.OrdinalIgnoreCase)).Select(folder => folder.MailboxId)
                .Distinct(StringComparer.Ordinal).ToArray() ?? [];
            result.Add(matches.Length == 1 ? new(role, matches[0], false, true, null) :
                new(role, null, false, false, matches.Length > 1 ? "ambiguous server role; set an override" :
                    serverFolders is null ? "refresh folders to discover" : "not advertised"));
        }
        return result.AsReadOnly();
    }
}
