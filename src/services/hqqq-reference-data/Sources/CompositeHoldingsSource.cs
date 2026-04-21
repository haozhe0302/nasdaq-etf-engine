using Hqqq.ReferenceData.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Sources;

/// <summary>
/// Ordered-arm holdings source: walks each configured primary in order
/// and returns the first snapshot that survives the validator. On every
/// primary being <c>Unavailable</c>, or failing validation (in strict
/// mode), it falls back to the deterministic seed — except in the
/// <em>Production RealSource</em> posture, where silent seed fallback
/// is refused so the operator is never surprised by a live-looking
/// basket that is actually sourced from the committed seed.
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 has two kinds of primary arms registered in this order:
/// <list type="number">
///   <item><c>RealSourceBasketHoldingsSource</c> — the ported Phase 1
///   basket lifecycle (StockAnalysis + Schwab anchors + AlphaVantage /
///   Nasdaq tail). This is the production path.</item>
///   <item><c>LiveHoldingsSource</c> — configuration-driven file or
///   HTTP drop. Useful for dev/demo bring-up and as a belt-and-suspenders
///   arm when the real-source pipeline is temporarily unavailable.</item>
/// </list>
/// When <see cref="BasketMode.Seed"/> is selected, the real-source arm
/// is not registered; the composite degrades to "live (file/http) →
/// seed", matching the pre-port behaviour.
/// </para>
/// <para>
/// Production guard: if <c>environment.IsProduction()</c> AND
/// <c>Mode == RealSource</c> AND
/// <c>AllowDeterministicSeedInProduction == false</c>, the composite
/// returns <see cref="HoldingsFetchResult.Unavailable"/> instead of
/// serving the seed. The active basket stays <c>null</c>, readiness
/// flips to <c>Degraded</c>/503 (via <c>ActiveBasketHealthCheck</c>),
/// and the orchestrator can take the pod out of rotation instead of
/// letting a seed-basket ship into the wire event.
/// </para>
/// </remarks>
public sealed class CompositeHoldingsSource : IHoldingsSource
{
    private readonly IReadOnlyList<IHoldingsSource> _primaries;
    private readonly FallbackSeedHoldingsSource _fallback;
    private readonly HoldingsValidator _validator;
    private readonly BasketOptions _basketOptions;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<CompositeHoldingsSource> _logger;

    /// <summary>Primary ctor used in production — posture-aware.</summary>
    public CompositeHoldingsSource(
        IEnumerable<IHoldingsSource> primaries,
        FallbackSeedHoldingsSource fallback,
        HoldingsValidator validator,
        IOptions<ReferenceDataOptions> options,
        IWebHostEnvironment environment,
        ILogger<CompositeHoldingsSource> logger)
    {
        _primaries = primaries?.ToArray() ?? Array.Empty<IHoldingsSource>();
        _fallback = fallback;
        _validator = validator;
        _basketOptions = options.Value.Basket;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Back-compat ctor used by existing tests (single primary + fallback).
    /// Defaults the posture to a Development environment + RealSource +
    /// deterministic-seed-allowed so legacy tests keep the historic
    /// "live → seed" fallback behaviour.
    /// </summary>
    public CompositeHoldingsSource(
        LiveHoldingsSource live,
        FallbackSeedHoldingsSource fallback,
        HoldingsValidator validator,
        ILogger<CompositeHoldingsSource> logger)
        : this(
            new IHoldingsSource[] { live },
            fallback,
            validator,
            Options.Create(new ReferenceDataOptions
            {
                Basket = new BasketOptions
                {
                    Mode = BasketMode.RealSource,
                    AllowDeterministicSeedInProduction = true,
                },
            }),
            new DevelopmentEnvironmentStub(),
            logger)
    {
    }

    public string Name =>
        $"composite({string.Join(",", _primaries.Select(p => p.Name))},fallback-seed)";

    public async Task<HoldingsFetchResult> FetchAsync(CancellationToken ct)
    {
        foreach (var primary in _primaries)
        {
            var result = await primary.FetchAsync(ct).ConfigureAwait(false);

            if (result.Status == HoldingsFetchStatus.Ok && result.Snapshot is not null)
            {
                var outcome = _validator.Validate(result.Snapshot);
                if (!_validator.BlocksActivation(outcome))
                {
                    _logger.LogInformation(
                        "Composite: primary {Name} accepted ({Count} constituents, asOf={AsOf}, valid={Valid})",
                        primary.Name, result.Snapshot.Constituents.Count,
                        result.Snapshot.AsOfDate, outcome.IsValid);
                    return result;
                }

                _logger.LogWarning(
                    "Composite: primary {Name} rejected by validator ({Errors}); trying next arm",
                    primary.Name, string.Join("; ", outcome.Errors));
                continue;
            }

            if (result.Status == HoldingsFetchStatus.Invalid)
            {
                _logger.LogWarning(
                    "Composite: primary {Name} returned Invalid ({Reason}); trying next arm",
                    primary.Name, result.Reason);
            }
            else
            {
                _logger.LogInformation(
                    "Composite: primary {Name} unavailable ({Reason}); trying next arm",
                    primary.Name, result.Reason ?? "no-reason");
            }
        }

        // Production guard — refuse silent seed fallback so an operator
        // never sees a live-looking active basket that is actually the
        // committed seed.
        if (_environment.IsProduction()
            && _basketOptions.Mode == BasketMode.RealSource
            && !_basketOptions.AllowDeterministicSeedInProduction)
        {
            _logger.LogWarning(
                "Composite: Production RealSource posture with AllowDeterministicSeedInProduction=false — refusing seed fallback. Active basket remains unavailable until an upstream primary produces a valid snapshot.");
            return HoldingsFetchResult.Unavailable(
                "production seed-fallback refused; upstream real-source not ready");
        }

        _logger.LogInformation("Composite: all primaries exhausted; using fallback seed");
        return await _fallback.FetchAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Minimal <see cref="IWebHostEnvironment"/> stub used by the
    /// back-compat ctor so the composite's posture guard is a no-op
    /// when legacy tests construct the composite directly.
    /// </summary>
    private sealed class DevelopmentEnvironmentStub : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "hqqq-reference-data";
        public string ContentRootPath { get; set; } = string.Empty;
        public string WebRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
