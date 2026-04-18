using System.Text.Json;
using Hqqq.Infrastructure.Serialization;

namespace Hqqq.Analytics.Reports;

/// <summary>
/// Writes a <see cref="ReportSummary"/> to disk as pretty-printed JSON using
/// the shared <see cref="HqqqJsonDefaults.Options"/> (camelCase, enum-as-string,
/// null-suppression). Indented on write to keep the artifact diff-friendly in
/// git or artifact stores.
/// </summary>
public sealed class JsonReportEmitter
{
    private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

    public async Task EmitAsync(ReportSummary summary, string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, summary, WriteOptions, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static JsonSerializerOptions CreateWriteOptions()
    {
        var options = new JsonSerializerOptions(HqqqJsonDefaults.Options)
        {
            WriteIndented = true,
        };
        return options;
    }
}
