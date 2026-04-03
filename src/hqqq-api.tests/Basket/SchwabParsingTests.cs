using Hqqq.Api.Modules.Basket.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hqqq.Api.Tests.Basket;

public class SchwabParsingTests
{
    private readonly SchwabHoldingsAdapter _adapter;

    public SchwabParsingTests()
    {
        _adapter = new SchwabHoldingsAdapter(
            new HttpClient(), NullLogger<SchwabHoldingsAdapter>.Instance);
    }

    [Fact]
    public void Parse_ExtractsHoldings_FromTsRawAttributes()
    {
        var html = """
            <html><body>
            <script>var gHoldingsAsOfDate = '03/27/2026';</script>
            <p>1 - 20 of 102 matches</p>
            <table>
            <tbody id="tthHoldingsTbody">
            <tr>
              <td class="symbol firstColumn" tsraw="AAPL">AAPL</td>
              <td class="description" tsraw="Apple Inc">Apple</td>
              <td tsraw="8.85">8.85%</td>
              <td tsraw="44952000">44,952,000</td>
              <td tsraw="8914000000">$8,914,000,000</td>
            </tr>
            <tr>
              <td class="symbol firstColumn" tsraw="MSFT">MSFT</td>
              <td class="description" tsraw="Microsoft Corp">Microsoft</td>
              <td tsraw="7.93">7.93%</td>
              <td tsraw="29100000">29,100,000</td>
              <td tsraw="12500000000">$12,500,000,000</td>
            </tr>
            </tbody>
            </table>
            </body></html>
            """;

        var result = _adapter.Parse(html);

        Assert.Equal(2, result.Constituents.Count);
        Assert.Equal("AAPL", result.Constituents[0].Symbol);
        Assert.Equal("Apple Inc", result.Constituents[0].SecurityName);
        Assert.Equal(0.0885m, result.Constituents[0].Weight);
        Assert.Equal(44952000m, result.Constituents[0].SharesHeld);
        Assert.Equal(new DateOnly(2026, 3, 27), result.AsOfDate);
        Assert.Equal(102, result.TotalHoldingsReported);
    }

    [Fact]
    public void Parse_ReturnsEmpty_WhenTbodyMissing()
    {
        var html = "<html><body><table></table></body></html>";

        var result = _adapter.Parse(html);

        Assert.Empty(result.Constituents);
    }

    [Fact]
    public void Parse_SkipsRows_WithBlankSymbol()
    {
        var html = """
            <html><body>
            <table><tbody id="tthHoldingsTbody">
            <tr>
              <td tsraw="">-</td>
              <td tsraw="Cash">Cash</td>
              <td tsraw="0.1">0.1%</td>
              <td tsraw="0">0</td>
              <td tsraw="0">$0</td>
            </tr>
            </tbody></table>
            </body></html>
            """;

        var result = _adapter.Parse(html);
        Assert.Empty(result.Constituents);
    }

    [Fact]
    public void Parse_UppercasesSymbol()
    {
        var html = """
            <html><body>
            <table><tbody id="tthHoldingsTbody">
            <tr>
              <td tsraw="nvda">nvda</td>
              <td tsraw="NVIDIA">NVIDIA</td>
              <td tsraw="7.5">7.5%</td>
              <td tsraw="1000">1,000</td>
              <td tsraw="500000">$500,000</td>
            </tr>
            </tbody></table>
            </body></html>
            """;

        var result = _adapter.Parse(html);
        Assert.Equal("NVDA", result.Constituents[0].Symbol);
    }

    [Fact]
    public void Parse_FallsBackToToday_WhenNoAsOfDate()
    {
        var html = """
            <html><body>
            <table><tbody id="tthHoldingsTbody">
            <tr>
              <td tsraw="AAPL">AAPL</td>
              <td tsraw="Apple">Apple</td>
              <td tsraw="8">8%</td>
              <td tsraw="100">100</td>
              <td tsraw="100">$100</td>
            </tr>
            </tbody></table>
            </body></html>
            """;

        var result = _adapter.Parse(html);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), result.AsOfDate);
    }

    [Fact]
    public void Parse_DividesWeightBy100_ForFractionRepresentation()
    {
        var html = """
            <html><body>
            <table><tbody id="tthHoldingsTbody">
            <tr>
              <td tsraw="AAPL">AAPL</td>
              <td tsraw="Apple">Apple</td>
              <td tsraw="10.5">10.5%</td>
              <td tsraw="500">500</td>
              <td tsraw="1000">$1,000</td>
            </tr>
            </tbody></table>
            </body></html>
            """;

        var result = _adapter.Parse(html);
        Assert.Equal(0.105m, result.Constituents[0].Weight);
    }
}
