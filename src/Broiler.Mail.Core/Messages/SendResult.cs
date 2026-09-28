namespace Broiler.Mail.Core.Messages;

/// <summary>SMTP acceptance is not a guarantee of delivery to the recipient.</summary>
public sealed record SendResult(SubmissionStatus Status, string? StatusMessage = null);

/// <summary>Unknown requires user review; it must never trigger an automatic resend.</summary>
public enum SubmissionStatus { Unknown, Accepted, Rejected }
