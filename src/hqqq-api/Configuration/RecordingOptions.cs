namespace Hqqq.Api.Configuration;

public sealed class RecordingOptions
{
    public const string SectionName = "Recording";

    public bool Enabled { get; set; }
    public string Directory { get; set; } = "data/recordings";
}
