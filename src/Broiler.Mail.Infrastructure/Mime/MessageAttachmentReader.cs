// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   16
// Annotated:        0/16
// Exempt:           6
// Human-reviewed:   0/16
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       16
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Infrastructure.Mime;

/// <summary>
/// Obtains one message's original for its attachments: first the newest verified saved original (no network), then one
/// streamed read-only download bounded at <see cref="IStreamingMessageSourceExports.MaximumBytes"/>, and last the buffered
/// <see cref="IMailSourceReceiver"/>. The parsed parts live in memory only; no directory or quota is touched.
/// </summary>
public sealed class MessageAttachmentReader(IStreamingMailSourceReceiver? streaming, IMailSourceReceiver? buffered,
    IMessageSourceExportCatalog? catalog = null, IVerifiedMessageSourceExports? verified = null) : IMessageAttachmentReader
{
    public const int SliceBytes = 81_920;
    private readonly IVerifiedMessageSourceExports? _verified = verified ?? catalog as IVerifiedMessageSourceExports;

    public async Task<IMailAttachmentSource> OpenAsync(AccountProfile account, MailMessageKey key, Action<int, int>? progress = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (key.AccountId != account.Id || key.Uid == 0 || key.UidValidity == 0 || string.IsNullOrWhiteSpace(key.MailboxId))
            throw new ArgumentException("The message does not belong to this account.", nameof(key));
        token.ThrowIfCancellationRequested();
        var (source, saved) = await ReadSourceAsync(account, key, progress, token).ConfigureAwait(false);
        var parts = await Task.Run(() => MimeAttachmentCatalog.Parse(source), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return new Source(key, saved, parts);
    }

    private async Task<(MailMessageSource Source, bool Saved)> ReadSourceAsync(AccountProfile account, MailMessageKey key, Action<int, int>? progress, CancellationToken token)
    {
        if (catalog is not null && _verified is not null)
        {
            IReadOnlyList<SavedMessageSourceExport> receipts;
            try { receipts = await catalog.ListSavedExportsAsync(key, token).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { receipts = []; }
            foreach (var receipt in receipts.OrderByDescending(row => row.SavedAt))
            {
                if (receipt.Export.Key != key) continue;
                try
                {
                    var original = await _verified.ReadVerifiedAsync(receipt.Export, token).ConfigureAwait(false);
                    if (original.Key == key && original.Bytes is { Length: > 0 and <= IStreamingMessageSourceExports.MaximumBytes }) return (original, true);
                }
                // A changed, missing or unreadable receipt falls through to the next one and then to the server.
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { }
            }
        }
        token.ThrowIfCancellationRequested();
        if (streaming is not null)
        {
            using var buffer = new BoundedMemoryStream(IStreamingMessageSourceExports.MaximumBytes);
            int length = await streaming.CopySourceAsync(account, key, buffer, IStreamingMessageSourceExports.MaximumBytes, progress, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (length < 1 || length != buffer.Length) throw new InvalidDataException("The original message was incomplete; no attachment was read.");
            return (new MailMessageSource(key, buffer.ToArray()), false);
        }
        if (buffered is not null)
        {
            var original = await buffered.GetSourceAsync(account, key, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (original.Key != key) throw new InvalidDataException("The original source returned another message identity.");
            if (original.Bytes is not { Length: > 0 and <= IStreamingMessageSourceExports.MaximumBytes }) throw new InvalidDataException("The original message is empty or too large.");
            return (original, false);
        }
        throw new InvalidOperationException("Attachments need the original message, and this account cannot download it.");
    }

    private sealed class Source(MailMessageKey key, bool saved, (ReceivedAttachment Info, byte[] Data)[] parts) : IMailAttachmentSource
    {
        private (ReceivedAttachment Info, byte[] Data)[]? _parts = parts;
        public MailMessageKey Key { get; } = key;
        public bool FromSavedOriginal { get; } = saved;
        public IReadOnlyList<ReceivedAttachment> Attachments { get; } = Array.AsReadOnly(parts.Select(row => row.Info).ToArray());

        public async Task CopyToAsync(ReceivedAttachment attachment, Stream destination, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(destination);
            byte[] data = Find(attachment);
            for (int offset = 0; offset < data.Length; offset += SliceBytes)
            {
                token.ThrowIfCancellationRequested();
                await destination.WriteAsync(data.AsMemory(offset, Math.Min(SliceBytes, data.Length - offset)), token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
        }

        public ReadOnlyMemory<byte> Read(ReceivedAttachment attachment) => Find(attachment);

        private byte[] Find(ReceivedAttachment attachment)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            var rows = _parts ?? throw new ObjectDisposedException(nameof(IMailAttachmentSource));
            if (attachment.Key != Key) throw new ArgumentException("The attachment belongs to another message.", nameof(attachment));
            foreach (var row in rows)
                if (row.Info == attachment) return row.Data;
            throw new InvalidDataException("The attachment list changed. Open the message's attachments again.");
        }

        public void Dispose() => _parts = null;
    }

    /// <summary>A memory buffer that refuses to grow beyond its cap, so a server cannot make the download unbounded.</summary>
    private sealed class BoundedMemoryStream(int maximum) : MemoryStream
    {
        private void Reserve(int count)
        {
            if (count < 0 || Position + count > maximum) throw new IOException("The original message exceeds the 12 MiB attachment limit.");
        }
        public override void SetLength(long value)
        {
            if (value > maximum) throw new IOException("The original message exceeds the 12 MiB attachment limit.");
            base.SetLength(value);
        }
        public override void Write(byte[] buffer, int offset, int count) { Reserve(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Reserve(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Reserve(1); base.WriteByte(value); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        { Reserve(count); return base.WriteAsync(buffer, offset, count, cancellationToken); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { Reserve(buffer.Length); return base.WriteAsync(buffer, cancellationToken); }
    }
}
