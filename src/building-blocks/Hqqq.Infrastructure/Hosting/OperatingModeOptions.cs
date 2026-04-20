namespace Hqqq.Infrastructure.Hosting;

/// <summary>
/// Bound from the top-level <c>OperatingMode</c> config key, with a flat
/// <c>HQQQ_OPERATING_MODE</c> environment variable as legacy fallback (mapped
/// by <see cref="LegacyConfigShim"/>). Default is <see cref="OperatingMode.Hybrid"/>
/// so an unset env preserves the historic Phase 2 demo behaviour exactly.
/// </summary>
public sealed class OperatingModeOptions
{
    /// <summary>
    /// Configuration key the section binds to.
    /// </summary>
    public const string SectionName = "OperatingMode";

    /// <summary>
    /// The resolved operating mode for this process.
    /// </summary>
    public OperatingMode Mode { get; set; } = OperatingMode.Hybrid;

    /// <summary>True when <see cref="Mode"/> is <see cref="OperatingMode.Standalone"/>.</summary>
    public bool IsStandalone => Mode == OperatingMode.Standalone;

    /// <summary>True when <see cref="Mode"/> is <see cref="OperatingMode.Hybrid"/>.</summary>
    public bool IsHybrid => Mode == OperatingMode.Hybrid;
}
