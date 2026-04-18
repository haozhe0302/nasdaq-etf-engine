using Hqqq.Analytics.Options;
using Hqqq.Analytics.Reports;
using Hqqq.Analytics.Services;
using Hqqq.Analytics.Timescale;
using Hqqq.Analytics.Workers;
using Hqqq.Infrastructure.Hosting;
using Hqqq.Infrastructure.Timescale;
using Hqqq.Observability.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();
builder.Logging.AddHqqqDefaults();

// C4 analytics is a one-shot report job off the persisted Timescale tables.
// It intentionally does NOT participate in Kafka, Redis, or any HTTP wiring.
builder.Services.AddHqqqTimescale(builder.Configuration);
builder.Services.AddHqqqObservability();

builder.Services.Configure<AnalyticsOptions>(builder.Configuration.GetSection("Analytics"));
builder.Services.AddSingleton<IValidateOptions<AnalyticsOptions>, AnalyticsOptionsValidator>();

// Shared data source so the snapshot reader and raw-tick aggregate reader
// share one connection pool, matching the pattern used by hqqq-persistence.
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<TimescaleOptions>>().Value;
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(TimescaleConnectionFactory));
    return TimescaleConnectionFactory.Create(options, logger);
});

builder.Services.AddSingleton<IQuoteSnapshotReader, TimescaleQuoteSnapshotReader>();
builder.Services.AddSingleton<IRawTickAggregateReader, TimescaleRawTickAggregateReader>();
builder.Services.AddSingleton<JsonReportEmitter>();

// Register the report job behind its interface so the dispatcher can select
// by Mode. Future modes (replay, anomaly, backfill) will register additional
// IReportJob implementations alongside this one.
builder.Services.AddSingleton<IReportJob, SnapshotQualityReportJob>();

builder.Services.AddHostedService<ReportJobDispatcher>();

var host = builder.Build();

host.Services.LogConfigurationPosture(
    "hqqq-analytics",
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"),
    "Analytics", "Timescale");

// Fail fast on bad options: resolve once so the validator runs before the
// dispatcher starts.
_ = host.Services.GetRequiredService<IOptions<AnalyticsOptions>>().Value;

await host.RunAsync().ConfigureAwait(false);
