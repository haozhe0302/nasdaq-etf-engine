using Hqqq.ReferenceData.Basket;
using Hqqq.ReferenceData.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hqqq.ReferenceData.Tests.Basket;

/// <summary>
/// Tests the Production fail-fast guards that prevent a Phase 2
/// deployment from silently booting on deterministic seed data or on
/// an offline-only corp-action provider.
/// </summary>
public class ReferenceDataStartupGuardTests
{
    [Fact]
    public void Validate_Development_NeverThrows()
    {
        var opts = new ReferenceDataOptions
        {
            Basket = new BasketOptions { Mode = BasketMode.Seed },
        };
        ReferenceDataStartupGuard.Validate(Env("Development"), opts, NullLogger.Instance);
    }

    [Fact]
    public void Validate_Production_SeedMode_WithoutOverride_Throws()
    {
        var opts = new ReferenceDataOptions
        {
            Basket = new BasketOptions
            {
                Mode = BasketMode.Seed,
                AllowDeterministicSeedInProduction = false,
            },
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => ReferenceDataStartupGuard.Validate(Env("Production"), opts, NullLogger.Instance));
        Assert.Contains("Mode=Seed", ex.Message);
    }

    [Fact]
    public void Validate_Production_SeedMode_WithOverride_Allowed()
    {
        var opts = new ReferenceDataOptions
        {
            Basket = new BasketOptions
            {
                Mode = BasketMode.Seed,
                AllowDeterministicSeedInProduction = true,
            },
        };

        ReferenceDataStartupGuard.Validate(Env("Production"), opts, NullLogger.Instance);
    }

    [Fact]
    public void Validate_Production_RealSource_AllAdaptersDisabled_Throws()
    {
        var opts = new ReferenceDataOptions
        {
            Basket = new BasketOptions
            {
                Mode = BasketMode.RealSource,
                Sources = new BasketSourcesOptions
                {
                    AlphaVantage = new AlphaVantageSourceOptions { Enabled = false },
                    Nasdaq = new NasdaqSourceOptions { Enabled = false },
                },
            },
        };

        Assert.Throws<InvalidOperationException>(
            () => ReferenceDataStartupGuard.Validate(Env("Production"), opts, NullLogger.Instance));
    }

    [Fact]
    public void Validate_Production_RealSource_StockAnalysisAnchorEnabled_WithAlphaVantage_Allowed()
    {
        // The standard Production path is the full four-source anchored
        // pipeline: StockAnalysis/Schwab anchor + AlphaVantage tail +
        // Nasdaq guardrail. Enabling StockAnalysis as the anchor AND
        // AlphaVantage with a real key satisfies both
        // RequireAnchorInProduction=true and the strict AlphaVantage
        // contract, so the guard passes quietly.
        var opts = new ReferenceDataOptions
        {
            Basket = new BasketOptions
            {
                Mode = BasketMode.RealSource,
                Sources = new BasketSourcesOptions
                {
                    StockAnalysis = new StockAnalysisSourceOptions { Enabled = true },
                    AlphaVantage = new AlphaVantageSourceOptions { Enabled = true, ApiKey = "real-key" },
                    Nasdaq = new NasdaqSourceOptions { Enabled = true },
                },
            },
        };

        ReferenceDataStartupGuard.Validate(Env("Production"), opts, NullLogger.Instance);
    }

    [Fact]
    public void Validate_Production_RealSource_SchwabAnchorEnabled_WithAlphaVantage_Allowed()
    {
        // Either anchor scraper alone is enough for RequireAnchor, but
        // the strict four-source contract still requires AlphaVantage
        // to be effectively enabled.
        var opts = new ReferenceDataOptions
        {
            Basket = new BasketOptions
            {
                Mode = BasketMode.RealSource,
                Sources = new BasketSourcesOptions
                {
                    Schwab = new SchwabSourceOptions { Enabled = true },
                    AlphaVantage = new AlphaVantageSourceOptions { Enabled = true, ApiKey = "real-key" },
                    Nasdaq = new NasdaqSourceOptions { Enabled = true },
                },
            },
        };

        ReferenceDataStartupGuard.Validate(Env("Production"), opts, NullLogger.Instance);
    }

    [Fact]
    public void Validate_Production_RealSource_WithoutAlphaVantage_Throws_WhenOverrideFalse()
    {
        // Anchored path (StockAnalysis on), AlphaVantage not configured,
        // AllowNasdaqTailOnlyInProduction=false (default). The strict
        // four-source contract fails startup so the Production deploy
        // does not silently narrow to a 3-source posture.
        var opts = new ReferenceDataOptions
        {
            Basket = new BasketOptions
            {
                Mode = BasketMode.RealSource,
                Sources = new BasketSourcesOptions
                {
                    StockAnalysis = new StockAnalysisSourceOptions { Enabled = true },
                    AlphaVantage = new AlphaVantageSourceOptions { Enabled = false },
                    Nasdaq = new NasdaqSourceOptions { Enabled = true },
                },
            },
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => ReferenceDataStartupGuard.Validate(Env("Production"), opts, NullLogger.Instance));
        Assert.Contains("AlphaVantage", ex.Message);
        Assert.Contains("AllowNasdaqTailOnlyInProduction", ex.Message);
    }

    [Fact]
    public void Validate_Production_RealSource_WithoutAlphaVantage_Allowed_WhenOverrideTrue()
    {
        // Explicit operator override: accept the degraded anchor +
        // Nasdaq-tail-only Production posture. Guard must not throw;
        // Program.cs emits a loud DEGRADED warning through the logger.
        var opts = new ReferenceDataOptions
        {
            Basket = new BasketOptions
            {
                Mode = BasketMode.RealSource,
                AllowNasdaqTailOnlyInProduction = true,
                Sources = new BasketSourcesOptions
                {
                    StockAnalysis = new StockAnalysisSourceOptions { Enabled = true },
                    AlphaVantage = new AlphaVantageSourceOptions { Enabled = false },
                    Nasdaq = new NasdaqSourceOptions { Enabled = true },
                },
            },
        };

        ReferenceDataStartupGuard.Validate(Env("Production"), opts, NullLogger.Instance);
    }

    [Fact]
    public void Validate_Production_RealSource_AlphaVantageEnabled_ButPlaceholderKey_TreatedAsMissing()
    {
        // Matches the guard's own placeholder filter (`YOUR_` prefix).
        // Placeholder keys must be treated as "not configured" so a
        // deploy that forgot to inject the real secret fails fast
        // instead of silently running with an unusable AlphaVantage
        // adapter.
        var opts = new ReferenceDataOptions
        {
            Basket = new BasketOptions
            {
                Mode = BasketMode.RealSource,
                Sources = new BasketSourcesOptions
                {
                    StockAnalysis = new StockAnalysisSourceOptions { Enabled = true },
                    AlphaVantage = new AlphaVantageSourceOptions
                    {
                        Enabled = true,
                        ApiKey = "YOUR_ALPHAVANTAGE_KEY",
                    },
                    Nasdaq = new NasdaqSourceOptions { Enabled = true },
                },
            },
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => ReferenceDataStartupGuard.Validate(Env("Production"), opts, NullLogger.Instance));
        Assert.Contains("AlphaVantage", ex.Message);
    }

    [Fact]
    public void Validate_Production_RealSource_NoAnchorScrapers_DefaultsThrow()
    {
        // Default posture in Production: RequireAnchorInProduction=true.
        // Neither StockAnalysis nor Schwab is enabled → startup must
        // fail so we never publish an anchor-less / zero-shares basket
        // as the "normal" Production path.
        var opts = new ReferenceDataOptions
        {
            Basket = new BasketOptions
            {
                Mode = BasketMode.RealSource,
                Sources = new BasketSourcesOptions
                {
                    StockAnalysis = new StockAnalysisSourceOptions { Enabled = false },
                    Schwab = new SchwabSourceOptions { Enabled = false },
                    AlphaVantage = new AlphaVantageSourceOptions { Enabled = false },
                    Nasdaq = new NasdaqSourceOptions { Enabled = true },
                },
            },
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => ReferenceDataStartupGuard.Validate(Env("Production"), opts, NullLogger.Instance));
        Assert.Contains("RequireAnchorInProduction", ex.Message);
    }

    [Fact]
    public void Validate_Production_RealSource_NoAnchorScrapers_AnchorlessProxyOptIn_Allowed()
    {
        // Explicit operator opt-in: accept the anchor-less proxy posture
        // in Production. The emitted basket carries SharesHeld=0 on
        // every row, which the startup log surfaces loudly as DEGRADED.
        var opts = new ReferenceDataOptions
        {
            Basket = new BasketOptions
            {
                Mode = BasketMode.RealSource,
                AllowAnchorlessProxyInProduction = true,
                Sources = new BasketSourcesOptions
                {
                    Nasdaq = new NasdaqSourceOptions { Enabled = true },
                },
            },
        };

        ReferenceDataStartupGuard.Validate(Env("Production"), opts, NullLogger.Instance);
    }

    [Fact]
    public void ValidateCorporateActions_Production_FileOnly_WithoutOverride_Throws()
    {
        var opts = new ReferenceDataOptions
        {
            CorporateActions = new CorporateActionOptions
            {
                Tiingo = new TiingoCorporateActionOptions { Enabled = false },
                AllowOfflineOnlyInProduction = false,
            },
        };

        Assert.Throws<InvalidOperationException>(
            () => ReferenceDataStartupGuard.ValidateCorporateActions(Env("Production"), opts, NullLogger.Instance));
    }

    [Fact]
    public void ValidateCorporateActions_Production_FileOnly_WithOverride_Allowed()
    {
        var opts = new ReferenceDataOptions
        {
            CorporateActions = new CorporateActionOptions
            {
                Tiingo = new TiingoCorporateActionOptions { Enabled = false },
                AllowOfflineOnlyInProduction = true,
            },
        };

        ReferenceDataStartupGuard.ValidateCorporateActions(Env("Production"), opts, NullLogger.Instance);
    }

    private static IWebHostEnvironment Env(string name) => new StubEnv(name);

    private sealed class StubEnv : IWebHostEnvironment
    {
        public StubEnv(string name) { EnvironmentName = name; }
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "hqqq-reference-data-tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    }
}
