using System.Diagnostics;

namespace Hqqq.Observability.Identity;

/// <summary>
/// Point-in-time process runtime snapshot used in health payloads.
/// Shape mirrors the Phase 1 hqqq-api RuntimeInfo so the existing
/// frontend BSystemHealth.runtime contract continues to render.
/// </summary>
public sealed record RuntimeInfo
{
    public required long UptimeSeconds { get; init; }
    public required long MemoryMb { get; init; }
    public required int GcGen0 { get; init; }
    public required int GcGen1 { get; init; }
    public required int GcGen2 { get; init; }
    public required int ThreadCount { get; init; }

    public static RuntimeInfo Capture(ServiceIdentity identity)
    {
        var proc = Process.GetCurrentProcess();
        return new RuntimeInfo
        {
            UptimeSeconds = identity.UptimeSeconds,
            MemoryMb = proc.WorkingSet64 / (1024 * 1024),
            GcGen0 = GC.CollectionCount(0),
            GcGen1 = GC.CollectionCount(1),
            GcGen2 = GC.CollectionCount(2),
            ThreadCount = proc.Threads.Count,
        };
    }
}
