using Hqqq.Bench;

var input = GetArg(args, "--input");
var output = GetArg(args, "--output") ?? ".";
var from = ParseTime(GetArg(args, "--from"));
var to = ParseTime(GetArg(args, "--to"));

if (input is null)
{
    Console.Error.WriteLine("""
    HQQQ Benchmark — offline replay and report generator

    Usage:
      dotnet run --project src/hqqq-bench -- --input <path> [options]

    Options:
      --input <path>     Path to a .jsonl file or a directory of recordings (required)
      --output <dir>     Output directory for reports (default: current directory)
      --from <iso-time>  Include events from this UTC timestamp onward
      --to <iso-time>    Include events up to this UTC timestamp

    Examples:
      dotnet run --project src/hqqq-bench -- --input data/recordings/2026-04-04
      dotnet run --project src/hqqq-bench -- --input data/recordings/2026-04-04/session-143000.jsonl --output reports
    """);
    return 1;
}

try
{
    Console.WriteLine($"Loading events from: {input}");
    var events = ReplayEngine.LoadEvents(input, from, to);
    Console.WriteLine($"Loaded {events.Count} events");

    if (events.Count == 0)
    {
        Console.Error.WriteLine("No events found in the specified input. Nothing to report.");
        return 1;
    }

    var report = ReplayEngine.Aggregate(events);

    Directory.CreateDirectory(output);
    var jsonPath = Path.Combine(output, "benchmark-report.json");
    var mdPath = Path.Combine(output, "benchmark-report.md");

    File.WriteAllText(jsonPath, report.ToJson());
    File.WriteAllText(mdPath, report.ToMarkdown());

    Console.WriteLine();
    Console.Write(report.ToMarkdown());
    Console.WriteLine($"Reports written to:");
    Console.WriteLine($"  JSON: {Path.GetFullPath(jsonPath)}");
    Console.WriteLine($"  Markdown: {Path.GetFullPath(mdPath)}");

    return 0;
}
catch (FileNotFoundException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

// ── Arg helpers ──────────────────────────────────────

static string? GetArg(string[] args, string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

static DateTimeOffset? ParseTime(string? value) =>
    value is not null && DateTimeOffset.TryParse(value, out var dto) ? dto : null;
