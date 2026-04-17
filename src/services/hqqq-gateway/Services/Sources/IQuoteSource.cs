namespace Hqqq.Gateway.Services.Sources;

public interface IQuoteSource
{
    Task<IResult> GetQuoteAsync(CancellationToken ct);
}
