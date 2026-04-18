namespace Hqqq.Gateway.Services.Sources;

public interface IHistorySource
{
    Task<IResult> GetHistoryAsync(string? range, CancellationToken ct);
}
