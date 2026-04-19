namespace Hqqq.Observability.Hosting;

/// <summary>
/// Bound from the <c>Management:</c> configuration section. Controls the
/// embedded HTTP listener that exposes <c>/healthz/*</c> and <c>/metrics</c>
/// in worker services that have no primary HTTP surface.
/// </summary>
public sealed class ManagementOptions
{
    public const string SectionName = "Management";

    /// <summary>
    /// When false the management host is not started. Defaults to true so
    /// workers expose health/metrics out of the box; tests use false to
    /// keep the worker silent on the network.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// TCP port to bind. Set to 0 to ask Kestrel to pick an available port
    /// (used by tests to avoid port collisions).
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Address to bind. Defaults to localhost so workers don't accidentally
    /// expose unauthenticated management surfaces on a public interface.
    /// Override to <c>0.0.0.0</c> in containers where Prometheus must scrape
    /// across the network.
    /// </summary>
    public string BindAddress { get; set; } = "127.0.0.1";
}
