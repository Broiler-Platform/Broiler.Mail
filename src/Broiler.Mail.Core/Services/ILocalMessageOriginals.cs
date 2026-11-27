// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   22
// Annotated:        0/22
// Exempt:           7
// Human-reviewed:   0/22
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       22
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>Local file identities are independent of IMAP UIDs. No operation contacts a server.</summary>
public interface ILocalMessageOriginals
{
    Task<LocalOriginalContent> OpenFileAsync(string path, CancellationToken token = default);
    Task<LocalOriginalReceipt> ImportAsync(AccountId account, LocalOriginalContent content, CancellationToken token = default);
    Task<IReadOnlyList<LocalOriginalReceipt>> ListImportsAsync(AccountId account, CancellationToken token = default);
    Task<LocalOriginalContent> ReadImportAsync(LocalOriginalReceipt receipt, CancellationToken token = default);
}
public sealed record LocalOriginalContent(string FileName, byte[] Bytes);

/// <summary>Size limits of local .eml files: opening and viewing reach the streamed-export limit, keeping a copy in the profile does not.</summary>
public static class LocalOriginalLimits
{
    /// <summary>The largest local .eml file that can be opened, decoded and shown (12 MiB).</summary>
    public const int OpenBytes = IStreamingMessageSourceExports.MaximumBytes;
    /// <summary>The largest file whose copy can be kept in the profile (2 MiB).</summary>
    public const int ImportBytes = MailMessageSource.MaximumBytes;
    /// <summary>How many copies one profile keeps.</summary>
    public const int ImportFiles = 16;
    /// <summary>The combined size of all kept copies (4 MiB).</summary>
    public const int ImportTotalBytes = 4 * 1024 * 1024;
}

/// <summary>Optional: opens a local original from a picked file's stream, for files without a path on this device.</summary>
public interface ILocalOriginalStreamSource
{
    /// <summary>Reads at most <see cref="LocalOriginalLimits.OpenBytes"/> bytes without trusting the stream's length.
    /// The extension is not checked, because document providers may show names without one.</summary>
    Task<LocalOriginalContent> OpenStreamAsync(string displayName, Stream source, CancellationToken token = default);
}
public sealed record LocalOriginalReceipt(Guid Id, AccountId AccountId, string FileName, int Bytes, string Sha256, DateTimeOffset ImportedAt);

/// <summary>Optional local MIME reading and explicit file management, independent of server message identities.</summary>
public interface ILocalOriginalManagement
{
    bool CanRemove { get; }
    Task<LocalOriginalPreview> DecodeAsync(LocalOriginalContent content, CancellationToken token = default);
    Task<LocalOriginalExport> ExportAsync(LocalOriginalReceipt receipt, string destination, CancellationToken token = default);
    Task<LocalOriginalRemoval> RemoveAsync(LocalOriginalReceipt receipt, CancellationToken token = default);
}
public sealed record LocalOriginalPreview(string Subject, string Sender, string PlainText, bool IsHtmlFallback, bool IsTruncated, bool HasIncompleteParts)
{
    public const int MaximumHtmlCharacters = 128_000;
    public string? HtmlText { get; init; }
    public bool HtmlExceedsLimit { get; init; }
    /// <summary>The first 20 To addresses, joined; null when there are none.</summary>
    public string? To { get; init; }
    /// <summary>The message's Date header, when it could be read.</summary>
    public DateTimeOffset? Date { get; init; }
    /// <summary>Regular attachment parts of the local original, at most 32; inline cid:/Content-Location images are not listed.</summary>
    public IReadOnlyList<Broiler.Mail.Core.Messages.MailAttachmentDescriptor> Attachments { get; init; } = [];
    public bool IsAttachmentListIncomplete { get; init; }
}
public sealed record LocalOriginalExport(string Path, int Bytes, string Sha256);
public sealed record LocalOriginalRemoval(LocalOriginalReceipt Removed, string BackupDirectory);
