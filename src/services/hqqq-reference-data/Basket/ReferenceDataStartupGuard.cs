using Hqqq.ReferenceData.Configuration;

namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// Production startup guards for the ported reference-data pipeline.
/// Called from <c>Program.cs</c> after configuration binding and
/// service registration; throws early so the container fails the
/// orchestrator's startup probe instead of silently running on a
/// deterministic seed in Production.
/// </summary>
public static class ReferenceDataStartupGuard
{
    /// <summary>
    /// Fails the process if we are in Production and:
    /// <list type="bullet">
    ///   <item><see cref="BasketOptions.Mode"/>=<see cref="BasketMode.Seed"/>
    ///   without <see cref="BasketOptions.AllowDeterministicSeedInProduction"/>, OR</item>
    ///   <item><see cref="BasketOptions.Mode"/>=<see cref="BasketMode.RealSource"/>
    ///   but every real-source adapter is disabled (effectively seed-only).</item>
    /// </list>
    /// </summary>
    public static void Validate(
        IWebHostEnvironment environment,
        ReferenceDataOptions options,
        ILogger logger)
    {
        if (!environment.IsProduction()) return;

        var basket = options.Basket;

        if (basket.Mode == BasketMode.Seed)
        {
            if (!basket.AllowDeterministicSeedInProduction)
            {
                throw new InvalidOperationException(
                    "ReferenceData:Basket:Mode=Seed is not permitted in Production. " +
                    "Set ReferenceData:Basket:Mode=RealSource (default) or, to accept the risk, " +
                    "set ReferenceData:Basket:AllowDeterministicSeedInProduction=true.");
            }

            logger.LogWarning(
                "ReferenceDataStartupGuard: Production is running on deterministic seed basket — " +
                "AllowDeterministicSeedInProduction=true is an explicit operator override.");
            return;
        }

        var alphaEnabled = basket.Sources.AlphaVantage.Enabled
            && !string.IsNullOrWhiteSpace(basket.Sources.AlphaVantage.ApiKey)
            && !basket.Sources.AlphaVantage.ApiKey.Contains("YOUR_", StringComparison.OrdinalIgnoreCase);
        var nasdaqEnabled = basket.Sources.Nasdaq.Enabled;

        if (!alphaEnabled && !nasdaqEnabled)
        {
            throw new InvalidOperationException(
                "ReferenceData:Basket:Mode=RealSource but every upstream adapter is disabled. " +
                "Enable at least one of ReferenceData:Basket:Sources:AlphaVantage or " +
                "ReferenceData:Basket:Sources:Nasdaq, or switch to Mode=Seed with " +
                "AllowDeterministicSeedInProduction=true if you intend to run offline.");
        }

        logger.LogInformation(
            "ReferenceDataStartupGuard: Production posture OK — alphavantage={Alpha} nasdaq={Nasdaq}",
            alphaEnabled, nasdaqEnabled);
    }

    /// <summary>
    /// Fails the process if we are in Production with only the offline
    /// file-based corporate-action provider and no explicit operator
    /// override.
    /// </summary>
    public static void ValidateCorporateActions(
        IWebHostEnvironment environment,
        ReferenceDataOptions options,
        ILogger logger)
    {
        if (!environment.IsProduction()) return;

        var tiingoEnabled = options.CorporateActions.Tiingo.Enabled
            && !string.IsNullOrWhiteSpace(options.CorporateActions.Tiingo.ApiKey);

        if (tiingoEnabled) return;

        if (!options.CorporateActions.AllowOfflineOnlyInProduction)
        {
            throw new InvalidOperationException(
                "ReferenceData:CorporateActions runs file-only in Production. " +
                "Either enable ReferenceData:CorporateActions:Tiingo with a valid ApiKey, " +
                "or set ReferenceData:CorporateActions:AllowOfflineOnlyInProduction=true " +
                "to accept the risk.");
        }

        logger.LogWarning(
            "ReferenceDataStartupGuard: Production corp-actions running file-only — " +
            "AllowOfflineOnlyInProduction=true is an explicit operator override.");
    }
}
