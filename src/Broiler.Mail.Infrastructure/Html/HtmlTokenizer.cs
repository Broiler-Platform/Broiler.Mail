// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;

namespace Broiler.Mail.Infrastructure.Html;

/// <summary>Base class for tokens produced by <see cref="HtmlTokenizer"/>.</summary>
public abstract class HtmlToken
{
}

/// <summary>An attribute on an HTML tag.</summary>
public sealed class HtmlAttribute
{
    public string Name { get; }
    public string Value { get; }

    public HtmlAttribute(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public override string ToString() => $"{Name}=\"{Value}\"";
}

/// <summary>An opening, closing, or self-closing HTML tag.</summary>
public sealed class HtmlTagToken : HtmlToken
{
    public string Name { get; }
    public bool IsEndTag { get; }
    public bool IsEmptyElement { get; }
    public IReadOnlyList<HtmlAttribute> Attributes { get; }

    public HtmlTagToken(string name, bool isEndTag, bool isEmptyElement, IReadOnlyList<HtmlAttribute> attributes)
    {
        Name = name;
        IsEndTag = isEndTag;
        IsEmptyElement = isEmptyElement;
        Attributes = attributes;
    }

    public override string ToString() =>
        IsEndTag ? $"</{Name}>" : $"<{Name}{(IsEmptyElement ? " /" : "")}>";
}

/// <summary>Character data content between tags.</summary>
public sealed class HtmlDataToken : HtmlToken
{
    public string Data { get; }

    public HtmlDataToken(string data)
    {
        Data = data;
    }

    public override string ToString() => Data;
}

/// <summary>An HTML comment (&lt;!-- ... --&gt;).</summary>
public sealed class HtmlCommentToken : HtmlToken
{
    public string Comment { get; }

    public HtmlCommentToken(string comment)
    {
        Comment = comment;
    }

    public override string ToString() => $"<!--{Comment}-->";
}

/// <summary>An HTML DOCTYPE declaration (&lt;!DOCTYPE ...&gt;).</summary>
public sealed class HtmlDocTypeToken : HtmlToken
{
    public string Raw { get; }

    public HtmlDocTypeToken(string raw)
    {
        Raw = raw;
    }

    public override string ToString() => $"<!DOCTYPE {Raw}>";
}

/// <summary>
/// A streaming HTML tokenizer designed for security sanitization and plain-text extraction.
/// Completely standalone and trimmer-safe with zero external dependencies.
/// </summary>
public sealed class HtmlTokenizer : IDisposable
{
    private readonly TextReader _reader;
    private readonly bool _ownsReader;
    private int _peekChar = -2;

    public bool DecodeCharacterReferences { get; set; } = true;

    public HtmlTokenizer(TextReader reader, bool ownsReader = false)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _ownsReader = ownsReader;
    }

    public void Dispose()
    {
        if (_ownsReader)
        {
            _reader.Dispose();
        }
    }

    private int Peek()
    {
        if (_peekChar == -2)
        {
            _peekChar = _reader.Read();
        }
        return _peekChar;
    }

    private int Read()
    {
        int c = _peekChar != -2 ? _peekChar : _reader.Read();
        _peekChar = -2;
        return c;
    }

    /// <summary>
    /// Reads the next HTML token from the input stream.
    /// </summary>
    public bool ReadNextToken([NotNullWhen(true)] out HtmlToken? token)
    {
        while (true)
        {
            int next = Peek();
            if (next == -1)
            {
                token = null;
                return false;
            }

            if (next == '<')
            {
                Read(); // consume '<'
                token = ReadTagOrSpecial();
                if (token is not null)
                {
                    return true;
                }
                // If comments/doctype/PI was skipped, continue
                continue;
            }

            // Text data
            token = ReadData();
            return true;
        }
    }

    private HtmlDataToken ReadData()
    {
        var sb = new StringBuilder();
        while (true)
        {
            int c = Peek();
            if (c == -1 || c == '<')
            {
                break;
            }
            sb.Append((char)Read());
        }

        string text = sb.ToString();
        if (DecodeCharacterReferences && text.IndexOf('&') >= 0)
        {
            text = WebUtility.HtmlDecode(text);
        }
        return new HtmlDataToken(text);
    }

    private HtmlToken? ReadTagOrSpecial()
    {
        int first = Peek();
        if (first == -1)
        {
            return new HtmlDataToken("<");
        }

        // Comment, CDATA, DOCTYPE
        if (first == '!')
        {
            Read(); // consume '!'
            int second = Peek();
            if (second == '-')
            {
                Read(); // consume '-'
                if (Peek() == '-')
                {
                    Read(); // consume '-'
                    return ReadComment();
                }
                // Malformed <!--
                SkipUntil('>');
                return null;
            }

            // CDATA: <![CDATA[
            var prefix = new StringBuilder();
            while (prefix.Length < 7 && Peek() != -1 && Peek() != '>')
            {
                prefix.Append((char)Read());
            }

            if (prefix.ToString().Equals("[CDATA[", StringComparison.OrdinalIgnoreCase))
            {
                return ReadCData();
            }

            if (prefix.ToString().StartsWith("DOCTYPE", StringComparison.OrdinalIgnoreCase))
            {
                var doctype = new StringBuilder(prefix.ToString());
                while (Peek() != -1 && Peek() != '>')
                {
                    doctype.Append((char)Read());
                }
                if (Peek() == '>') Read();
                return new HtmlDocTypeToken(doctype.ToString());
            }

            // Other <! declarations
            SkipUntil('>');
            return null;
        }

        // Processing instruction <?xml ... ?>
        if (first == '?')
        {
            Read(); // consume '?'
            while (Peek() != -1)
            {
                int c = Read();
                if (c == '?' && Peek() == '>')
                {
                    Read();
                    break;
                }
            }
            return null;
        }

        bool isEndTag = false;
        if (first == '/')
        {
            isEndTag = true;
            Read(); // consume '/'
        }

        // Read tag name
        SkipWhitespace();
        var nameBuilder = new StringBuilder();
        while (true)
        {
            int c = Peek();
            if (c == -1 || char.IsWhiteSpace((char)c) || c == '>' || c == '/')
            {
                break;
            }
            nameBuilder.Append((char)Read());
        }

        string tagName = nameBuilder.ToString();
        if (tagName.Length == 0)
        {
            // Empty or malformed tag like <> or </>
            SkipUntil('>');
            return null;
        }

        // Read attributes
        var attributes = new List<HtmlAttribute>();
        bool isEmptyElement = false;

        while (true)
        {
            SkipWhitespace();
            int c = Peek();
            if (c == -1)
            {
                break;
            }
            if (c == '>')
            {
                Read(); // consume '>'
                break;
            }
            if (c == '/')
            {
                Read(); // consume '/'
                SkipWhitespace();
                if (Peek() == '>')
                {
                    Read(); // consume '>'
                    isEmptyElement = true;
                    break;
                }
                continue;
            }

            // Read attribute name
            var attrNameBuilder = new StringBuilder();
            while (true)
            {
                int ac = Peek();
                if (ac == -1 || char.IsWhiteSpace((char)ac) || ac == '=' || ac == '>' || ac == '/')
                {
                    break;
                }
                attrNameBuilder.Append((char)Read());
            }

            string attrName = attrNameBuilder.ToString();
            if (attrName.Length == 0)
            {
                Read(); // avoid infinite loop on unexpected char
                continue;
            }

            SkipWhitespace();
            string attrValue = "";
            if (Peek() == '=')
            {
                Read(); // consume '='
                SkipWhitespace();
                attrValue = ReadAttributeValue();
            }

            if (DecodeCharacterReferences && attrValue.IndexOf('&') >= 0)
            {
                attrValue = WebUtility.HtmlDecode(attrValue);
            }

            attributes.Add(new HtmlAttribute(attrName, attrValue));
        }

        return new HtmlTagToken(tagName, isEndTag, isEmptyElement, attributes);
    }

    private string ReadAttributeValue()
    {
        int quote = Peek();
        if (quote is '"' or '\'')
        {
            Read(); // consume quote
            var valBuilder = new StringBuilder();
            while (true)
            {
                int c = Read();
                if (c == -1 || c == quote)
                {
                    break;
                }
                valBuilder.Append((char)c);
            }
            return valBuilder.ToString();
        }

        // Unquoted attribute value
        var unquoted = new StringBuilder();
        while (true)
        {
            int c = Peek();
            if (c == -1 || char.IsWhiteSpace((char)c) || c == '>')
            {
                break;
            }
            unquoted.Append((char)Read());
        }
        return unquoted.ToString();
    }

    private HtmlCommentToken ReadComment()
    {
        var sb = new StringBuilder();
        while (true)
        {
            int c = Read();
            if (c == -1)
            {
                break;
            }
            if (c == '-' && Peek() == '-')
            {
                Read(); // consume 2nd '-'
                if (Peek() == '>')
                {
                    Read(); // consume '>'
                    break;
                }
                sb.Append("--");
                continue;
            }
            sb.Append((char)c);
        }
        return new HtmlCommentToken(sb.ToString());
    }

    private HtmlDataToken ReadCData()
    {
        var sb = new StringBuilder();
        while (true)
        {
            int c = Read();
            if (c == -1)
            {
                break;
            }
            if (c == ']' && Peek() == ']')
            {
                Read(); // consume 2nd ']'
                if (Peek() == '>')
                {
                    Read(); // consume '>'
                    break;
                }
                sb.Append("]]");
                continue;
            }
            sb.Append((char)c);
        }
        return new HtmlDataToken(sb.ToString());
    }

    private void SkipWhitespace()
    {
        while (true)
        {
            int c = Peek();
            if (c != -1 && char.IsWhiteSpace((char)c))
            {
                Read();
            }
            else
            {
                break;
            }
        }
    }

    private void SkipUntil(char endChar)
    {
        while (true)
        {
            int c = Read();
            if (c == -1 || c == endChar)
            {
                break;
            }
        }
    }
}
