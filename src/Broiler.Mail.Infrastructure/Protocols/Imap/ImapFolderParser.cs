using System.Text;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Infrastructure.Protocols.Imap;

internal static class ImapFolderParser
{
    internal static MailFolder Parse(string line, string? literal = null)
    {
        int index = 0;
        if (!line.StartsWith("* LIST (", StringComparison.OrdinalIgnoreCase)) throw Invalid();
        index = 8;
        int close = line.IndexOf(')', index);
        if (close < 0) throw Invalid();
        string[] attributes = line[index..close].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (attributes.Length > 32 || attributes.Any(value => value.Length > 64 || value[0] != '\\' ||
            value.Length == 1 || value.Skip(1).Any(character => character <= ' ' || character > '~' ||
                "(){%*\"\\]".Contains(character))))
            throw Invalid();
        index = close + 1;
        Space(line, ref index);
        string? delimiter;
        if (line.AsSpan(index).StartsWith("NIL ", StringComparison.OrdinalIgnoreCase)) { delimiter = null; index += 3; }
        else
        {
            delimiter = Quoted(line, ref index);
            if (delimiter.Length != 1 || char.IsControl(delimiter[0]) || delimiter[0] > 127) throw Invalid();
        }
        Space(line, ref index);
        string name;
        if (literal is not null)
        {
            if (line[index..] != "{" + literal.Length + "}") throw Invalid();
            name = literal;
        }
        else if (index < line.Length && line[index] == '"') name = Quoted(line, ref index);
        else
        {
            name = line[index..]; index = line.Length;
            if (name.Any(value => value <= ' ' || "(){\"\\".Contains(value))) throw Invalid();
        }
        if (literal is null && index != line.Length) throw Invalid();
        return new(ImapMailboxName.Decode(name), delimiter,
            !attributes.Contains("\\Noselect", StringComparer.OrdinalIgnoreCase) &&
            !attributes.Contains("\\NonExistent", StringComparer.OrdinalIgnoreCase), Array.AsReadOnly(attributes));
    }

    private static void Space(string text, ref int index)
    {
        if (index >= text.Length || text[index++] != ' ') throw Invalid();
    }

    private static string Quoted(string text, ref int index)
    {
        if (index >= text.Length || text[index++] != '"') throw Invalid();
        var value = new StringBuilder();
        while (index < text.Length)
        {
            char character = text[index++];
            if (character == '"') return value.ToString();
            if (character == '\\')
            {
                if (index >= text.Length || (text[index] != '\\' && text[index] != '"')) throw Invalid();
                character = text[index++];
            }
            if (character < ' ' || character > '~') throw Invalid();
            value.Append(character);
        }
        throw Invalid();
    }

    private static ImapProtocolException Invalid() => new("Invalid LIST response.");
}
