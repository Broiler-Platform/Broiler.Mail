// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   13
// Annotated:        0/13
// Exempt:           14
// Human-reviewed:   0/13
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       13
//
// GENERATED - DO NOT EDIT MANUALLY

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Mail.Core.Diagnostics;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Media;
using Broiler.Media.Image;
using Broiler.Media.Image.Managed;

namespace Broiler.Mail.Windows.Preview;

/// <summary>
/// Defines structured high-level outcomes for resource loading and rendering.
/// HTML preview owns resource requests and rendering outcomes, while Mail
/// owns permissions, user explanations, and UI presentation.
/// </summary>
public enum HtmlResourceOutcomeKind
{
    Rendered,
    Partial,
    Blocked,
    Failed,
    BudgetExceeded
}

/// <summary>
/// Fine-grained failure or rejection reasons for individual resource requests.
/// </summary>
public enum HtmlResourceFailureReason
{
    None,
    PolicyBlocked,
    NetworkError,
    InvalidContentType,
    ByteBudgetExceeded,
    DimensionBudgetExceeded,
    PixelBudgetExceeded,
    CorruptedHeader,
    DecompressionBombDetected
}

/// <summary>
/// Represents the structured outcome of inspecting or loading an individual resource.
/// </summary>
public sealed record HtmlResourceResult(
    string Url,
    HtmlResourceOutcomeKind Outcome,
    HtmlResourceFailureReason Reason,
    long BytesLoaded = 0,
    ImageInfo? ImageInfo = null,
    string? ErrorDetail = null)
{
    public bool IsSuccess => Outcome == HtmlResourceOutcomeKind.Rendered;
}

/// <summary>
/// Aggregated structured outcome across all resources requested by a preview document.
/// </summary>
public sealed record HtmlResourceBatchResult(
    HtmlResourceOutcomeKind OverallOutcome,
    int TotalRequested,
    int TotalSucceeded,
    int TotalFailed,
    int TotalBudgetExceeded,
    int TotalBlocked,
    long TotalDownloadedBytes,
    IReadOnlyList<HtmlResourceResult> Results)
{
    public static HtmlResourceBatchResult Create(IReadOnlyList<HtmlResourceResult> results, long totalDownloadedBytes)
    {
        int requested = results.Count;
        int succeeded = results.Count(r => r.Outcome == HtmlResourceOutcomeKind.Rendered);
        int failed = results.Count(r => r.Outcome == HtmlResourceOutcomeKind.Failed);
        int budgetExceeded = results.Count(r => r.Outcome == HtmlResourceOutcomeKind.BudgetExceeded);
        int blocked = results.Count(r => r.Outcome == HtmlResourceOutcomeKind.Blocked);

        HtmlResourceOutcomeKind overall;
        if (succeeded > 0 && failed == 0 && budgetExceeded == 0 && blocked == 0)
        {
            overall = HtmlResourceOutcomeKind.Rendered;
        }
        else if (succeeded > 0)
        {
            overall = HtmlResourceOutcomeKind.Partial;
        }
        else if (budgetExceeded > 0 && succeeded == 0)
        {
            overall = HtmlResourceOutcomeKind.BudgetExceeded;
        }
        else if (blocked > 0 && succeeded == 0)
        {
            overall = HtmlResourceOutcomeKind.Blocked;
        }
        else
        {
            overall = HtmlResourceOutcomeKind.Failed;
        }

        return new HtmlResourceBatchResult(
            overall,
            requested,
            succeeded,
            failed,
            budgetExceeded,
            blocked,
            totalDownloadedBytes,
            results);
    }
}

/// <summary>
/// Manages bounded streaming and header inspection for preview resources, reusing
/// <see cref="MediaLimits"/> and managed codecs from Broiler.Media.
/// </summary>
public static class HtmlResourceLoader
{
    /// <summary>
    /// Conservative media limits tailored for email preview:
    /// 5 MB max per image, 64 MB max decoded bitmap, 16 MP max area, 4096 px max dimension, 100 max frames.
    /// </summary>
    public static readonly MediaLimits DefaultMailLimits = new(
        maxEncodedBytes: 5_000_000,
        maxDecodedBytes: 64L * 1024 * 1024,
        maxImagePixels: 16_000_000L,
        maxImageDimension: 4096,
        maxFrames: 100);

    /// <summary>
    /// Total aggregate budget across all remote images in a single email (20 MB).
    /// </summary>
    public const long DefaultMaxTotalBytes = 20_000_000;

    private static readonly IReadOnlyList<ImageCodec> ManagedCodecs = ManagedImageCodecs.CreateCodecs();

    /// <summary>
    /// Inspects an encoded image buffer using Broiler.Media image codecs without decoding pixels.
    /// Rejects decompression bombs, oversized dimensions/areas, and malformed headers.
    /// </summary>
    public static (bool IsValid, ImageInfo? Info, HtmlResourceFailureReason Reason, string? Detail) InspectImageBytes(
        ReadOnlySpan<byte> data,
        MediaLimits? limits = null)
    {
        limits ??= DefaultMailLimits;

        if (data.Length == 0)
        {
            return (false, null, HtmlResourceFailureReason.CorruptedHeader, "Image payload is empty.");
        }

        if (data.Length > limits.MaxEncodedBytes)
        {
            return (false, null, HtmlResourceFailureReason.ByteBudgetExceeded,
                $"Image payload ({data.Length:N0} bytes) exceeds limit ({limits.MaxEncodedBytes:N0} bytes).");
        }

        ImageInfo? inspectedInfo = null;
        bool inspected = false;
        foreach (var codec in ManagedCodecs)
        {
            if (codec.TryInspect(data, out inspectedInfo) && inspectedInfo is not null)
            {
                inspected = true;
                break;
            }
        }

        if (!inspected || inspectedInfo is null)
        {
            return (false, null, HtmlResourceFailureReason.CorruptedHeader,
                "Image header could not be verified by any supported codec (malformed or unrecognized image format).");
        }

        if (inspectedInfo.Width > limits.MaxImageDimension || inspectedInfo.Height > limits.MaxImageDimension)
        {
            return (false, inspectedInfo, HtmlResourceFailureReason.DimensionBudgetExceeded,
                $"Image dimensions ({inspectedInfo.Width}x{inspectedInfo.Height}) exceed maximum allowed dimension ({limits.MaxImageDimension}px).");
        }

        if (inspectedInfo.PixelCount > limits.MaxImagePixels)
        {
            return (false, inspectedInfo, HtmlResourceFailureReason.PixelBudgetExceeded,
                $"Image pixel count ({inspectedInfo.PixelCount:N0}) exceeds maximum allowed pixel budget ({limits.MaxImagePixels:N0} pixels).");
        }

        return (true, inspectedInfo, HtmlResourceFailureReason.None, null);
    }

    /// <summary>
    /// Bounded streaming fetch of a single remote image with early byte cap enforcement and header inspection.
    /// </summary>
    public static async Task<(HtmlResourceResult Result, byte[]? Data)> FetchAndValidateImageAsync(
        HttpClient client,
        string url,
        MediaLimits limits,
        long currentTotalBytes,
        long maxTotalBytes,
        CancellationToken cancellationToken)
    {
        if (!HtmlPreviewPolicy.TryExternalLink(url, out var uri) || uri is null)
        {
            return (new HtmlResourceResult(url, HtmlResourceOutcomeKind.Blocked, HtmlResourceFailureReason.PolicyBlocked,
                ErrorDetail: "URL does not comply with mail isolation external link policy."), null);
        }

        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (new HtmlResourceResult(url, HtmlResourceOutcomeKind.Failed, HtmlResourceFailureReason.NetworkError,
                    ErrorDetail: $"HTTP request failed with status {(int)response.StatusCode} {response.ReasonPhrase}."), null);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Contains("svg", StringComparison.OrdinalIgnoreCase))
            {
                return (new HtmlResourceResult(url, HtmlResourceOutcomeKind.Failed, HtmlResourceFailureReason.InvalidContentType,
                    ErrorDetail: $"Unsupported or unsafe content type '{mediaType}'."), null);
            }

            if (response.Content.Headers.ContentLength is { } declaredLength)
            {
                if (declaredLength > limits.MaxEncodedBytes)
                {
                    return (new HtmlResourceResult(url, HtmlResourceOutcomeKind.BudgetExceeded, HtmlResourceFailureReason.ByteBudgetExceeded,
                        ErrorDetail: $"Declared content length ({declaredLength:N0} bytes) exceeds per-image limit ({limits.MaxEncodedBytes:N0} bytes)."), null);
                }
                if (currentTotalBytes + declaredLength > maxTotalBytes)
                {
                    return (new HtmlResourceResult(url, HtmlResourceOutcomeKind.BudgetExceeded, HtmlResourceFailureReason.ByteBudgetExceeded,
                        ErrorDetail: $"Declared content length ({declaredLength:N0} bytes) exceeds remaining message budget ({maxTotalBytes - currentTotalBytes:N0} bytes)."), null);
                }
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + bytesRead > limits.MaxEncodedBytes)
                {
                    return (new HtmlResourceResult(url, HtmlResourceOutcomeKind.BudgetExceeded, HtmlResourceFailureReason.ByteBudgetExceeded,
                        BytesLoaded: buffer.Length,
                        ErrorDetail: $"Image stream exceeded per-image limit of {limits.MaxEncodedBytes:N0} bytes during download."), null);
                }
                if (currentTotalBytes + buffer.Length + bytesRead > maxTotalBytes)
                {
                    return (new HtmlResourceResult(url, HtmlResourceOutcomeKind.BudgetExceeded, HtmlResourceFailureReason.ByteBudgetExceeded,
                        BytesLoaded: buffer.Length,
                        ErrorDetail: $"Image stream exceeded total message budget of {maxTotalBytes:N0} bytes during download."), null);
                }
                buffer.Write(chunk, 0, bytesRead);
            }

            if (buffer.Length == 0)
            {
                return (new HtmlResourceResult(url, HtmlResourceOutcomeKind.Failed, HtmlResourceFailureReason.CorruptedHeader,
                    ErrorDetail: "Server returned zero bytes."), null);
            }

            byte[] imageBytes = buffer.ToArray();
            var inspection = InspectImageBytes(imageBytes, limits);
            if (!inspection.IsValid)
            {
                var outcome = inspection.Reason is HtmlResourceFailureReason.ByteBudgetExceeded
                    or HtmlResourceFailureReason.DimensionBudgetExceeded
                    or HtmlResourceFailureReason.PixelBudgetExceeded
                    or HtmlResourceFailureReason.DecompressionBombDetected
                    ? HtmlResourceOutcomeKind.BudgetExceeded
                    : HtmlResourceOutcomeKind.Failed;

                MailLogger.Warning("HtmlResourceLoader", $"Image inspection failed for {url}: {inspection.Reason} ({inspection.Detail})");
                return (new HtmlResourceResult(url, outcome, inspection.Reason,
                    BytesLoaded: imageBytes.Length,
                    ImageInfo: inspection.Info,
                    ErrorDetail: inspection.Detail), null);
            }

            MailLogger.Info("HtmlResourceLoader", $"Image verified for {url}: {imageBytes.Length:N0} bytes, {inspection.Info?.Width}x{inspection.Info?.Height} {inspection.Info?.MediaType}.");
            return (new HtmlResourceResult(url, HtmlResourceOutcomeKind.Rendered, HtmlResourceFailureReason.None,
                BytesLoaded: imageBytes.Length,
                ImageInfo: inspection.Info), imageBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            MailLogger.Warning("HtmlResourceLoader", $"Network or processing error for {url}: {ex.Message}");
            return (new HtmlResourceResult(url, HtmlResourceOutcomeKind.Failed, HtmlResourceFailureReason.NetworkError,
                ErrorDetail: ex.Message), null);
        }
    }

    /// <summary>
    /// Processes a collection of remote resource URLs, enforcing aggregate and individual limits,
    /// inspecting image headers, and returning structured batch outcomes.
    /// </summary>
    public static async Task<(HtmlResourceBatchResult BatchResult, Dictionary<string, MailEmbeddedImage> DownloadedImages)> LoadBatchAsync(
        HttpClient client,
        IReadOnlyCollection<string> urls,
        IReadOnlyDictionary<string, MailEmbeddedImage>? initialImages,
        MediaLimits? limits = null,
        long maxTotalBytes = DefaultMaxTotalBytes,
        CancellationToken cancellationToken = default)
    {
        limits ??= DefaultMailLimits;
        var downloadedImages = new Dictionary<string, MailEmbeddedImage>(StringComparer.OrdinalIgnoreCase);
        if (initialImages is not null)
        {
            foreach (var kvp in initialImages) downloadedImages[kvp.Key] = kvp.Value;
        }

        var results = new List<HtmlResourceResult>();
        long totalDownloadedBytes = 0;

        foreach (string url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (downloadedImages.ContainsKey(url))
            {
                results.Add(new HtmlResourceResult(url, HtmlResourceOutcomeKind.Rendered, HtmlResourceFailureReason.None));
                continue;
            }

            var (res, data) = await FetchAndValidateImageAsync(
                client, url, limits, totalDownloadedBytes, maxTotalBytes, cancellationToken).ConfigureAwait(false);

            results.Add(res);

            if (res.Outcome == HtmlResourceOutcomeKind.Rendered && data is not null)
            {
                totalDownloadedBytes += res.BytesLoaded;
                string mediaType = res.ImageInfo?.MediaType ?? "image/jpeg";
                downloadedImages[url] = new MailEmbeddedImage(url, mediaType, data);
            }
        }

        var batchResult = HtmlResourceBatchResult.Create(results, totalDownloadedBytes);
        return (batchResult, downloadedImages);
    }
}
