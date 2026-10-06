// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

namespace Broiler.Mail.Infrastructure.Protocols.Smtp;

public enum SmtpErrorCode
{
    None,
    SenderNotAccepted,
    RecipientNotAccepted,
    MessageNotAccepted,
    ServiceUnavailable,
}

/// <summary>Exception thrown when the SMTP server returns an error response code.</summary>
public sealed class SmtpCommandException : Exception
{
    public int StatusCode { get; }
    public SmtpErrorCode ErrorCode { get; }

    public SmtpCommandException(int statusCode, string message, SmtpErrorCode errorCode = SmtpErrorCode.None)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}

/// <summary>Exception thrown when an unexpected or malformed response is received from the SMTP server.</summary>
public sealed class SmtpProtocolException : Exception
{
    public SmtpProtocolException(string message) : base(message) { }
}

/// <summary>Exception thrown when SMTP authentication credentials are rejected by the server.</summary>
public sealed class SmtpAuthenticationException : Exception
{
    public SmtpAuthenticationException(string message) : base(message) { }
}

/// <summary>Exception thrown when TLS handshake or certificate verification fails.</summary>
public sealed class TlsHandshakeException : Exception
{
    public TlsHandshakeException(string message, Exception? innerException = null) : base(message, innerException) { }
}
