namespace Hqqq.Gateway.Services.Sources;

public interface ISystemHealthSource
{
    Task<IResult> GetSystemHealthAsync(CancellationToken ct);
}
