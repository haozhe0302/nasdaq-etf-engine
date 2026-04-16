using System.Diagnostics;

namespace Hqqq.Observability.Tracing;

/// <summary>
/// Shared ActivitySource and tracing helpers for Phase 2 services.
/// Full distributed tracing setup (e.g., OTLP export) will be added in later phases.
/// </summary>
public static class TracingExtensions
{
    public static readonly ActivitySource ActivitySource = new("Hqqq", "1.0.0");

    /// <summary>
    /// Starts a new activity for the given operation. Returns null if no listener is attached.
    /// </summary>
    public static Activity? StartActivity(string operationName, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(operationName, kind);
    }
}
