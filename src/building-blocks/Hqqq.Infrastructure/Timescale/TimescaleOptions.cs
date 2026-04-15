namespace Hqqq.Infrastructure.Timescale;

/// <summary>
/// TimescaleDB connection settings, bound to the "Timescale" configuration section.
/// </summary>
public sealed class TimescaleOptions
{
    public string ConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=hqqq;Username=admin;Password=changeme";
}
