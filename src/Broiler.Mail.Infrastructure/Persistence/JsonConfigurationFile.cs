// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   6
// Annotated:        6/6
// Exempt:           3
// Human-reviewed:   0/6
// IP risk:          Low
// Security risk:    High
// Criteria:         5/5
// Resource impact:  4/10 max
// Unverified:       6
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broiler.Mail.Infrastructure.Persistence;

/// <summary>Versioned configuration with same-directory replacement and a cross-process write lock.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0001; IP=Low; Security=High; Resources=4; Fingerprint=398D88
// Broiler-Falsified-If: two overlapping UpdateAsync calls, in one process or two, both apply their change to the same prior state so that one change is lost
// Broiler-Human:        PENDING
internal sealed class JsonConfigurationFile<T>(string path, Func<T> createDefault, Action<T> validate, long maximumBytes = 1024 * 1024) where T : class
{
    private readonly string _path = Path.GetFullPath(path);
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=1; Fingerprint=4C6116
    // Broiler-Falsified-If: a configuration file with an unknown property, or an integer where an enum name belongs, deserializes without a JsonException
    // Broiler-Human:        PENDING
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    // Broiler-AI:           Origin=AI; Spec=ADR-0001; IP=Low; Security=High; Resources=4; Fingerprint=7A3C57
    // Broiler-Falsified-If: an existing file that is corrupt, larger than maximumBytes or of another schema version yields the default value instead of an exception
    // Broiler-Human:        PENDING
    public async Task<T> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (stream.Length > maximumBytes)
                throw new InvalidDataException($"Configuration exceeds the {maximumBytes} byte limit.");
            var envelope = await JsonSerializer.DeserializeAsync<Envelope>(stream, Options, cancellationToken).ConfigureAwait(false);
            if (envelope is null || envelope.SchemaVersion != 1 || envelope.Data is null)
                throw new InvalidDataException("Unsupported or incomplete configuration. The file has not been changed.");
            validate(envelope.Data);
            return envelope.Data;
        }
        catch (FileNotFoundException) { return createDefault(); }
        catch (DirectoryNotFoundException) { return createDefault(); }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new InvalidDataException("Configuration is invalid. Restore or repair the file before saving.", error);
        }
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0001; IP=Low; Security=High; Resources=4; Fingerprint=421205
    // Broiler-Falsified-If: a failure or cancellation while writing leaves the target file truncated or partly written instead of holding its previous content
    // Broiler-Human:        PENDING
    public async Task UpdateAsync(Func<T, T> update, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var lease = await AcquireWriteLockAsync(cancellationToken).ConfigureAwait(false);
        // Read and validate before writing, so corrupt or future-version files are never reset silently.
        var data = update(await ReadAsync(cancellationToken).ConfigureAwait(false));
        validate(data);
        string temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, new Envelope { SchemaVersion = 1, Data = data }, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
                if (stream.Length > maximumBytes)
                    throw new InvalidDataException($"Configuration exceeds the {maximumBytes} byte limit.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { /* Preserve the original operation's outcome. */ }
            catch (UnauthorizedAccessException) { /* A stale temporary file may require cleanup. */ }
        }
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=E7E027
    // Broiler-Falsified-If: a lock file held by another writer for more than 3 seconds lets the update proceed without the lock instead of throwing
    // Broiler-Human:        PENDING
    private async Task<FileStream> AcquireWriteLockAsync(CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(3))
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=78B419
    // Broiler-Human:        PENDING
    private sealed class Envelope
    {
        public required int SchemaVersion { get; init; }
        public required T Data { get; init; }
    }
}
