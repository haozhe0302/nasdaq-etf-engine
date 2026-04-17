using Hqqq.Gateway.Services.Sources;

namespace Hqqq.Gateway.Endpoints;

public static class SystemHealthEndpoints
{
    public static WebApplication MapSystemHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/system/health", async (ISystemHealthSource source, CancellationToken ct) =>
            await source.GetSystemHealthAsync(ct))
            .WithName("GetSystemHealth")
            .WithTags("System");

        return app;
    }
}
