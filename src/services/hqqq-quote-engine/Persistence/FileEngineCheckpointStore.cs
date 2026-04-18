using System.Text.Json;
using Hqqq.Infrastructure.Serialization;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.State;

namespace Hqqq.QuoteEngine.Persistence;

/// <summary>
/// File-backed <see cref="IEngineCheckpointStore"/>. Writes via a temp file
/// + atomic rename so a partial write cannot poison the next restore.
/// Tolerant on load: missing or malformed files return <c>null</c> and
/// never throw out of <see cref="LoadAsync"/>.
/// </summary>
public sealed class FileEngineCheckpointStore : IEngineCheckpointStore
{
    private readonly string _path;
    private readonly ILogger<FileEngineCheckpointStore> _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public FileEngineCheckpointStore(
        QuoteEngineOptions options,
        ILogger<FileEngineCheckpointStore> logger)
        : this(options.CheckpointPath, logger)
    {
    }

    public FileEngineCheckpointStore(string path, ILogger<FileEngineCheckpointStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    public string Path => _path;

    public async ValueTask<EngineCheckpoint?> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            _logger.LogInformation("No engine checkpoint found at {Path}", _path);
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                _path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var checkpoint = await JsonSerializer
                .DeserializeAsync<EngineCheckpoint>(stream, HqqqJsonDefaults.Options, ct)
                .ConfigureAwait(false);

            if (checkpoint is null)
            {
                _logger.LogWarning("Engine checkpoint at {Path} deserialized to null", _path);
                return null;
            }

            if (checkpoint.Basket is null)
            {
                _logger.LogWarning(
                    "Engine checkpoint at {Path} is missing Basket payload — ignoring", _path);
                return null;
            }

            return checkpoint;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex,
                "Engine checkpoint at {Path} is unreadable — continuing without restore", _path);
            return null;
        }
    }

    public async ValueTask SaveAsync(EngineCheckpoint checkpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = _path + ".tmp";

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer
                    .SerializeAsync(stream, checkpoint, HqqqJsonDefaults.Options, ct)
                    .ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }

            // File.Move with overwrite is atomic on both Windows and POSIX for
            // same-volume paths, which is what we need to avoid a half-written
            // checkpoint ever being visible to a concurrent reader.
            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            _writeGate.Release();
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch (IOException) { /* best effort */ }
            }
        }
    }
}
