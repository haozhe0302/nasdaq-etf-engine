using Hqqq.Ingress.Configuration;

namespace Hqqq.Ingress.Tests;

public class TiingoOptionsResolveSymbolsTests
{
    [Fact]
    public void ResolveSymbols_FallsBackToDefaultListWhenEmpty()
    {
        var opts = new TiingoOptions();
        var resolved = opts.ResolveSymbols();

        Assert.NotEmpty(resolved);
        Assert.Equal(TiingoOptions.DefaultSymbols, resolved);
        Assert.Contains("AAPL", resolved);
        Assert.Contains("MSFT", resolved);
        Assert.True(resolved.Count >= 25, $"expected at least 25 symbols, got {resolved.Count}");
    }

    [Fact]
    public void ResolveSymbols_NormalizesUserOverride()
    {
        var opts = new TiingoOptions { Symbols = " aapl, msft ; goog ,, MSFT " };

        var resolved = opts.ResolveSymbols();
        Assert.Equal(new[] { "AAPL", "MSFT", "GOOG" }, resolved);
    }
}
