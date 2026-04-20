namespace Hqqq.ReferenceData.Standalone;

/// <summary>
/// Bound from <c>ReferenceData:Standalone</c>. Lets an operator
/// override the embedded basket seed with an external file (useful
/// when iterating on demo content without rebuilding the image)
/// without changing default behaviour.
/// </summary>
public sealed class BasketSeedOptions
{
    public const string SectionName = "ReferenceData:Standalone";

    /// <summary>
    /// Optional override path. When set and the file exists, the loader
    /// reads it instead of the embedded resource.
    /// </summary>
    public string? SeedPath { get; set; }

    /// <summary>
    /// Re-publish cadence in seconds for the standalone publisher. The
    /// active basket sits on a compacted topic, so a single publish on
    /// startup is correct semantics; a slow re-publish only exists to
    /// keep late consumers warm against operator-initiated topic resets.
    /// </summary>
    public int RepublishIntervalSeconds { get; set; } = 300;
}
