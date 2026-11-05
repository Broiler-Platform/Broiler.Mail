namespace Broiler.Mail.Application.ViewModels;

/// <summary>English word hints in authored text only. Never changes submission state or sends mail.</summary>
public static class AttachmentReminder
{
    public static bool IsMentioned(string subject, string body)
    {
        if (HasWord(subject)) return true;
        foreach (var line in body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (line == "-- " || line.Contains("---------- Forwarded message ----------", StringComparison.Ordinal)) break;
            if (!line.TrimStart().StartsWith('>') && HasWord(line)) return true;
        }
        return false;
    }
    private static bool HasWord(string text) => text.Split(text.Where(c => !char.IsLetter(c)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries)
        .Any(word => word.Equals("attached", StringComparison.OrdinalIgnoreCase) || word.Equals("attachment", StringComparison.OrdinalIgnoreCase) || word.Equals("attachments", StringComparison.OrdinalIgnoreCase));
}
