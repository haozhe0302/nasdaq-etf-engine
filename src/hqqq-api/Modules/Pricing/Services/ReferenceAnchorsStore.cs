using Hqqq.Api.Modules.Pricing.Contracts;

namespace Hqqq.Api.Modules.Pricing.Services;

/// <summary>
/// Thread-safe in-memory store for the current <see cref="ReferenceAnchors"/>.
/// Updated daily by <see cref="ReferenceAnchorsRefreshService"/>;
/// read on every quote cycle by <see cref="PricingEngine"/>.
/// </summary>
public sealed class ReferenceAnchorsStore
{
    private volatile ReferenceAnchors? _current;

    public ReferenceAnchors? Get() => _current;

    public void Update(ReferenceAnchors anchors) => _current = anchors;
}
