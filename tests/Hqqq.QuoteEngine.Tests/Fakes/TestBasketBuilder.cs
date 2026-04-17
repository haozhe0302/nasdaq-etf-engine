using Hqqq.Domain.Entities;
using Hqqq.Domain.ValueObjects;
using Hqqq.QuoteEngine.Models;

namespace Hqqq.QuoteEngine.Tests.Fakes;

/// <summary>
/// Fluent helper for assembling deterministic <see cref="ActiveBasket"/> fixtures.
/// </summary>
public sealed class TestBasketBuilder
{
    private readonly List<BasketConstituentState> _constituents = new();
    private readonly List<PricingBasisEntry> _entries = new();

    private string _basketId = "HQQQ";
    private string _fingerprint = "fp-deadbeef";
    private DateOnly _asOfDate = new(2026, 4, 16);
    private DateTimeOffset _activatedAtUtc = new(2026, 4, 16, 13, 30, 0, TimeSpan.Zero);
    private decimal _scaleFactor = 1m;
    private decimal? _navPreviousClose;
    private decimal? _qqqPreviousClose;

    public TestBasketBuilder WithBasketId(string id) { _basketId = id; return this; }
    public TestBasketBuilder WithFingerprint(string fp) { _fingerprint = fp; return this; }
    public TestBasketBuilder WithAsOfDate(DateOnly d) { _asOfDate = d; return this; }
    public TestBasketBuilder WithActivatedAt(DateTimeOffset at) { _activatedAtUtc = at; return this; }
    public TestBasketBuilder WithScaleFactor(decimal s) { _scaleFactor = s; return this; }
    public TestBasketBuilder WithNavPreviousClose(decimal? v) { _navPreviousClose = v; return this; }
    public TestBasketBuilder WithQqqPreviousClose(decimal? v) { _qqqPreviousClose = v; return this; }

    public TestBasketBuilder AddConstituent(
        string symbol,
        string name,
        int shares,
        decimal referencePrice,
        decimal weight,
        string sector = "Tech",
        string sharesOrigin = "official")
    {
        _constituents.Add(new BasketConstituentState
        {
            Symbol = symbol,
            SecurityName = name,
            Sector = sector,
            TargetWeight = weight,
            SharesHeld = shares,
            SharesOrigin = sharesOrigin,
        });
        _entries.Add(new PricingBasisEntry
        {
            Symbol = symbol,
            Shares = shares,
            ReferencePrice = referencePrice,
            SharesOrigin = sharesOrigin,
            TargetWeight = weight,
        });
        return this;
    }

    public ActiveBasket Build()
    {
        var basis = new PricingBasis
        {
            BasketFingerprint = _fingerprint,
            PricingBasisFingerprint = _fingerprint + ":basis",
            CreatedAtUtc = _activatedAtUtc,
            Entries = _entries,
            InferredTotalNotional = _entries.Sum(e => e.ReferencePrice * e.Shares),
            OfficialSharesCount = _entries.Count(e => e.SharesOrigin.StartsWith("official")),
            DerivedSharesCount = _entries.Count(e => e.SharesOrigin == "derived"),
        };

        return new ActiveBasket
        {
            BasketId = _basketId,
            Fingerprint = _fingerprint,
            AsOfDate = _asOfDate,
            ActivatedAtUtc = _activatedAtUtc,
            Constituents = _constituents,
            PricingBasis = basis,
            ScaleFactor = new ScaleFactor(_scaleFactor),
            NavPreviousClose = _navPreviousClose,
            QqqPreviousClose = _qqqPreviousClose,
        };
    }

    public static NormalizedTick Tick(
        string symbol,
        decimal last,
        DateTimeOffset ts,
        decimal? previousClose = null,
        long sequence = 1,
        string provider = "test")
    {
        return new NormalizedTick
        {
            Symbol = symbol,
            Last = last,
            Currency = "USD",
            Provider = provider,
            ProviderTimestamp = ts,
            IngressTimestamp = ts,
            Sequence = sequence,
            PreviousClose = previousClose,
        };
    }
}
