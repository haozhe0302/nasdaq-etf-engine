using System.Reflection;
using Microsoft.Extensions.Hosting;

namespace Hqqq.Observability.Identity;

/// <summary>
/// Stable per-process metadata that every Phase 2 service emits in its
/// health and metrics surfaces. Captured once at startup and registered
/// as a singleton via <see cref="Hosting.ObservabilityRegistration.AddHqqqObservability"/>.
/// </summary>
public sealed record ServiceIdentity
{
    public required string ServiceName { get; init; }
    public required string ServiceVersion { get; init; }
    public required string Environment { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required string MachineName { get; init; }

    public long UptimeSeconds =>
        (long)Math.Max(0, (DateTimeOffset.UtcNow - StartedAtUtc).TotalSeconds);

    public static ServiceIdentity Capture(string serviceName, IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name is required", nameof(serviceName));

        return new ServiceIdentity
        {
            ServiceName = serviceName,
            ServiceVersion = ResolveVersion(),
            Environment = environment.EnvironmentName,
            StartedAtUtc = DateTimeOffset.UtcNow,
            MachineName = System.Environment.MachineName,
        };
    }

    private static string ResolveVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var attr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var raw = attr?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(raw))
            return asm.GetName().Version?.ToString() ?? "0.0.0";

        // Cap any "+<sha>" build metadata to 8 chars for display compactness,
        // matching the Phase 1 SystemModule.GetInformationalVersion behavior.
        var plus = raw.IndexOf('+');
        if (plus >= 0 && plus < raw.Length - 1)
        {
            var suffix = raw[(plus + 1)..];
            if (suffix.Length > 8)
                return raw[..(plus + 1)] + suffix[..8];
        }

        return raw;
    }
}
