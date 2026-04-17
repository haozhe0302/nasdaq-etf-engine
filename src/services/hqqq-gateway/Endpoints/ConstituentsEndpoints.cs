using Hqqq.Gateway.Services.Sources;

namespace Hqqq.Gateway.Endpoints;

public static class ConstituentsEndpoints
{
    public static WebApplication MapConstituentsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/constituents", async (IConstituentsSource source, CancellationToken ct) =>
            await source.GetConstituentsAsync(ct))
            .WithName("GetConstituents")
            .WithTags("Constituents");

        return app;
    }
}
