using System.Reflection;
using Hqqq.Api.Modules.Basket.Services;

namespace Hqqq.Api.Tests.Basket;

public class AlphaVantageFilteringTests
{
    private static bool InvokeIsValidEquityRow(AlphaVantageAdapter.AvHolding h)
    {
        var method = typeof(AlphaVantageAdapter)
            .GetMethod("IsValidEquityRow", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, [h])!;
    }

    [Fact]
    public void AcceptsNormalEquity()
    {
        var h = new AlphaVantageAdapter.AvHolding
        {
            Symbol = "AAPL",
            Description = "Apple Inc",
            Weight = "8.85",
        };
        Assert.True(InvokeIsValidEquityRow(h));
    }

    [Theory]
    [InlineData("n/a", "CASH COLLATERAL")]
    [InlineData("N/A", "Some Future Thing")]
    [InlineData("", "Something")]
    [InlineData(null, "Something")]
    [InlineData("  ", "Something")]
    public void RejectsInvalidSymbols(string? symbol, string description)
    {
        var h = new AlphaVantageAdapter.AvHolding
        {
            Symbol = symbol,
            Description = description,
            Weight = "0.1",
        };
        Assert.False(InvokeIsValidEquityRow(h));
    }

    [Theory]
    [InlineData("CASH COLLATERAL")]
    [InlineData("S&P 500 FUTURE")]
    [InlineData("MONEY MARKET FUND")]
    public void RejectsNonEquityDescriptions(string description)
    {
        var h = new AlphaVantageAdapter.AvHolding
        {
            Symbol = "XYZ",
            Description = description,
            Weight = "0.1",
        };
        Assert.False(InvokeIsValidEquityRow(h));
    }

    [Fact]
    public void AcceptsNullDescription()
    {
        var h = new AlphaVantageAdapter.AvHolding
        {
            Symbol = "GOOG",
            Description = null,
            Weight = "5.0",
        };
        Assert.True(InvokeIsValidEquityRow(h));
    }
}
