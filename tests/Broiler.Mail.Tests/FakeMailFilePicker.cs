// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Tests;

/// <summary>
/// A scripted <see cref="IMailFilePicker"/>: each pick returns the next queued result of its kind, or Canceled
/// when none is queued, and every request is recorded. Results complete at once, as a picker that showed
/// nothing would.
/// </summary>
internal sealed class FakeMailFilePicker : IMailFilePicker
{
    public MailFilePickerCapabilities Capabilities { get; set; } =
        MailFilePickerCapabilities.OpenFile | MailFilePickerCapabilities.SaveFile |
        MailFilePickerCapabilities.PickDirectory | MailFilePickerCapabilities.LocalPaths;

    public Queue<MailFilePick<IMailReadableFile>> OpenResults { get; } = new();
    public Queue<MailFilePick<IMailWritableFile>> SaveResults { get; } = new();
    public Queue<MailFilePick<IMailPickedDirectory>> DirectoryResults { get; } = new();

    /// <summary>Every request, in order, with which kind of pick it was.</summary>
    public List<(string Kind, MailFileRequest Request)> Requests { get; } = [];

    public Task<MailFilePick<IMailReadableFile>> PickOpenFileAsync(MailFileRequest request, CancellationToken token = default)
    {
        Requests.Add(("open", request));
        return Task.FromResult(token.IsCancellationRequested ? MailFilePick<IMailReadableFile>.Canceled
            : OpenResults.TryDequeue(out var result) ? result : MailFilePick<IMailReadableFile>.Canceled);
    }

    public Task<MailFilePick<IMailWritableFile>> PickSaveFileAsync(MailFileRequest request, CancellationToken token = default)
    {
        Requests.Add(("save", request));
        return Task.FromResult(token.IsCancellationRequested ? MailFilePick<IMailWritableFile>.Canceled
            : SaveResults.TryDequeue(out var result) ? result : MailFilePick<IMailWritableFile>.Canceled);
    }

    public Task<MailFilePick<IMailPickedDirectory>> PickDirectoryAsync(MailFileRequest request, CancellationToken token = default)
    {
        Requests.Add(("directory", request));
        return Task.FromResult(token.IsCancellationRequested ? MailFilePick<IMailPickedDirectory>.Canceled
            : DirectoryResults.TryDequeue(out var result) ? result : MailFilePick<IMailPickedDirectory>.Canceled);
    }

    /// <summary>Queues an open pick of the file at <paramref name="path"/>.</summary>
    public FakeMailFilePicker WillOpen(string path)
    {
        OpenResults.Enqueue(MailFilePick<IMailReadableFile>.Picked(new LocalMailFile(path)));
        return this;
    }

    /// <summary>Queues a save pick of a new file at <paramref name="path"/>.</summary>
    public FakeMailFilePicker WillSave(string path)
    {
        SaveResults.Enqueue(MailFilePick<IMailWritableFile>.Picked(new LocalMailWritableFile(path)));
        return this;
    }

    /// <summary>Queues a folder pick of <paramref name="path"/>.</summary>
    public FakeMailFilePicker WillChooseFolder(string path)
    {
        DirectoryResults.Enqueue(MailFilePick<IMailPickedDirectory>.Picked(new LocalMailDirectory(path)));
        return this;
    }
}
