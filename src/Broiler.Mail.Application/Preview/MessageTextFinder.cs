namespace Broiler.Mail.Application.Preview;

public readonly record struct MessageTextMatch(int Start, int Length);
public sealed record MessageTextMatches(IReadOnlyList<MessageTextMatch> Matches, bool HasMore);

/// <summary>Bounded literal ordinal-ignore-case find in one decoded message preview; no query language or network.</summary>
public static class MessageTextFinder
{
    public const int MaximumMatches = 1000;
    public const int MaximumTextLength = 1_000_000;
    public static MessageTextMatches Find(string text, string query)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(query);
        if (text.Length > MaximumTextLength) throw new ArgumentException("This text exceeds the 1,000,000-character find limit.");
        if (query.Length is 0 or > 256 || query.Any(char.IsControl)) throw new ArgumentException("Enter 1–256 literal characters without control characters.");
        for (int i = 0; i < query.Length; i++)
            if (char.IsSurrogate(query[i]) && (!char.IsHighSurrogate(query[i]) || ++i >= query.Length || !char.IsLowSurrogate(query[i]))) throw new ArgumentException("Enter valid Unicode find text.");
        var matches = new List<MessageTextMatch>(); int offset = 0;
        while (offset <= text.Length - query.Length)
        {
            int found = text.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase); if (found < 0) break;
            if (matches.Count == MaximumMatches) return new(matches.AsReadOnly(), true);
            matches.Add(new(found, query.Length)); offset = found + query.Length;
        }
        return new(matches.AsReadOnly(), false);
    }
}
