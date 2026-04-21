using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.CorporateActions.Contracts;
using Hqqq.ReferenceData.CorporateActions.Services;
using Hqqq.ReferenceData.Publishing;
using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests.TestDoubles;

/// <summary>
/// Convenience builder that constructs a <see cref="BasketRefreshPipeline"/>
/// with all the DI wiring tests historically re-created by hand — now
/// extended with the corporate-action + transition layer.
/// </summary>
internal static class PipelineBuilder
{
    public static Bench Build(
        StubHoldingsSource? source = null,
        CapturingPublisher? publisher = null,
        ActiveBasketStore? store = null,
        PublishHealthTracker? publishHealth = null,
        ReferenceDataOptions? options = null,
        ICorporateActionProvider? corpActionProvider = null,
        TimeProvider? clock = null)
        => BuildCore(
            source,
            publisher ?? new CapturingPublisher(),
            externalPublisher: null,
            store, publishHealth, options, corpActionProvider, clock);

    /// <summary>
    /// Builds a bench wired to an arbitrary <see cref="IBasketPublisher"/>.
    /// The <see cref="Bench.Publisher"/> is a no-op
    /// <see cref="CapturingPublisher"/> kept only to preserve the existing
    /// shape for callers that do not care about publish-side assertions.
    /// </summary>
    public static Bench BuildWithPublisher(
        IBasketPublisher publisher,
        StubHoldingsSource? source = null,
        ActiveBasketStore? store = null,
        PublishHealthTracker? publishHealth = null,
        ReferenceDataOptions? options = null,
        ICorporateActionProvider? corpActionProvider = null,
        TimeProvider? clock = null)
        => BuildCore(
            source,
            new CapturingPublisher(),
            externalPublisher: publisher,
            store, publishHealth, options, corpActionProvider, clock);

    private static Bench BuildCore(
        StubHoldingsSource? source,
        CapturingPublisher publisher,
        IBasketPublisher? externalPublisher,
        ActiveBasketStore? store,
        PublishHealthTracker? publishHealth,
        ReferenceDataOptions? options,
        ICorporateActionProvider? corpActionProvider,
        TimeProvider? clock)
    {
        source ??= new StubHoldingsSource();
        store ??= new ActiveBasketStore();
        publishHealth ??= new PublishHealthTracker();
        options ??= new ReferenceDataOptions
        {
            Validation = new ValidationOptions { Strict = true, MinConstituents = 1, MaxConstituents = 500 },
        };
        corpActionProvider ??= new StubCorporateActionProvider();

        var opts = Options.Create(options);
        var validator = new HoldingsValidator(opts);
        var adjustments = new CorporateActionAdjustmentService(
            corpActionProvider,
            opts,
            NullLogger<CorporateActionAdjustmentService>.Instance,
            clock);
        var transition = new BasketTransitionPlanner();

        var pipeline = new BasketRefreshPipeline(
            source, validator, adjustments, transition, store,
            externalPublisher ?? publisher, publishHealth,
            NullLogger<BasketRefreshPipeline>.Instance, clock);

        return new Bench(pipeline, source, publisher, store, publishHealth);
    }

    public sealed record Bench(
        BasketRefreshPipeline Pipeline,
        StubHoldingsSource Source,
        CapturingPublisher Publisher,
        ActiveBasketStore Store,
        PublishHealthTracker PublishHealth);
}

/// <summary>Deterministic corp-action provider for tests — empty feed by default.</summary>
internal sealed class StubCorporateActionProvider : ICorporateActionProvider
{
    public IReadOnlyList<SplitEvent> Splits { get; set; } = Array.Empty<SplitEvent>();
    public IReadOnlyList<SymbolRenameEvent> Renames { get; set; } = Array.Empty<SymbolRenameEvent>();
    public string SourceTag { get; set; } = "stub";
    public string? Error { get; set; }

    public string Name => SourceTag;

    public Task<CorporateActionFeed> FetchAsync(
        IReadOnlyCollection<string> symbols, DateOnly from, DateOnly to, CancellationToken ct)
    {
        return Task.FromResult(new CorporateActionFeed
        {
            Splits = Splits,
            Renames = Renames,
            Source = SourceTag,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Error = Error,
        });
    }
}
