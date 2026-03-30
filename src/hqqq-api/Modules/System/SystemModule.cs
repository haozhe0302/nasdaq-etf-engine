using Hqqq.Api.Modules.System.Contracts;

namespace Hqqq.Api.Modules.System;

public static class SystemModule
{
    public static IServiceCollection AddSystemModule(this IServiceCollection services)
    {
        // TODO: Phase B — register health-check probes for each dependency
        return services;
    }

    public static WebApplication MapSystemEndpoints(this WebApplication app)
    {
        app.MapGet("/api/system/health", () =>
        {
            var health = new SystemHealth
            {
                ServiceName = "hqqq-api",
                Status = "healthy",
                CheckedAtUtc = DateTimeOffset.UtcNow,
                Version = typeof(SystemModule).Assembly
                    .GetName().Version?.ToString() ?? "0.0.0",
                Dependencies = []
            };

            return Results.Ok(health);
        })
        .WithName("GetSystemHealth")
        .WithTags("System")
        .WithOpenApi();

        return app;
    }
}
