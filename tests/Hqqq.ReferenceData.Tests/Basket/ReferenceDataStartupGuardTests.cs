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
    public void Validate_Production_RealSource_StockAnalysisAnchorEnabled_Allowed()
    {
        // The standard Production path is the full four-source anchored
        // pipeline. Enabling the StockAnalysis scraper as an anchor
        // satisfies RequireAnchorInProduction=true.
        var opts = new ReferenceDataOptions
        {
            Basket = new BasketOptions
            {
                Mode = BasketMode.RealSource,
                Sources = new BasketSourcesOptions
                {
                    StockAnalysis = new StockAnalysisSourceOptions { Enabled = true },
                    Nasdaq = new NasdaqSourceOptions { Enabled = true },
                },
            },
        };

        ReferenceDataStartupGuard.Validate(Env("Production"), opts, NullLogger.Instance);
    }

    [Fact]
    public void Validate_Production_RealSource_SchwabAnchorEnabled_Allowed()
    {
        // Either anchor scraper alone is enough for RequireAnchor.
        var opts = new ReferenceDataOptions
        {
            Basket = new BasketOptions
            {
                Mode = BasketMode.RealSource,
                Sources = new BasketSourcesOptions
                {
                    Schwab = new SchwabSourceOptions { Enabled = true },
                    Nasdaq = new NasdaqSourceOptions { Enabled = true },
                },
            },
        };

        ReferenceDataStartupGuard.Validate(Env("Production"), opts, NullLogger.Instance);
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
