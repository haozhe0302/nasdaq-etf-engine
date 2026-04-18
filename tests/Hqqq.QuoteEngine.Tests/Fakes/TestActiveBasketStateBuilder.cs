using Hqqq.Contracts.Events;

namespace Hqqq.QuoteEngine.Tests.Fakes;

/// <summary>
/// Tiny fluent builder for <see cref="BasketActiveStateV1"/> payloads in
/// consumer / restore tests. Mirrors the shape emitted by reference-data.
/// </summary>
public sealed class TestActiveBasketStateBuilder
{
    private readonly List<BasketConstituentV1> _constituents = new();
    private readonly List<PricingBasisEntryV1> _entries = new();

    private string _basketId = "HQQQ";
    private string _fingerprint = "fp-deadbeef";
    private string _version = "v1";
    private DateOnly _asOfDate = new(2026, 4, 16);
    private DateTimeOffset _activatedAtUtc = new(2026, 4, 16, 13, 30, 0, TimeSpan.Zero);
    private decimal _scaleFactor = 0.001m;
    private decimal? _navPreviousClose;
    private decimal? _qqqPreviousClose;

    public TestActiveBasketStateBuilder WithBasketId(string id) { _basketId = id; return this; }
    public TestActiveBasketStateBuilder WithFingerprint(string fp) { _fingerprint = fp; return this; }
    public TestActiveBasketStateBuilder WithVersion(string v) { _version = v; return this; }
    public TestActiveBasketStateBuilder WithScaleFactor(decimal s) { _scaleFactor = s; return this; }
    public TestActiveBasketStateBuilder WithNavPreviousClose(decimal? v) { _navPreviousClose = v; return this; }
    public TestActiveBasketStateBuilder WithQqqPreviousClose(decimal? v) { _qqqPreviousClose = v; return this; }

    public TestActiveBasketStateBuilder AddConstituent(
        string symbol, string name, int shares, decimal referencePrice, decimal weight,
        string sector = "Tech", string sharesOrigin = "official")
    {
        _constituents.Add(new BasketConstituentV1
        {
            Symbol = symbol,
            SecurityName = name,
            Sector = sector,
            TargetWeight = weight,
            SharesHeld = shares,
            SharesOrigin = sharesOrigin,
        });
        _entries.Add(new PricingBasisEntryV1
        {
            Symbol = symbol,
            Shares = shares,
            ReferencePrice = referencePrice,
            SharesOrigin = sharesOrigin,
            TargetWeight = weight,
        });
        return this;
    }

    public BasketActiveStateV1 Build()
    {
        return new BasketActiveStateV1
        {
            BasketId = _basketId,
            Fingerprint = _fingerprint,
            Version = _version,
            AsOfDate = _asOfDate,
            ActivatedAtUtc = _activatedAtUtc,
            Constituents = _constituents,
            PricingBasis = new PricingBasisV1
            {
                PricingBasisFingerprint = _fingerprint + ":basis",
                CreatedAtUtc = _activatedAtUtc,
                Entries = _entries,
                InferredTotalNotional = _entries.Sum(e => e.ReferencePrice * e.Shares),
                OfficialSharesCount = _entries.Count(e => e.SharesOrigin.StartsWith("official")),
                DerivedSharesCount = _entries.Count(e => e.SharesOrigin == "derived"),
            },
            ScaleFactor = _scaleFactor,
            NavPreviousClose = _navPreviousClose,
            QqqPreviousClose = _qqqPreviousClose,
        };
    }
}
