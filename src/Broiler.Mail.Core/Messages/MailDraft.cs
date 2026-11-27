// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   10
// Annotated:        1/10
// Exempt:           13
// Human-reviewed:   0/10
// IP risk:          None
// Security risk:    Medium
// Criteria:         1/0
// Resource impact:  1/10 max
// Unverified:       10
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using System.Text.Json.Serialization;

namespace Broiler.Mail.Core.Messages;

/// <summary>A plain-text composition snapshot with a pinned sender and optional reply thread.</summary>
// Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=1; Fingerprint=1104C4
// Hairs-Falsified-If: a saved draft whose JSON omits the id deserializes with a fresh Guid, so its Message-ID differs from the one already submitted
// Hairs-Human:        PENDING
public sealed record MailDraft
{
    [JsonRequired] public Guid Id { get; init; } = Guid.NewGuid();
    public required AccountId AccountId { get; init; }
    [JsonRequired] public string FromAddress { get; init; } = string.Empty;
    /// <summary>Pinned for newly created drafts; null preserves legacy account-name behavior.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FromDisplayName { get; init; }
    public DateTimeOffset? SubmissionDate { get; init; }
    public IReadOnlyList<string> To { get; init; } = [];
    public IReadOnlyList<string> Cc { get; init; } = [];
    public IReadOnlyList<string> Bcc { get; init; } = [];
    public string Subject { get; init; } = string.Empty;
    public string PlainText { get; init; } = string.Empty;
    public string? InReplyTo { get; init; }
    public IReadOnlyList<string> References { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DraftAttachment>? Attachments { get; init; }
}

public sealed record DraftAttachment(string FileName, ReadOnlyMemory<byte> Content)
{
    public const int MaximumCount = 4;
    public const int MaximumBytes = 8 * 1024 * 1024;
    public const int MaximumProfileBytes = 16 * 1024 * 1024;
    /// <summary>The longest file name a draft keeps, in characters; <see cref="MaximumFileNameBytes"/> also applies.</summary>
    public const int MaximumFileNameLength = 120;
    /// <summary>The longest file name a draft keeps, in UTF-8 bytes.</summary>
    public const int MaximumFileNameBytes = 240;
    /// <summary>The name used when nothing of the original name can be kept.</summary>
    public const string FallbackFileName = "attachment.bin";

    /// <summary>
    /// Turns a picked or typed file name into one <see cref="Validate"/> accepts: the last path segment, with control
    /// characters, '/', '\', ':' and each surrogate pair (such as an emoji) replaced by '_', trailing dots and spaces
    /// removed, and at most <see cref="MaximumFileNameLength"/> characters and <see cref="MaximumFileNameBytes"/> UTF-8
    /// bytes, keeping a short extension. A name with nothing left becomes <see cref="FallbackFileName"/>.
    /// </summary>
    public static string SafeName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return FallbackFileName;
        int separator = name.LastIndexOfAny(['/', '\\']);
        string segment = separator >= 0 ? name[(separator + 1)..] : name;
        var safe = new System.Text.StringBuilder(segment.Length);
        for (int index = 0; index < segment.Length; index++)
        {
            char c = segment[index];
            if (char.IsHighSurrogate(c) && index + 1 < segment.Length && char.IsLowSurrogate(segment[index + 1])) index++;
            safe.Append(char.IsControl(c) || char.IsSurrogate(c) || c is '/' or '\\' or ':' ? '_' : c);
        }
        string result = safe.ToString().TrimEnd('.', ' ');
        if (!Fits(result))
        {
            // Keep a short extension so the shortened file still opens with the right program.
            int dot = result.LastIndexOf('.');
            string extension = dot > 0 && result.Length - dot <= 16 ? result[dot..] : "";
            string stem = result[..(result.Length - extension.Length)];
            while (stem.Length > 0 && !Fits(stem + extension)) stem = stem[..^1];
            stem = stem.TrimEnd('.', ' ');
            result = stem.Length > 0 ? stem + extension : extension.TrimStart('.');
            while (result.Length > 0 && !Fits(result)) result = result[..^1];
            result = result.TrimEnd('.', ' ');
        }
        return result is "" or "." or ".." ? FallbackFileName : result;

        static bool Fits(string value) => value.Length <= MaximumFileNameLength &&
            System.Text.Encoding.UTF8.GetByteCount(value) <= MaximumFileNameBytes;
    }

    public static void Validate(IReadOnlyList<DraftAttachment>? attachments)
    {
        if (attachments is null) return;
        if (attachments.Count > MaximumCount) throw new ArgumentException("Use at most four attachments per draft.");
        int bytes = 0;
        foreach (var attachment in attachments)
        {
            if (attachment?.FileName is not { Length: > 0 and <= 120 } name ||
                name is "." or ".." || name.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' || char.IsSurrogate(c)) ||
                System.Text.Encoding.UTF8.GetByteCount(name) > 240 ||
                attachment.Content.Length == 0 || attachment.Content.Length > MaximumBytes - bytes)
                throw new ArgumentException("Attachments require safe file names and at most 8 MiB total per draft.");
            bytes += attachment.Content.Length;
        }
    }
}
