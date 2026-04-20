namespace Hqqq.Infrastructure.Hosting;

/// <summary>
/// Phase 2 operating mode. Selected per-deployment via the
/// <c>HQQQ_OPERATING_MODE</c> env / <c>OperatingMode</c> config key.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hybrid</b> preserves the original Phase 2 demo posture: the legacy
/// monolith (<c>hqqq-api</c>) is allowed to bridge market ticks and basket
/// state into Kafka, and the Phase 2 services that haven't yet replaced
/// the monolith stay as stubs (e.g. <c>hqqq-ingress</c> idles, and
/// <c>hqqq-reference-data</c> serves an in-memory seed without publishing
/// <c>refdata.basket.active.v1</c>).
/// </para>
/// <para>
/// <b>Standalone</b> is the self-contained Phase 2 posture: no monolith
/// bridge is permitted; <c>hqqq-ingress</c> opens a real Tiingo IEX
/// websocket and publishes ticks itself, and <c>hqqq-reference-data</c>
/// publishes a deterministic basket on startup. Services in standalone
/// mode <i>fail fast</i> at startup if the inputs they need (Tiingo API
/// key, basket seed) are missing.
/// </para>
/// </remarks>
public enum OperatingMode
{
    /// <summary>Legacy bridge allowed; Phase 2 native ingestion/refdata are stubs.</summary>
    Hybrid = 0,

    /// <summary>Phase 2 native ingestion/refdata required; no monolith bridge.</summary>
    Standalone = 1,
}
