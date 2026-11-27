// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   13
// Annotated:        0/13
// Exempt:           35
// Human-reviewed:   0/13
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       13
//
// GENERATED - DO NOT EDIT MANUALLY

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

/// <summary>The three human search scopes. The cache picks the engine for each: sender and subject always match as literal
/// ordinal-ignore-case substrings; message text matches as an indexed word phrase over the saved preview and the complete
/// retained decoded text when the search index is attached, otherwise as a literal substring of the same downloaded text.</summary>
public enum MailSearchScope { Everywhere, SenderOrSubject, MessageBody }

/// <summary>How message text was (or must be) matched for a <see cref="MailSearchScope"/> query.</summary>
public enum MailSearchEngine { Literal, Indexed }

/// <summary>Literal ordinal-ignore-case substrings, or quoted word phrases in optional Indexed field modes; no user query language or wildcards.
/// Blank text is allowed only with at least one sender, flag, availability or date filter.</summary>
public sealed record MailHeaderSearchQuery(AccountId AccountId, string Text)
{
    /// <summary>Low-level engine field. Ignored, and must stay at its default, when <see cref="Scope"/> is set.</summary>
    public MailHeaderSearchField Field { get; init; }
    /// <summary>Human scope; when set, the cache chooses the engine automatically (see <see cref="Engine"/>).</summary>
    public MailSearchScope? Scope { get; init; }
    /// <summary>Only with <see cref="Scope"/>: null chooses automatically, Indexed requires the search index (throwing
    /// MailSearchIndexUnavailableException without it) and Literal forces direct scanning. Paging pins the engine page 1 used.</summary>
    public MailSearchEngine? Engine { get; init; }
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
    /// <summary>Exclusive continuation after the last message of the previous page. Within an explicit mailbox it is a UID
    /// continuation; in all-folder scope it is a (ReceivedAt, MailboxId, Uid) keyset that needs AccountMailboxSnapshot and
    /// <see cref="BeforeReceivedAt"/>. Preserve the submitted text/filters/date bounds for subsequent pages.</summary>
    public MailMessageKey? BeforeMessage { get; init; }
    /// <summary>All-folder continuation only: the ReceivedAt of <see cref="BeforeMessage"/> exactly as the previous page
    /// returned it (null for an unknown date, which sorts last). See <see cref="MailHeaderSearchOrder.Next"/>.</summary>
    public DateTimeOffset? BeforeReceivedAt { get; init; }
    /// <summary>Account-bound fingerprint of current saved mailbox epochs, required for all-folder continuation.
    /// Detects head additions/removals/epoch changes, not a frozen message-content snapshot.</summary>
    public string? AccountMailboxSnapshot { get; init; }
    public int MaximumCount { get; init; } = 50;
}

/// <summary>All folders: newest ReceivedAt first (unknown dates last), then mailbox name ascending (SQLite BINARY), then
/// descending UID. An explicit mailbox: descending UID. HasMore means the result limit was exceeded.
/// Coverage is saved headers and optionally downloaded previews or complete retained decoded reader text, excluding old epochs and server-only mail.</summary>
public sealed record MailHeaderSearchResult(IReadOnlyList<MailMessageSummary> Messages, bool HasMore)
{
    public string? AccountMailboxSnapshot { get; init; }
    /// <summary>The engine that matched message text (Indexed for the Indexed* fields).</summary>
    public MailSearchEngine Engine { get; init; }
    /// <summary>For a scope with message text and non-blank text: whether some in-scope saved messages have headers only,
    /// so their text could not be searched.</summary>
    public bool HasHeaderOnlyMessages { get; init; }
    /// <summary>For a scope with message text and non-blank text matched by a direct scan while the search index is
    /// enabled: why the index was not used (for example the reason it failed to attach). Null when the index is disabled
    /// or was used.</summary>
    public string? SearchIndexIssue { get; init; }
}

/// <summary>The orders search results are listed in. Mailbox names compare as SQLite BINARY does (UTF-8 bytes, including
/// supplementary Unicode).</summary>
public static class MailHeaderSearchOrder
{
    /// <summary>Mailbox name, then UID; within one explicit mailbox, results are strictly descending in this order.</summary>
    public static int Compare(MailMessageKey left, MailMessageKey right)
    {
        int folder = CompareMailbox(left.MailboxId, right.MailboxId);
        return folder != 0 ? folder : left.Uid.CompareTo(right.Uid);
    }

    /// <summary>The all-folder order: positive when <paramref name="left"/> is listed before <paramref name="right"/>, so
    /// results are strictly descending in it like <see cref="Compare"/>. Newer received dates come first and unknown dates
    /// last; equal dates list mailboxes ascending, then UIDs descending.</summary>
    public static int CompareChronological(DateTimeOffset? leftReceived, MailMessageKey left, DateTimeOffset? rightReceived, MailMessageKey right)
    {
        int date = Nullable.Compare(leftReceived?.UtcTicks, rightReceived?.UtcTicks);
        if (date != 0) return date;
        int folder = CompareMailbox(right.MailboxId, left.MailboxId);
        return folder != 0 ? folder : left.Uid.CompareTo(right.Uid);
    }

    /// <summary>The query for the page after <paramref name="page"/>: its last message is the cursor, with that message's
    /// date and the page's snapshot in all-folder scope.</summary>
    public static MailHeaderSearchQuery Next(MailHeaderSearchQuery query, MailHeaderSearchResult page)
    {
        ArgumentNullException.ThrowIfNull(query); ArgumentNullException.ThrowIfNull(page);
        if (page.Messages.Count == 0) throw new ArgumentException("An empty page has no continuation.", nameof(page));
        var last = page.Messages[^1];
        return query with
        {
            BeforeMessage = last.Key,
            BeforeReceivedAt = query.MailboxId is null ? last.ReceivedAt : null,
            AccountMailboxSnapshot = query.MailboxId is null ? page.AccountMailboxSnapshot : null,
        };
    }

    private static int CompareMailbox(string left, string right) =>
        System.Text.Encoding.UTF8.GetBytes(left).AsSpan().SequenceCompareTo(System.Text.Encoding.UTF8.GetBytes(right));
}

/// <summary>Submitted all-folder paging no longer matches the saved mailbox epoch set.</summary>
public sealed class MailSearchScopeChangedException(string message) : IOException(message);
