using Hqqq.Gateway.Services.Sources;

namespace Hqqq.Gateway.Endpoints;

public static class HistoryEndpoints
{
    public static WebApplication MapHistoryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/history", async (string? range, IHistorySource source, CancellationToken ct) =>
            await source.GetHistoryAsync(range, ct))
            .WithName("GetHistory")
            .WithTags("History");

        return app;
    }
}
