namespace Hqqq.Api.Modules.MarketData.Contracts;

/// <summary>
/// Thread-safe in-memory store for the latest price of each tracked symbol.
/// </summary>
public interface ILatestPriceStore
{
    void Update(PriceTick tick);
    LatestPriceState? Get(string symbol);
    IReadOnlyDictionary<string, LatestPriceState> GetAll();
    IReadOnlyList<LatestPriceState> GetLatest(IEnumerable<string> symbols);
    FeedHealthSnapshot GetHealthSnapshot();
    void SetTrackedSymbols(IReadOnlyDictionary<string, SymbolRole> symbolRoles);
    IReadOnlyDictionary<string, SymbolRole> GetTrackedSymbols();
}
