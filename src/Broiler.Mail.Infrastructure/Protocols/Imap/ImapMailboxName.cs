using System.Text;

namespace Broiler.Mail.Infrastructure.Protocols.Imap;

/// <summary>IMAP4rev1 mailbox names (RFC 3501 section 5.1.3).</summary>
internal static class ImapMailboxName
{
    private static readonly Encoding StrictBigEndian = new UnicodeEncoding(true, false, true);

    internal static string Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 1024 || name.Any(char.IsControl))
            throw new ArgumentException("A bounded, control-free mailbox name is required.", nameof(name));
        try { _ = StrictBigEndian.GetByteCount(name); }
        catch (EncoderFallbackException error) { throw new ArgumentException("Invalid Unicode mailbox name.", nameof(name), error); }
        return string.Equals(name, "INBOX", StringComparison.OrdinalIgnoreCase) ? "INBOX" : name;
    }

    internal static string Decode(string wire)
    {
        if (wire.Length > 4096 || wire.Any(value => value < ' ' || value > '~'))
            throw new ImapProtocolException("Invalid mailbox encoding.");
        var result = new StringBuilder();
        for (int index = 0; index < wire.Length; index++)
        {
            if (wire[index] != '&') { result.Append(wire[index]); continue; }
            int end = wire.IndexOf('-', index + 1);
            if (end < 0) throw new ImapProtocolException("Unterminated mailbox encoding.");
            string encoded = wire[(index + 1)..end];
            if (encoded.Length == 0) result.Append('&');
            else
            {
                if (encoded.Any(value => !char.IsAsciiLetterOrDigit(value) && value != '+' && value != ','))
                    throw new ImapProtocolException("Invalid mailbox encoding.");
                try
                {
                    string base64 = encoded.Replace(',', '/');
                    base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
                    result.Append(StrictBigEndian.GetString(Convert.FromBase64String(base64)));
                }
                catch (Exception error) when (error is FormatException or DecoderFallbackException)
                { throw new ImapProtocolException("Invalid mailbox encoding."); }
            }
            index = end;
        }
        try
        {
            string decoded = result.ToString();
            if (Encode(decoded) != wire && !(string.Equals(decoded, "INBOX", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(wire, "INBOX", StringComparison.OrdinalIgnoreCase)))
                throw new ImapProtocolException("Noncanonical mailbox encoding.");
            return Validate(decoded);
        }
        catch (ArgumentException) { throw new ImapProtocolException("Invalid mailbox identity."); }
    }

    internal static string Encode(string name)
    {
        name = Validate(name);
        var result = new StringBuilder();
        for (int index = 0; index < name.Length;)
        {
            char value = name[index];
            if (value >= ' ' && value <= '~')
            {
                result.Append(value == '&' ? "&-" : value.ToString());
                index++;
                continue;
            }
            int start = index++;
            while (index < name.Length && (name[index] < ' ' || name[index] > '~')) index++;
            result.Append('&').Append(Convert.ToBase64String(StrictBigEndian.GetBytes(name[start..index]))
                .TrimEnd('=').Replace('/', ',')).Append('-');
        }
        return result.ToString();
    }
}
