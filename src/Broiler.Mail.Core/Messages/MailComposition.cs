// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   12
// Annotated:        12/12
// Exempt:           3
// Human-reviewed:   0/12
// IP risk:          Low
// Security risk:    High
// Criteria:         11/8
// Resource impact:  3/10 max
// Unverified:       12
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Net.Mail;
using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Messages;

// Broiler-AI:           Origin=AI; IP=None; Security=None; Resources=0; Fingerprint=D4B65B
// Broiler-Human:        PENDING
public enum CompositionKind { Reply, ReplyAll, Forward }

/// <summary>Mail-specific recipient and threading rules; never sends or writes a draft.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=659CA5
// Broiler-Falsified-If: a draft whose subject or a References entry contains CR or LF passes ValidateDraft and reaches the outgoing MIME headers
// Broiler-Human:        PENDING
public static class MailComposition
{
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=35F557
    // Broiler-Falsified-If: a draft naming 101 recipients across To, Cc and Bcc passes ValidateDraft
    // Broiler-Human:        PENDING
    public const int MaximumRecipients = 100;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=4D7692
    // Broiler-Falsified-If: a 999-character subject passes ValidateDraft
    // Broiler-Human:        PENDING
    public const int MaximumSubjectLength = 998;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=82B420
    // Broiler-Falsified-If: a plain-text body of 100,001 characters passes ValidateDraft
    // Broiler-Human:        PENDING
    public const int MaximumBodyLength = 100_000;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=49ECCA
    // Broiler-Falsified-If: a draft carrying 101 References entries passes ValidateDraft
    // Broiler-Human:        PENDING
    public const int MaximumReferences = 100;

    /// <summary>Shared composer/transport validation; the transport must not trust UI validation.</summary>
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=B32F87
    // Broiler-Falsified-If: a draft whose AccountId or FromAddress no longer matches the enabled account passes and is submitted under that account's credential
    // Broiler-Human:        PENDING
    public static void ValidateDraft(AccountProfile account, MailDraft draft)
    {
        if (!account.IsEnabled || draft.AccountId != account.Id || draft.FromAddress != account.EmailAddress || draft.Id == Guid.Empty)
            throw new InvalidOperationException("The saved sender changed. This draft retains its original sender; restore that account identity or start a new draft.");
        var recipients = draft.To.Concat(draft.Cc).Concat(draft.Bcc).ToArray();
        if (recipients.Length is 0 or > MaximumRecipients)
            throw new ArgumentException("Enter between 1 and 100 recipients across To, Cc, and Bcc.");
        if (recipients.Any(value => value is null || value.Length > 320 || value.Any(char.IsControl) || !MailAddress.TryCreate(value, out var address) || address.Address != value))
            throw new ArgumentException("Enter valid recipient email addresses.");
        if (draft.Subject is null || draft.Subject.Length > MaximumSubjectLength || draft.Subject.Any(char.IsControl))
            throw new ArgumentException("The subject must be at most 998 characters and contain no control characters.");
        if (draft.PlainText is null || draft.PlainText.Length > MaximumBodyLength || draft.PlainText.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t'))
            throw new ArgumentException("The body must be at most 100,000 characters and contain only text, tabs, and line breaks.");
        static bool InvalidId(string value) => string.IsNullOrWhiteSpace(value) || value.Length > 998 || value.Any(c => char.IsControl(c) || c is '<' or '>');
        if (draft.References.Count > MaximumReferences || draft.References.Any(InvalidId) || draft.InReplyTo is { } parent && InvalidId(parent))
            throw new ArgumentException("Invalid reply-thread identifiers.");
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=5934D2
    // Broiler-Falsified-If: the new draft's AccountId or FromAddress differs from the account it was created for
    // Broiler-Human:        PENDING
    public static MailDraft Create(AccountProfile account) => new()
    {
        AccountId = account.Id, FromAddress = account.EmailAddress,
    };

    // Broiler-AI:           Origin=AI; Spec=RFC-5322 s3.6.4; IP=Low; Security=High; Resources=3; Fingerprint=0BAEC6
    // Broiler-Falsified-If: a reply-all to a received message that lists the account's own address in To or Cc, in any letter case, puts that address among the new recipients
    // Broiler-Human:        PENDING
    public static MailDraft Create(AccountProfile account, MailMessageBody body, CompositionKind kind)
    {
        if (body.Key.AccountId != account.Id)
            throw new ArgumentException("The selected message belongs to another account.");
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var source = body.Composition ?? throw new ArgumentException(body.CompositionUnavailableReason ?? "Read the message again to load its reply headers.");
        string note = body.IsTruncated ? "[Quoted preview is truncated.]\n" : "";
        if (body.IsHtmlFallback) note += "[Quoted text was extracted from HTML.]\n";
        var draft = Create(account);
        if (kind == CompositionKind.Forward)
            return draft with
            {
                Subject = Prefix(source.Subject, "Fwd:"),
                PlainText = $"\n\n---------- Forwarded message ----------\nFrom: {string.Join(", ", source.From)}\nTo: {string.Join(", ", source.To)}\nCc: {string.Join(", ", source.Cc)}\nSubject: {source.Subject}\n{note}\n{body.PlainText}",
            };

        // Reply-To replaces From. Reply-all also includes visible original recipients; never Bcc.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { account.EmailAddress };
        string[] to = Unique(source.ReplyTo.Count > 0 ? source.ReplyTo : source.From, seen);
        if (kind == CompositionKind.ReplyAll) to = to.Concat(Unique(source.To, seen)).ToArray();
        string[] cc = kind == CompositionKind.ReplyAll ? Unique(source.Cc, seen) : [];
        if (to.Length + cc.Length > MaximumRecipients)
            throw new ArgumentException("This message has too many recipients to prepare a reply.");
        // RFC 5322 3.6.4: parent's References, or its sole In-Reply-To, followed by its Message-ID.
        IEnumerable<string> references = source.References.Count > 0 ? source.References
            : source.InReplyTo.Count == 1 ? source.InReplyTo : [];
        if (source.MessageId is { } parentId) references = references.Append(parentId);
        var thread = references.ToArray();
        if (thread.Length > MaximumReferences)
            throw new ArgumentException("This conversation has too many thread references to prepare a reply.");
        return draft with
        {
            To = to, Cc = cc, Subject = Prefix(source.Subject, "Re:"),
            InReplyTo = source.MessageId, References = thread,
            PlainText = "\n\n" + note + string.Join("\n", body.PlainText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Select(line => "> " + line)),
        };
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=2C4912
    // Broiler-Falsified-If: recipient text naming 101 addresses is returned as a list instead of raising ArgumentException
    // Broiler-Human:        PENDING
    public static IReadOnlyList<string> ParseRecipients(string text)
    {
        if (text.Length > 16_000 || text.Any(char.IsControl))
            throw new ArgumentException("Recipient fields must be at most 16,000 characters and contain no control characters.");
        if (string.IsNullOrWhiteSpace(text)) return [];
        var addresses = new MailAddressCollection();
        try { addresses.Add(text); }
        catch (FormatException) { throw new ArgumentException("Enter valid email addresses separated by commas."); }
        if (addresses.Count > MaximumRecipients) throw new ArgumentException("Use at most 100 recipients.");
        return addresses.Select(address => address.Address).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=2; Fingerprint=7298F7
    // Broiler-Falsified-If: an address already in the seen set, written in different letter case, is returned again as a recipient
    // Broiler-Human:        PENDING
    private static string[] Unique(IEnumerable<string> addresses, HashSet<string> seen) => addresses.Where(seen.Add).ToArray();
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=44F6D8
    // Broiler-Falsified-If: a subject that already starts with "RE:", or with "Fw:" when forwarding, gains a second prefix
    // Broiler-Human:        PENDING
    private static string Prefix(string subject, string prefix) =>
        subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || prefix == "Fwd:" && subject.StartsWith("Fw:", StringComparison.OrdinalIgnoreCase)
            ? subject : prefix + " " + subject;
}
