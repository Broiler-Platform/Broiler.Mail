using System.Net.Mail;
using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Messages;

public enum CompositionKind { Reply, ReplyAll, Forward }

/// <summary>Mail-specific recipient and threading rules; never sends or writes a draft.</summary>
public static class MailComposition
{
    public const int MaximumRecipients = 100;
    public const int MaximumSubjectLength = 998;
    public const int MaximumBodyLength = 100_000;
    public const int MaximumReferences = 100;

    /// <summary>Shared composer/transport validation; the transport must not trust UI validation.</summary>
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

    public static MailDraft Create(AccountProfile account) => new()
    {
        AccountId = account.Id, FromAddress = account.EmailAddress,
    };

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

    private static string[] Unique(IEnumerable<string> addresses, HashSet<string> seen) => addresses.Where(seen.Add).ToArray();
    private static string Prefix(string subject, string prefix) =>
        subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || prefix == "Fwd:" && subject.StartsWith("Fw:", StringComparison.OrdinalIgnoreCase)
            ? subject : prefix + " " + subject;
}
