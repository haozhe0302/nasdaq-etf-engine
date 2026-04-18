using Hqqq.Analytics.Options;
using Microsoft.Extensions.Configuration;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Hqqq.Analytics.Tests.Configuration;

public class AnalyticsOptionsBindingTests
{
    [Fact]
    public void Defaults_AreReasonable()
    {
        var opts = new AnalyticsOptions();

        Assert.Equal("report", opts.Mode);
        Assert.Equal("HQQQ", opts.BasketId);
        Assert.Null(opts.StartUtc);
        Assert.Null(opts.EndUtc);
        Assert.Null(opts.EmitJsonPath);
        Assert.Equal(1_000_000, opts.MaxRows);
        Assert.False(opts.IncludeRawTickAggregates);
        Assert.Equal(new[] { "stale", "degraded" }, opts.StaleQualityStates);
        Assert.Equal(5, opts.TopGapCount);
    }

    [Fact]
    public void BindsFromAnalyticsSection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Analytics:Mode"] = "report",
                ["Analytics:BasketId"] = "HQQQ-TEST",
                ["Analytics:StartUtc"] = "2026-04-17T00:00:00Z",
                ["Analytics:EndUtc"] = "2026-04-18T00:00:00Z",
                ["Analytics:EmitJsonPath"] = "artifacts/out.json",
                ["Analytics:MaxRows"] = "500",
                ["Analytics:IncludeRawTickAggregates"] = "true",
                ["Analytics:StaleQualityStates:0"] = "stale",
                ["Analytics:StaleQualityStates:1"] = "paused",
                ["Analytics:TopGapCount"] = "3",
            })
            .Build();

        // Start from an empty array so we assert binding rather than defaults+binding.
        var opts = new AnalyticsOptions { StaleQualityStates = Array.Empty<string>() };
        config.GetSection("Analytics").Bind(opts);

        Assert.Equal("report", opts.Mode);
        Assert.Equal("HQQQ-TEST", opts.BasketId);
        Assert.Equal(new DateTimeOffset(2026, 4, 17, 0, 0, 0, TimeSpan.Zero), opts.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 4, 18, 0, 0, 0, TimeSpan.Zero), opts.EndUtc);
        Assert.Equal("artifacts/out.json", opts.EmitJsonPath);
        Assert.Equal(500, opts.MaxRows);
        Assert.True(opts.IncludeRawTickAggregates);
        Assert.Equal(new[] { "stale", "paused" }, opts.StaleQualityStates);
        Assert.Equal(3, opts.TopGapCount);
    }

    [Fact]
    public void MissingSection_KeepsDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var opts = new AnalyticsOptions();
        config.GetSection("Analytics").Bind(opts);

        Assert.Equal("report", opts.Mode);
        Assert.Equal("HQQQ", opts.BasketId);
        Assert.Equal(1_000_000, opts.MaxRows);
    }

    [Fact]
    public void Validator_FailsWhenReportWindowMissing()
    {
        var validator = new AnalyticsOptionsValidator();

        var result = validator.Validate(MsOptions.DefaultName, new AnalyticsOptions { Mode = "report" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("StartUtc", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("EndUtc", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_FailsWhenStartAfterOrEqualEnd()
    {
        var validator = new AnalyticsOptionsValidator();
        var ts = new DateTimeOffset(2026, 4, 17, 0, 0, 0, TimeSpan.Zero);

        var result = validator.Validate(MsOptions.DefaultName, new AnalyticsOptions
        {
            Mode = "report",
            StartUtc = ts,
            EndUtc = ts,
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("strictly less", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_AcceptsValidReportWindow()
    {
        var validator = new AnalyticsOptionsValidator();

        var result = validator.Validate(MsOptions.DefaultName, new AnalyticsOptions
        {
            Mode = "report",
            StartUtc = new DateTimeOffset(2026, 4, 17, 0, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 4, 18, 0, 0, 0, TimeSpan.Zero),
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validator_FailsOnNonPositiveMaxRows()
    {
        var validator = new AnalyticsOptionsValidator();

        var result = validator.Validate(MsOptions.DefaultName, new AnalyticsOptions
        {
            Mode = "report",
            MaxRows = 0,
            StartUtc = DateTimeOffset.UtcNow.AddHours(-1),
            EndUtc = DateTimeOffset.UtcNow,
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("MaxRows", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_IgnoresWindowWhenModeIsNotReport()
    {
        var validator = new AnalyticsOptionsValidator();

        var result = validator.Validate(MsOptions.DefaultName, new AnalyticsOptions
        {
            Mode = "replay",
        });

        Assert.True(result.Succeeded);
    }
}

