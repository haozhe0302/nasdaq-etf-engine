namespace Hqqq.QuoteEngine.Persistence;

/// <summary>
/// Persistence seam for <see cref="CalibrationRecord"/>. The production
/// implementation is <see cref="RedisCalibrationStore"/>; tests substitute
/// an in-memory fake so they can drive coordinator state transitions
/// without standing up Redis.
/// </summary>
/// <remarks>
/// The API is intentionally synchronous — the coordinator calls into the
/// store from the engine's hot tick path and a Redis miss/hit is a single
/// round-trip we already accept elsewhere (snapshot writer, channel
/// publisher). Async would force the entire OnTick chain to become async
/// for negligible benefit.
/// </remarks>
public interface ICalibrationStore
{
    /// <summary>
    /// Returns the most recent calibration for <paramref name="basketId"/>,
    /// or <c>null</c> if no record exists or it has expired.
    /// Implementations must swallow transient backend errors and return
    /// <c>null</c> so a Redis outage degrades to "needs bootstrap"
    /// rather than taking the engine down.
    /// </summary>
    CalibrationRecord? TryGet(string basketId);

    /// <summary>
    /// Persists <paramref name="record"/>, overwriting any existing entry
    /// for the same <see cref="CalibrationRecord.BasketId"/>. TTL is
    /// applied by the implementation. Failures are swallowed and logged
    /// — calibration succeeds in-memory even if the persistence layer
    /// is briefly unavailable, and the next basket-activation cycle will
    /// repeat the persistence attempt.
    /// </summary>
    void Set(CalibrationRecord record);
}
