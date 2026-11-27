// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   5
// Annotated:        0/5
// Exempt:           2
// Human-reviewed:   0/5
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       5
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// A picker for a host that cannot choose files yet: every pick returns
/// <see cref="MailFilePickStatus.Unavailable"/> with the same reason, at once, and nothing is shown.
/// Views that receive it keep their typed path fields so the host loses no function.
/// </summary>
public sealed class UnavailableMailFilePicker(string reason) : IMailFilePicker
{
    /// <summary>The picker of a device whose file picker has not been built yet.</summary>
    public static UnavailableMailFilePicker NotOnThisDeviceYet { get; } = new("Choosing files is not available on this device yet.");

    /// <summary>Why nothing can be chosen.</summary>
    public string Reason { get; } = string.IsNullOrWhiteSpace(reason) ? throw new ArgumentException("A reason is required.", nameof(reason)) : reason;

    public MailFilePickerCapabilities Capabilities => MailFilePickerCapabilities.None;

    public Task<MailFilePick<IMailReadableFile>> PickOpenFileAsync(MailFileRequest request, CancellationToken token = default) =>
        Task.FromResult(MailFilePick<IMailReadableFile>.Unavailable(Reason));

    public Task<MailFilePick<IMailWritableFile>> PickSaveFileAsync(MailFileRequest request, CancellationToken token = default) =>
        Task.FromResult(MailFilePick<IMailWritableFile>.Unavailable(Reason));

    public Task<MailFilePick<IMailPickedDirectory>> PickDirectoryAsync(MailFileRequest request, CancellationToken token = default) =>
        Task.FromResult(MailFilePick<IMailPickedDirectory>.Unavailable(Reason));
}
