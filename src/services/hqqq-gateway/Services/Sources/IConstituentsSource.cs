namespace Hqqq.Gateway.Services.Sources;

public interface IConstituentsSource
{
    Task<IResult> GetConstituentsAsync(CancellationToken ct);
}
