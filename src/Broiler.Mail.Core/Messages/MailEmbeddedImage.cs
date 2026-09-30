namespace Broiler.Mail.Core.Messages;

/// <summary>Bounded inline image parsed from a MIME multipart/related message.</summary>
public sealed record MailEmbeddedImage(string ContentId, string ContentType, byte[] Data)
{
    public const int MaximumImageBytes = 1_048_576;
    public const int MaximumTotalBytes = 2_097_152;
    public const int MaximumImageCount = 16;
}
