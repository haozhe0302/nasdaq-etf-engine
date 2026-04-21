using Hqqq.Ingress.Configuration;

namespace Hqqq.Ingress.Tests;

public class TiingoOptionsTests
{
    [Fact]
    public void ResolveOverrideSymbols_IsEmptyByDefault()
    {
        var opts = new TiingoOptions();
        Assert.Empty(opts.ResolveOverrideSymbols());
    }

    [Fact]
    public void ResolveOverrideSymbols_NormalizesUserOverride()
    {
        var opts = new TiingoOptions { Symbols = " aapl, msft ; goog ,, MSFT " };

        var resolved = opts.ResolveOverrideSymbols();
        Assert.Equal(new[] { "AAPL", "MSFT", "GOOG" }, resolved);
    }
}
