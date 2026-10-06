// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

namespace Broiler.Mail.Infrastructure.Protocols.Imap;

/// <summary>Exception thrown when an IMAP server rejects a command (NO or BAD).</summary>
public sealed class ImapCommandException : Exception
{
    public string Tag { get; }
    public string ResponseText { get; }

    public ImapCommandException(string tag, string responseText)
        : base($"IMAP command '{tag}' rejected: {responseText}")
    {
        Tag = tag;
        ResponseText = responseText;
    }
}

/// <summary>Exception thrown when the IMAP server sends a malformed or unexpected response.</summary>
public sealed class ImapProtocolException : Exception
{
    public ImapProtocolException(string message) : base(message) { }
}

/// <summary>Exception thrown when IMAP authentication fails.</summary>
public sealed class ImapAuthenticationException : Exception
{
    public ImapAuthenticationException(string message) : base(message) { }
}

/// <summary>Exception thrown when a requested message UID is not found on the server.</summary>
public sealed class ImapMessageNotFoundException : Exception
{
    public ImapMessageNotFoundException(string message) : base(message) { }
}
