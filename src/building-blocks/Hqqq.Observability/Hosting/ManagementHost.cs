using Hqqq.Observability.Health;
using Hqqq.Observability.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hqqq.Observability.Hosting;

/// <summary>
/// Embedded HTTP listener that exposes <c>/healthz/live</c>,
/// <c>/healthz/ready</c>, and <c>/metrics</c> in worker services that
/// otherwise have no HTTP surface. Runs as an <see cref="IHostedService"/>
/// inside the worker's generic host so the worker process stays a worker —
/// it gains a Kestrel sidecar, not a full API app conversion.
/// </summary>
public sealed class ManagementHost : IHostedService, IAsyncDisposable
{
    private readonly IServiceProvider _parentServices;
    private readonly ManagementOptions _options;
    private readonly ILogger<ManagementHost> _logger;
    private WebApplication? _app;

    public ManagementHost(
        IServiceProvider parentServices,
        IOptions<ManagementOptions> options,
        ILogger<ManagementHost> logger)
    {
        _parentServices = parentServices;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Management host disabled (Management:Enabled=false)");
            return;
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(
            _parentServices.GetRequiredService<ILoggerFactory>() is { } lf
                ? new ParentLoggerProvider(lf)
                : NullLoggerProvider.Instance);

        builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(o =>
        {
            if (System.Net.IPAddress.TryParse(_options.BindAddress, out var ip))
                o.Listen(ip, _options.Port);
            else
                o.ListenLocalhost(_options.Port);
        });

        // Bridge parent singletons used by the standard endpoints. The
        // embedded app keeps its own DI scope but resolves identity and
        // health-check service from the worker that owns this sidecar.
        builder.Services.AddSingleton(_parentServices.GetRequiredService<ServiceIdentity>());
        builder.Services.AddSingleton(_parentServices.GetRequiredService<HealthCheckService>());

        // The Prometheus exporter belongs to the embedded app: workers do
        // not host any other Kestrel surface, so this is the only place a
        // /metrics endpoint can be served from. The Hqqq meter is a static
        // global so the exporter still observes measurements written by
        // the worker pipeline.
        builder.Services.AddHqqqMetricsExporter();

        var app = builder.Build();

        var identity = _parentServices.GetRequiredService<ServiceIdentity>();
        var parentHealth = _parentServices.GetRequiredService<HealthCheckService>();

        app.MapGet("/healthz/live", async (HttpContext ctx) =>
        {
            var report = await parentHealth.CheckHealthAsync(
                r => r.Tags.Contains(ObservabilityRegistration.LiveTag),
                ctx.RequestAborted);
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(HealthzPayloadBuilder.Build(identity, report));
        });

        app.MapGet("/healthz/ready", async (HttpContext ctx) =>
        {
            var report = await parentHealth.CheckHealthAsync(ctx.RequestAborted);
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(HealthzPayloadBuilder.Build(identity, report));
        });

        app.MapPrometheusScrapingEndpoint("/metrics");

        await app.StartAsync(cancellationToken);
        _app = app;

        _logger.LogInformation(
            "Management host listening on http://{Address}:{Port} (live, ready, metrics)",
            _options.BindAddress,
            ResolveBoundPort(app));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }

    /// <summary>
    /// Returns the actual TCP port Kestrel is bound to. When
    /// <see cref="ManagementOptions.Port"/> is 0 the OS chooses a free port
    /// (used by tests); reading the bound address surfaces it.
    /// </summary>
    public int? BoundPort => _app is null ? null : ResolveBoundPort(_app);

    private static int ResolveBoundPort(WebApplication app)
    {
        var addresses = app.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        if (addresses is null)
            return 0;
        foreach (var addr in addresses.Addresses)
        {
            if (Uri.TryCreate(addr, UriKind.Absolute, out var uri))
                return uri.Port;
        }
        return 0;
    }

    private sealed class ParentLoggerProvider : ILoggerProvider
    {
        private readonly ILoggerFactory _parent;
        public ParentLoggerProvider(ILoggerFactory parent) => _parent = parent;
        public ILogger CreateLogger(string categoryName) => _parent.CreateLogger(categoryName);
        public void Dispose() { }
    }

    private sealed class NullLoggerProvider : ILoggerProvider
    {
        public static readonly NullLoggerProvider Instance = new();
        public ILogger CreateLogger(string categoryName) =>
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        public void Dispose() { }
    }
}

/// <summary>
/// Registration helpers for the worker-side <see cref="ManagementHost"/>.
/// </summary>
public static class ManagementHostExtensions
{
    /// <summary>
    /// Binds <see cref="ManagementOptions"/> from the <c>Management</c>
    /// section and registers <see cref="ManagementHost"/> as a hosted
    /// service. Safe to call multiple times — the registration is keyed
    /// on the singleton type.
    /// </summary>
    public static IServiceCollection AddHqqqManagementHost(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ManagementOptions>(
            configuration.GetSection(ManagementOptions.SectionName));
        services.AddSingleton<ManagementHost>();
        services.AddHostedService(sp => sp.GetRequiredService<ManagementHost>());
        return services;
    }
}
