namespace Broiler.Mail.Core.Services;

/// <summary>A safe user-facing connection failure. Must not include remote response text or secrets.</summary>
public sealed class MailConnectionException(string message) : Exception(message);
