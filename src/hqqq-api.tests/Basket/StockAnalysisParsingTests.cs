using Hqqq.Api.Modules.Basket.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hqqq.Api.Tests.Basket;

public class StockAnalysisParsingTests
{
    private readonly StockAnalysisAdapter _adapter;

    public StockAnalysisParsingTests()
    {
        _adapter = new StockAnalysisAdapter(
            new HttpClient(), NullLogger<StockAnalysisAdapter>.Instance);
    }

    [Fact]
    public void Parse_ExtractsHoldings_FromMinimalTable()
    {
        var html = """
            <html>
            <body>
            <p>As of Mar 27, 2026</p>
            <p>Showing 25 of 101 holdings</p>
            <table>
            <tbody>
            <tr>
              <td>1</td>
              <td><a href="/stocks/aapl/">AAPL</a></td>
              <td>Apple Inc.</td>
              <td>8.85%</td>
              <td>44,952,000</td>
            </tr>
            <tr>
              <td>2</td>
              <td><a href="/stocks/msft/">MSFT</a></td>
              <td>Microsoft Corporation</td>
              <td>7.93%</td>
              <td>29,100,000</td>
            </tr>
            </tbody>
            </table>
            </body>
            </html>
            """;

        var result = _adapter.Parse(html);

        Assert.Equal(2, result.Holdings.Count);
        Assert.Equal("AAPL", result.Holdings[0].Symbol);
        Assert.Equal("Apple Inc.", result.Holdings[0].Name);
        Assert.Equal(8.85m, result.Holdings[0].WeightPct);
        Assert.Equal(44952000m, result.Holdings[0].Shares);
        Assert.Equal("MSFT", result.Holdings[1].Symbol);
        Assert.Equal(new DateOnly(2026, 3, 27), result.AsOfDate);
        Assert.Equal(101, result.TotalReported);
    }

    [Fact]
    public void Parse_SkipsRows_WithInsufficientCells()
    {
        var html = """
            <html><body>
            <table><tbody>
            <tr><td>1</td><td><a>AAPL</a></td></tr>
            <tr>
              <td>1</td>
              <td><a href="/stocks/nvda/">NVDA</a></td>
              <td>NVIDIA</td>
              <td>7.5%</td>
              <td>1000</td>
            </tr>
            </tbody></table>
            </body></html>
            """;

        var result = _adapter.Parse(html);

        Assert.Single(result.Holdings);
        Assert.Equal("NVDA", result.Holdings[0].Symbol);
    }

    [Fact]
    public void Parse_SkipsRows_WithEmptySymbolLink()
    {
        var html = """
            <html><body>
            <table><tbody>
            <tr>
              <td>1</td><td><a></a></td><td>Unknown</td><td>1.0%</td><td>100</td>
            </tr>
            </tbody></table>
            </body></html>
            """;

        var result = _adapter.Parse(html);
        Assert.Empty(result.Holdings);
    }

    [Fact]
    public void Parse_FallsBackToToday_WhenNoAsOfDate()
    {
        var html = "<html><body><table><tbody></tbody></table></body></html>";

        var result = _adapter.Parse(html);

        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), result.AsOfDate);
    }

    [Fact]
    public void Parse_ReturnsZeroTotal_WhenPatternNotPresent()
    {
        var html = "<html><body></body></html>";
        var result = _adapter.Parse(html);
        Assert.Equal(0, result.TotalReported);
    }

    [Fact]
    public void Parse_UppercasesSymbol()
    {
        var html = """
            <html><body><table><tbody>
            <tr>
              <td>1</td><td><a>aapl</a></td><td>Apple</td><td>8.0%</td><td>100</td>
            </tr>
            </tbody></table></body></html>
            """;

        var result = _adapter.Parse(html);
        Assert.Equal("AAPL", result.Holdings[0].Symbol);
    }
}
