using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>Offline search returning headers, optionally matching saved previews or complete retained decoded reader text. Only current cached mailbox epochs are searched; never contacts a server.</summary>
public interface IMailLocalSearch
{
    Task<MailHeaderSearchResult> SearchHeadersAsync(MailHeaderSearchQuery query,
        CancellationToken cancellationToken = default);
}

public enum MailHeaderSearchField { SenderOrSubject, Sender, Subject, BodyPreview, HeadersOrBodyPreview, IndexedHeaders, IndexedSender, IndexedSubject, IndexedBodyPreview, IndexedHeadersOrBodyPreview, IndexedRetainedBody, IndexedHeadersOrRetainedBody }

/// <summary>Literal ordinal-ignore-case substrings, or quoted word phrases in optional Indexed field modes; no user query language or wildcards.
/// Blank text is allowed only with at least one sender, flag, availability or date filter.</summary>
public sealed record MailHeaderSearchQuery(AccountId AccountId, string Text)
{
    public MailHeaderSearchField Field { get; init; }
    public string? MailboxId { get; init; }
    /// <summary>Optional ordinal-ignore-case substring of the saved sender header, independent of the phrase/field.
    /// This is header matching, not verified sender identity.</summary>
    public string? SenderContains { get; init; }
    public bool? IsRead { get; init; }
    public bool? IsStarred { get; init; }
    public bool? IsBodyCached { get; init; }
    /// <summary>Inclusive UTC instant; unknown dates are excluded when either date bound is set.</summary>
    public DateTimeOffset? ReceivedFrom { get; init; }
    /// <summary>Exclusive UTC instant. When both bounds exist, this must be later than ReceivedFrom.</summary>
    public DateTimeOffset? ReceivedBefore { get; init; }
    /// <summary>Exclusive folder/UID continuation within an explicit mailbox, or all-folder scope with AccountMailboxSnapshot.
    /// Preserve the submitted text/filters/date bounds for subsequent pages.</summary>
    public MailMessageKey? BeforeMessage { get; init; }
    /// <summary>Account-bound fingerprint of current saved mailbox epochs, required for all-folder continuation.
    /// Detects head additions/removals/epoch changes, not a frozen message-content snapshot.</summary>
    public string? AccountMailboxSnapshot { get; init; }
    public int MaximumCount { get; init; } = 50;
}

/// <summary>Descending mailbox-name (SQLite BINARY), then descending UID order. HasMore means the result limit was exceeded.
/// Coverage is saved headers and optionally downloaded previews or complete retained decoded reader text, excluding old epochs and server-only mail.</summary>
public sealed record MailHeaderSearchResult(IReadOnlyList<MailMessageSummary> Messages, bool HasMore)
{
    public string? AccountMailboxSnapshot { get; init; }
}

/// <summary>Matches SQLite BINARY mailbox ordering, including supplementary Unicode, then UID.</summary>
public static class MailHeaderSearchOrder
{
    public static int Compare(MailMessageKey left, MailMessageKey right)
    {
        int folder = System.Text.Encoding.UTF8.GetBytes(left.MailboxId).AsSpan().SequenceCompareTo(System.Text.Encoding.UTF8.GetBytes(right.MailboxId));
        return folder != 0 ? folder : left.Uid.CompareTo(right.Uid);
    }
}

/// <summary>Submitted all-folder paging no longer matches the saved mailbox epoch set.</summary>
public sealed class MailSearchScopeChangedException(string message) : IOException(message);
