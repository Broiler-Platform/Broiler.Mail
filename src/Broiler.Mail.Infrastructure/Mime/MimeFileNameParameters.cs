using System.Globalization;
using System.Text;
using Broiler.Mail.Infrastructure.Mime.Encodings;

namespace Broiler.Mail.Infrastructure.Mime;

/// <summary>Bounded filename/name parameter decoding; ambiguous extraction names fail closed.</summary>
internal static class MimeFileNameParameters
{
    internal static string? Read(string? header, string name, bool strict)
    {
        if (header is null) return null;
        var values = new List<KeyValuePair<string, string>>();
        try
        {
            values = Tokenize(header);
            var matching = values.Where(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                pair.Key.StartsWith(name + "*", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Length == 0) return null;
            if (matching.GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
                throw Invalid("Duplicate filename parameter.");
            string? basic = matching.SingleOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
            var extended = matching.Where(pair => !pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (extended.Length == 0) return Rfc2047Decoder.Decode(basic);
            var single = extended.Where(pair => pair.Key.Equals(name + "*", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (single.Length != 0)
            {
                if (extended.Length != 1) throw Invalid("Conflicting extended filename forms.");
                return Decode([(single[0].Value, true)]);
            }
            var segments = new SortedDictionary<int, (string Value, bool Encoded)>();
            foreach (var pair in extended)
            {
                string suffix = pair.Key[(name.Length + 1)..];
                bool encoded = suffix.EndsWith('*');
                string number = encoded ? suffix[..^1] : suffix;
                if (number.Length == 0 || number.Length > 2 || number.Any(c => !char.IsAsciiDigit(c)) ||
                    number.Length > 1 && number[0] == '0' || !int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
                    index >= 32 || !segments.TryAdd(index, (pair.Value, encoded)))
                    throw Invalid("Invalid or duplicate filename continuation index (maximum 32 segments).");
            }
            if (segments.Count == 0 || segments.Keys.Where((value, index) => value != index).Any())
                throw Invalid("Filename continuation has a gap or lacks segment zero.");
            return Decode(segments.Values.ToArray());
        }
        catch (InvalidDataException) when (!strict)
        {
            // Preserve ordinary preview of malformed mail, but never promote named text to its body.
            return values.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value
                ?? (header.Contains(name, StringComparison.OrdinalIgnoreCase) ? "attachment.bin" : null);
        }
    }

    private static string Decode((string Value, bool Encoded)[] segments)
    {
        string? charset = null;
        var bytes = new List<byte>();
        for (int index = 0; index < segments.Length; index++)
        {
            var (value, encoded) = segments[index];
            if (index == 0 && encoded)
            {
                int first = value.IndexOf('\'');
                int second = first < 0 ? -1 : value.IndexOf('\'', first + 1);
                if (first < 0 || second < 0) throw Invalid("Extended filename lacks charset/language delimiters.");
                charset = value[..first];
                value = value[(second + 1)..];
            }
            if (value.Any(c => c > 127)) throw Invalid("Extended filename contains non-ASCII encoded data.");
            for (int offset = 0; offset < value.Length; offset++)
            {
                char current = value[offset];
                if (encoded && current == '%')
                {
                    if (offset + 2 >= value.Length || !Uri.IsHexDigit(value[offset + 1]) || !Uri.IsHexDigit(value[offset + 2]) || !byte.TryParse(value.AsSpan(offset + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte octet))
                        throw Invalid("Invalid filename percent escape.");
                    bytes.Add(octet); offset += 2;
                }
                else bytes.Add((byte)current);
                if (bytes.Count > 16 * 1024) throw Invalid("Decoded filename exceeds 16 KiB.");
            }
        }
        try
        {
            // No charset is guessed: unspecified charset is accepted only for ASCII octets.
            Encoding encoding = string.IsNullOrEmpty(charset) ? Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                : Encoding.GetEncoding(charset, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            return encoding.GetString(bytes.ToArray());
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException) { throw new InvalidDataException("Unsupported filename charset or invalid filename bytes.", error); }
    }

    private static List<KeyValuePair<string, string>> Tokenize(string header)
    {
        if (header.Length > 64 * 1024) throw Invalid("Filename header exceeds 64 KiB.");
        var result = new List<KeyValuePair<string, string>>();
        int offset = header.IndexOf(';');
        if (offset < 0) return result;
        offset++;
        while (offset < header.Length)
        {
            while (offset < header.Length && (char.IsWhiteSpace(header[offset]) || header[offset] == ';')) offset++;
            if (offset == header.Length) break;
            int start = offset;
            while (offset < header.Length && header[offset] is not '=' and not ';') offset++;
            if (offset == header.Length || header[offset] != '=') throw Invalid("Malformed MIME parameter.");
            string name = header[start..offset].Trim(); offset++;
            while (offset < header.Length && char.IsWhiteSpace(header[offset])) offset++;
            var value = new StringBuilder();
            if (offset < header.Length && header[offset] == '"')
            {
                offset++; bool closed = false;
                while (offset < header.Length)
                {
                    char c = header[offset++];
                    if (c == '"') { closed = true; break; }
                    if (c == '\\')
                    {
                        if (offset == header.Length) throw Invalid("Incomplete quoted filename escape.");
                        c = header[offset++];
                    }
                    value.Append(c);
                }
                if (!closed) throw Invalid("Unterminated quoted filename parameter.");
                while (offset < header.Length && char.IsWhiteSpace(header[offset])) offset++;
                if (offset < header.Length && header[offset] != ';') throw Invalid("Unexpected data after quoted parameter.");
            }
            else
            {
                start = offset;
                while (offset < header.Length && header[offset] != ';') offset++;
                value.Append(header[start..offset].Trim());
            }
            result.Add(new(name, value.ToString()));
            if (result.Count > 256) throw Invalid("Too many MIME parameters.");
        }
        return result;
    }
    private static InvalidDataException Invalid(string message) => new(message);
}
