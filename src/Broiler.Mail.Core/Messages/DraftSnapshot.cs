using System.Text.Json.Serialization;

namespace Broiler.Mail.Core.Messages;

public enum DraftSubmissionState { Editing, Sending, Accepted, Failed, Unknown }
public enum SentCopyState { NotRequested, ProviderManaged, Pending, Saved, Failed, Unknown }

/// <summary>Raw recipient edits may be incomplete or invalid. Saving never requires send validation.</summary>
public sealed record DraftSnapshot
{
    public required MailDraft Draft { get; init; }
    public required string ToText { get; init; }
    public required string CcText { get; init; }
    public required string BccText { get; init; }
    [JsonRequired] public DraftSubmissionState State { get; init; }
    public SentCopyState SentCopy { get; init; }
    public string? SentCopyFolder { get; init; }
}

/// <summary>A null draft is a revisioned tombstone, preventing stale writers from recreating a discarded draft.</summary>
public sealed record DraftStoreState([property: JsonRequired] long Revision, [property: JsonRequired] DraftSnapshot? Draft);
