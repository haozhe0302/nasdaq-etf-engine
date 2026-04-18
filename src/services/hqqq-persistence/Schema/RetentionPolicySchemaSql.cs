using System.Globalization;
using Hqqq.Persistence.Options;

namespace Hqqq.Persistence.Schema;

/// <summary>
/// Builds idempotent <c>add_retention_policy</c> statements from the
/// persistence options so each hypertable / continuous aggregate gets a
/// locally-configured retention window. TimescaleDB's
/// <c>add_retention_policy(..., if_not_exists => TRUE)</c> makes repeated
/// startup safe, so no DO-EXCEPTION wrapper is needed here.
/// </summary>
/// <remarks>
/// Pure SQL building — no database access — so tests can assert exact
/// output without a live DB.
/// </remarks>
public static class RetentionPolicySchemaSql
{
    /// <summary>
    /// Describes one retention target: the hypertable or continuous
    /// aggregate name plus its retention window.
    /// </summary>
    public sealed record RetentionTarget(string Relation, TimeSpan Window);

    /// <summary>
    /// Enumerates the full retention target list built from the supplied
    /// options. Ordered: raw ticks first (shortest), then quote snapshots,
    /// then each rollup view.
    /// </summary>
    public static IReadOnlyList<RetentionTarget> BuildTargets(PersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var targets = new List<RetentionTarget>
        {
            new("raw_ticks", options.RawTickRetention),
            new("quote_snapshots", options.QuoteSnapshotRetention),
        };

        foreach (var view in QuoteSnapshotRollupSchemaSql.RollupViews)
        {
            targets.Add(new RetentionTarget(view, options.RollupRetention));
        }

        return targets;
    }

    /// <summary>
    /// Builds the ordered list of idempotent retention-policy SQL
    /// statements. One statement per target. Windows are formatted via
    /// <see cref="FormatInterval(TimeSpan)"/> into PostgreSQL
    /// <c>INTERVAL</c> literals.
    /// </summary>
    public static IReadOnlyList<string> BuildStatements(PersistenceOptions options)
    {
        var targets = BuildTargets(options);
        var statements = new List<string>(targets.Count);

        foreach (var target in targets)
        {
            statements.Add(BuildStatement(target));
        }

        return statements;
    }

    /// <summary>
    /// Builds a single <c>add_retention_policy</c> statement for one
    /// target. Public so tests can exercise it in isolation.
    /// </summary>
    public static string BuildStatement(RetentionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(target.Relation))
            throw new ArgumentException("target must have a non-empty Relation", nameof(target));
        if (target.Window <= TimeSpan.Zero)
            throw new ArgumentException(
                $"retention window for '{target.Relation}' must be positive (was {target.Window})",
                nameof(target));

        var interval = FormatInterval(target.Window);
        return $"SELECT add_retention_policy('{target.Relation}', INTERVAL '{interval}', if_not_exists => TRUE);";
    }

    /// <summary>
    /// Formats a <see cref="TimeSpan"/> as a PostgreSQL
    /// <c>INTERVAL</c>-compatible string. Whole-day windows render as
    /// <c>N days</c>; shorter windows fall back to <c>N hours</c>,
    /// <c>N minutes</c>, or <c>N seconds</c> so sub-day retention is
    /// expressible without rounding down to zero.
    /// </summary>
    public static string FormatInterval(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window), window, "interval must be positive");

        if (window.Ticks % TimeSpan.TicksPerDay == 0)
        {
            var days = (long)window.TotalDays;
            return $"{days.ToString(CultureInfo.InvariantCulture)} days";
        }

        if (window.Ticks % TimeSpan.TicksPerHour == 0)
        {
            var hours = (long)window.TotalHours;
            return $"{hours.ToString(CultureInfo.InvariantCulture)} hours";
        }

        if (window.Ticks % TimeSpan.TicksPerMinute == 0)
        {
            var minutes = (long)window.TotalMinutes;
            return $"{minutes.ToString(CultureInfo.InvariantCulture)} minutes";
        }

        var seconds = (long)Math.Round(window.TotalSeconds);
        if (seconds <= 0) seconds = 1;
        return $"{seconds.ToString(CultureInfo.InvariantCulture)} seconds";
    }
}
