// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   22
// Annotated:        0/22
// Exempt:           4
// Human-reviewed:   0/22
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       22
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Infrastructure.Protocols.Imap;

/// <summary>One parsed <c>* n FETCH (UID u BODYSTRUCTURE (...))</c> response. <see cref="HasAttachments"/> is null
/// when the structure was missing, malformed or over budget.</summary>
internal sealed record ImapAttachmentHint(int SequenceNumber, uint Uid, bool? HasAttachments);

/// <summary>
/// Bounded reader for IMAP BODYSTRUCTURE (RFC 3501 §7.4.2, RFC 9051 §7.5.2) that answers one question: does the
/// message have an attachment by the same rule <c>MimeParser</c> uses? A part counts when its disposition is
/// <c>attachment</c>, or when it names a file, is not <c>inline</c> and has neither a Content-ID nor a
/// Content-Location. A counted multipart container is not descended into, and an embedded message/rfc822 is a
/// leaf, exactly as the MIME parser treats them. Anything malformed or beyond the budget gives an unknown hint.
/// </summary>
internal static class ImapBodyStructure
{
    /// <summary>Deepest multipart nesting evaluated; equals the MIME parser's nesting limit.</summary>
    public const int MaximumPartDepth = 32;
    /// <summary>Most MIME parts evaluated in one structure; equals the MIME parser's part limit.</summary>
    public const int MaximumParts = 256;
    /// <summary>Most tokens (lists, strings, atoms, NILs) one FETCH response may contain.</summary>
    public const int MaximumNodes = 4096;
    /// <summary>Deepest parenthesis nesting of one FETCH response.</summary>
    public const int MaximumListDepth = 80;
    /// <summary>Largest single literal in a BODYSTRUCTURE response.</summary>
    public const int MaximumLiteralBytes = 8 * 1024;
    /// <summary>Largest FETCH response, including literal contents.</summary>
    public const int MaximumResponseLength = 64 * 1024;

    private sealed class Node
    {
        public string? Text;
        public List<Node>? Items;
        public bool IsList => Items is not null;
        public bool IsNil => Text is null && Items is null;
    }

    private sealed class Budget { public int Nodes; public int Parts; }

    /// <summary>Parses one untagged FETCH response (with any literals inline as <c>{n}\r\n</c> followed by n characters).
    /// Returns null when the text is not a FETCH response that names a UID.</summary>
    public static ImapAttachmentHint? ParseFetchResponse(string response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Length > MaximumResponseLength || !response.StartsWith("* ", StringComparison.Ordinal)) return null;
        int space = response.IndexOf(' ', 2);
        if (space < 0 || !int.TryParse(response.AsSpan(2, space - 2), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int sequence) || sequence <= 0) return null;
        const string fetch = " FETCH ";
        if (!response.AsSpan(space).StartsWith(fetch, StringComparison.OrdinalIgnoreCase)) return null;
        int index = space + fetch.Length;
        var budget = new Budget();
        Node? attributes = Read(response, ref index, 0, budget);
        if (attributes is not { IsList: true } || index != response.Length) return null;
        uint uid = 0; Node? structure = null; bool repeated = false;
        var items = attributes.Items!;
        for (int i = 0; i + 1 < items.Count; i += 2)
        {
            string? name = items[i].Text;
            if (string.Equals(name, "UID", StringComparison.OrdinalIgnoreCase))
            {
                if (uid != 0 || !uint.TryParse(items[i + 1].Text, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out uid) || uid == 0) return null;
            }
            else if (string.Equals(name, "BODYSTRUCTURE", StringComparison.OrdinalIgnoreCase))
            {
                repeated |= structure is not null;
                structure = items[i + 1];
            }
        }
        if (uid == 0 || items.Count % 2 != 0) return null;
        bool? hint = structure is null || repeated ? null : Evaluate(structure, 1, budget);
        return new(sequence, uid, hint);
    }

    /// <summary>Evaluates one parenthesized BODYSTRUCTURE value; null when it is malformed or over budget.</summary>
    public static bool? HasAttachments(string structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (structure.Length > MaximumResponseLength) return null;
        int index = 0; var budget = new Budget();
        Node? node = Read(structure, ref index, 0, budget);
        SkipSpaces(structure, ref index);
        return node is null || index != structure.Length ? null : Evaluate(node, 1, budget);
    }

    private static bool? Evaluate(Node body, int depth, Budget budget)
    {
        if (body.Items is not { Count: > 0 } items || depth > MaximumPartDepth || ++budget.Parts > MaximumParts) return null;
        if (items[0].IsList)
        {
            int count = 0;
            while (count < items.Count && items[count].IsList) count++;
            if (count == items.Count || items[count].Text is null) return null; // media subtype is required
            Node? parameters = At(items, count + 1), disposition = At(items, count + 2), location = At(items, count + 4);
            if (!IsParameterList(parameters)) return null;
            bool? container = IsAttachment(parameters, null, disposition, location);
            if (container is null) return null;
            if (container.Value) return true;
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                bool? part = Evaluate(items[i], depth + 1, budget);
                if (part is null) return null;
                any |= part.Value;
            }
            return any;
        }
        if (items.Count < 7 || items[0].Text is null || items[1].Text is null || !IsParameterList(items[2]) ||
            items[3].IsList || items[4].IsList || items[5].IsList || !IsNumber(items[6])) return null;
        int extensions = 7;
        if (string.Equals(items[0].Text, "TEXT", StringComparison.OrdinalIgnoreCase))
        {
            if (items.Count < 8 || !IsNumber(items[7])) return null;
            extensions = 8;
        }
        else if (string.Equals(items[0].Text, "MESSAGE", StringComparison.OrdinalIgnoreCase) && items.Count >= 10 &&
                 items[7].IsList && items[8].IsList)
        {
            // envelope, embedded body and line count; the embedded message is one leaf, as in the MIME parser.
            if (!IsNumber(items[9])) return null;
            extensions = 10;
        }
        return IsAttachment(items[2], items[3], At(items, extensions + 1), At(items, extensions + 3));
    }

    // Same decision as MimeParser.ParseEntity: disposition attachment, or a file name that is not inline and has
    // no Content-ID or Content-Location. Returns null for a disposition of the wrong shape.
    private static bool? IsAttachment(Node? contentParameters, Node? contentId, Node? disposition, Node? location)
    {
        string kind = "";
        Node? dispositionParameters = null;
        if (disposition is { IsList: true })
        {
            var parts = disposition.Items!;
            if (parts.Count < 1 || parts[0].Text is null || parts.Count > 1 && !IsParameterList(parts[1])) return null;
            kind = parts[0].Text!.Trim().ToLowerInvariant();
            dispositionParameters = At(parts, 1);
        }
        else if (disposition is { IsNil: false }) return null;
        if (kind == "attachment") return true;
        bool named = HasParameter(dispositionParameters, "filename") || HasParameter(contentParameters, "name");
        return named && kind != "inline" && string.IsNullOrWhiteSpace(contentId?.Text) && string.IsNullOrWhiteSpace(location?.Text);
    }

    // Matches name, name*, name*0 and name*0* (RFC 2231) with a non-empty value.
    private static bool HasParameter(Node? parameters, string name)
    {
        if (parameters?.Items is not { } items) return false;
        for (int i = 0; i + 1 < items.Count; i += 2)
        {
            string? key = items[i].Text;
            if (key is null || !key.StartsWith(name, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(items[i + 1].Text)) continue;
            if (key.Length == name.Length || key[name.Length] == '*') return true;
        }
        return false;
    }

    private static bool IsParameterList(Node? node) =>
        node is null || node.IsNil || node.Items is { } items && items.Count % 2 == 0 && items.TrueForAll(item => item.Text is not null);

    private static bool IsNumber(Node node) =>
        node.Text is { Length: > 0 and <= 20 } text && text.All(char.IsAsciiDigit);

    private static Node? At(List<Node> items, int index) => index < items.Count ? items[index] : null;

    private static void SkipSpaces(string text, ref int index)
    {
        while (index < text.Length && text[index] == ' ') index++;
    }

    private static Node? Read(string text, ref int index, int depth, Budget budget)
    {
        SkipSpaces(text, ref index);
        if (index >= text.Length || ++budget.Nodes > MaximumNodes || depth > MaximumListDepth) return null;
        char first = text[index];
        if (first == '(')
        {
            index++;
            var items = new List<Node>();
            while (true)
            {
                SkipSpaces(text, ref index);
                if (index >= text.Length) return null;
                if (text[index] == ')') { index++; return new Node { Items = items }; }
                var item = Read(text, ref index, depth + 1, budget);
                if (item is null) return null;
                items.Add(item);
            }
        }
        if (first == ')') return null;
        if (first == '"')
        {
            var value = new System.Text.StringBuilder();
            for (index++; index < text.Length; index++)
            {
                char c = text[index];
                if (c == '"') { index++; return new Node { Text = value.ToString() }; }
                if (c is '\r' or '\n') return null;
                if (c == '\\')
                {
                    if (++index >= text.Length || text[index] is not ('\\' or '"')) return null;
                    c = text[index];
                }
                value.Append(c);
            }
            return null;
        }
        if (first == '{')
        {
            int close = text.IndexOf('}', index);
            if (close < 0 || close - index - 1 is < 1 or > 6 ||
                !int.TryParse(text.AsSpan(index + 1, close - index - 1), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int length) ||
                length > MaximumLiteralBytes || close + 2 >= text.Length ||
                text[close + 1] != '\r' || text[close + 2] != '\n' || close + 3 + length > text.Length) return null;
            int start = close + 3;
            index = start + length;
            return new Node { Text = text.Substring(start, length) };
        }
        int atomStart = index;
        while (index < text.Length && text[index] is not (' ' or '(' or ')' or '"' or '{' or '\r' or '\n') && text[index] >= ' ' && text[index] < '\u007f') index++;
        if (index == atomStart) return null;
        string atom = text[atomStart..index];
        return atom.Equals("NIL", StringComparison.OrdinalIgnoreCase) ? new Node() : new Node { Text = atom };
    }
}
