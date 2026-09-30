using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broiler.Mail.Infrastructure.Persistence;

/// <summary>Versioned configuration with same-directory replacement and a cross-process write lock.</summary>
internal sealed class JsonConfigurationFile<T>(string path, Func<T> createDefault, Action<T> validate, long maximumBytes = 1024 * 1024) where T : class
{
    private readonly string _path = Path.GetFullPath(path);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

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

    private sealed class Envelope
    {
        public required int SchemaVersion { get; init; }
        public required T Data { get; init; }
    }
}
