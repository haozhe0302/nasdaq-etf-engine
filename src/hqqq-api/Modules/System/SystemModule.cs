using Hqqq.Api.Modules.System.Contracts;
using Microsoft.Extensions.Options;
using Hqqq.Api.Configuration;

namespace Hqqq.Api.Modules.System;

public static class SystemModule
{
    public static IServiceCollection AddSystemModule(this IServiceCollection services)
    {
        return services;
    }

    public static WebApplication MapSystemEndpoints(this WebApplication app)
    {
        app.MapGet("/api/system/health", (IOptions<FeatureOptions> featureOpts) =>
        {
            var features = featureOpts.Value;

            var health = new SystemHealth
            {
                ServiceName = "hqqq-api",
                Status = "healthy",
                CheckedAtUtc = DateTimeOffset.UtcNow,
                Version = typeof(SystemModule).Assembly
                    .GetName().Version?.ToString() ?? "0.0.0",
                Dependencies =
                [
                    new DependencyHealth
                    {
                        Name = "tiingo",
                        Status = features.EnableLiveMode ? "unknown" : "disabled",
                        LastCheckedAtUtc = DateTimeOffset.UtcNow,
                        Details = features.EnableLiveMode
                            ? "Live mode enabled; feed not yet connected"
                            : "Live mode disabled",
                    },
                ],
            };

            return Results.Ok(health);
        })
        .WithName("GetSystemHealth")
        .WithTags("System")
        .WithOpenApi();

        return app;
    }
}
