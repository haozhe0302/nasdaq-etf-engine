namespace Hqqq.Infrastructure.Hosting;

/// <summary>
/// Phase 2 operating-mode tag. Historically this selector chose between
/// "hybrid" (legacy monolith bridged ticks / basket) and "standalone"
/// (Phase 2 owned the path end-to-end). In the current runtime Phase 2
/// is <b>unconditionally self-sufficient</b> — the enum is retained as
/// a logging-posture tag for cross-service consistency in structured
/// logs and for config-binding backwards compatibility.
/// </summary>
/// <remarks>
/// <para>
/// Runtime behaviour no longer branches on this value in any Phase 2
/// service. <c>hqqq-ingress</c> always opens the real Tiingo websocket,
/// <c>hqqq-reference-data</c> always owns the active basket, and the
/// gateway's system-health rollup always requires ingress + reference-
/// data. The <c>hqqq-api</c> monolith is repo-only reference code.
/// </para>
/// </remarks>
public enum OperatingMode
{
    /// <summary>
    /// Legacy tag. Kept so existing <c>HQQQ_OPERATING_MODE=hybrid</c>
    /// config continues to bind; same runtime behaviour as
    /// <see cref="Standalone"/>.
    /// </summary>
    Hybrid = 0,

    /// <summary>The recommended logging-posture tag for Phase 2 runtime.</summary>
    Standalone = 1,
}
