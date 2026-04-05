using Hqqq.Api.Modules.Benchmark.Services;

namespace Hqqq.Api.Modules.Benchmark;

public static class BenchmarkModule
{
    public static IServiceCollection AddBenchmarkModule(this IServiceCollection services)
    {
        services.AddSingleton<EventRecorderService>();
        return services;
    }
}
