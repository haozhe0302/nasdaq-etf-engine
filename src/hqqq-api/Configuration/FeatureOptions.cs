namespace Hqqq.Api.Configuration;

public sealed class FeatureOptions
{
    public const string SectionName = "Feature";

    public bool EnableLiveMode { get; set; }
    public bool EnableMockFallback { get; set; }
}
