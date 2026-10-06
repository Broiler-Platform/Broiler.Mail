// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   9
// Annotated:        0/9
// Exempt:           7
// Human-reviewed:   0/9
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       9
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Windows;

/// <summary>
/// The new-mail fixture's synthetic server. It starts like the inbox fixture, with UIDs 1 to 55, and
/// every newest-page receive after the first behaves as if another client used the same account:
/// <see cref="Arrivals"/> newer messages arrive and the message this app has open is marked read.
/// The receive after the fixture's own two can instead apply a <see cref="DemoServerChange"/>.
/// Older pages are read by index, so a cursor from before a change is refused, as IMAP does.
/// </summary>
internal sealed class DemoMailbox(DemoServerChange change)
{
    /// <summary>Messages that arrive with each ordinary receive after the first.</summary>
    public const int Arrivals = 3;
    /// <summary>The fixture receives twice (the inbox, then new mail while reading); a server change applies to the receive after that.</summary>
    public const int FixtureReceives = 2;
    public const uint InitialCount = 55;

    private readonly Lock _gate = new();
    private readonly List<uint> _uids = [.. Enumerable.Range(1, (int)InitialCount).Select(uid => (uint)uid)];
    private readonly HashSet<uint> _read = [.. Enumerable.Range(1, (int)InitialCount).Where(uid => uid % 3 == 0).Select(uid => (uint)uid)];
    private uint _uidValidity = 1;
    private uint _uidNext = InitialCount + 1;
    private int _receives;
    private uint? _open;

    /// <summary>Records the message whose body was fetched last, which is the open one as the server sees it.</summary>
    public void Opened(MailMessageKey key)
    {
        lock (_gate)
            if (key.UidValidity == _uidValidity) _open = key.Uid;
    }

    public bool Contains(MailMessageKey key)
    {
        lock (_gate) return key.UidValidity == _uidValidity && _uids.BinarySearch(key.Uid) >= 0;
    }

    /// <summary>The newest page (and the server's changes since the last receive), or an older page after <paramref name="older"/>.</summary>
    public MailInboxPage GetInbox(AccountId account, int maximumCount, MailInboxCursor? older, Func<MailMessageKey, bool, MailMessageSummary> summary)
    {
        lock (_gate)
        {
            if (older is null)
            {
                if (++_receives > 1) Change(maximumCount);
            }
            else if (older.AccountId != account || older.NextIndex < 0 || older.NextIndex >= older.MessageCount)
                throw new ArgumentException("The inbox continuation does not belong to this request.", nameof(older));
            else if (older.UidValidity != _uidValidity || older.UidNext != _uidNext || older.MessageCount != _uids.Count)
                throw new MailConnectionException("The inbox changed. Receive mail again before loading more messages.");
            int count = _uids.Count;
            if (count == 0) return new([], null);
            int end = older?.NextIndex ?? count - 1;
            int start = Math.Max(0, end - maximumCount + 1);
            var messages = Enumerable.Range(start, end - start + 1).Reverse()
                .Select(index => summary(new(account, "INBOX", _uidValidity, _uids[index]), _read.Contains(_uids[index]))).ToArray();
            return new(messages, start == 0 ? null : new(account, _uidValidity, _uidNext, count, start - 1));
        }
    }

    private void Change(int pageSize)
    {
        // Another client read the open message, so its row changes while the body stays.
        if (_open is { } open) _read.Add(open);
        switch (_receives == FixtureReceives + 1 ? change : DemoServerChange.None)
        {
            // Nothing arrives, so the open message's UID stays within the refreshed page's range.
            case DemoServerChange.Vanish:
                if (_open is { } removed) _uids.Remove(removed);
                break;
            // A full page of new mail pushes the open message below the newest page.
            case DemoServerChange.Outside:
                Arrive(pageSize);
                break;
            case DemoServerChange.Renumber:
                _uidValidity++;
                _open = null;
                Arrive(Arrivals);
                break;
            default:
                Arrive(Arrivals);
                break;
        }
    }

    private void Arrive(int count)
    {
        for (int index = 0; index < count; index++) _uids.Add(_uidNext++);
    }
}
