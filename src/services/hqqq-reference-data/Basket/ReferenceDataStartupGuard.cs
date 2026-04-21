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

        var stockAnalysisEnabled = basket.Sources.StockAnalysis.Enabled;
        var schwabEnabled = basket.Sources.Schwab.Enabled;
        var alphaEnabled = basket.Sources.AlphaVantage.Enabled
            && !string.IsNullOrWhiteSpace(basket.Sources.AlphaVantage.ApiKey)
            && !basket.Sources.AlphaVantage.ApiKey.Contains("YOUR_", StringComparison.OrdinalIgnoreCase);
        var nasdaqEnabled = basket.Sources.Nasdaq.Enabled;

        if (!stockAnalysisEnabled && !schwabEnabled && !alphaEnabled && !nasdaqEnabled)
        {
            throw new InvalidOperationException(
                "ReferenceData:Basket:Mode=RealSource but every upstream adapter is disabled. " +
                "Enable at least one of ReferenceData:Basket:Sources:StockAnalysis, Schwab, " +
                "AlphaVantage, or Nasdaq, or switch to Mode=Seed with " +
                "AllowDeterministicSeedInProduction=true if you intend to run offline.");
        }

        // The standard Production path is the full four-source anchored
        // pipeline (StockAnalysis or Schwab as anchor → AlphaVantage or
        // Nasdaq as tail). If no anchor source is enabled and the
        // operator has not explicitly opted in to the anchor-less proxy
        // posture, fail startup so we never publish an all-zero-shares
        // basket into Production by default.
        if (basket.RequireAnchorInProduction
            && !stockAnalysisEnabled
            && !schwabEnabled
            && !basket.AllowAnchorlessProxyInProduction)
        {
            throw new InvalidOperationException(
                "ReferenceData:Basket:RequireAnchorInProduction=true but neither " +
                "ReferenceData:Basket:Sources:StockAnalysis nor " +
                "ReferenceData:Basket:Sources:Schwab is enabled. " +
                "Enable at least one anchor adapter, or explicitly opt in to the " +
                "anchor-less proxy posture with " +
                "ReferenceData:Basket:AllowAnchorlessProxyInProduction=true " +
                "(the resulting basket will carry SharesHeld=0 on every row).");
        }

        if (!basket.RequireAnchorInProduction || basket.AllowAnchorlessProxyInProduction)
        {
            logger.LogWarning(
                "ReferenceDataStartupGuard: Production basket posture is DEGRADED — requireAnchor={RequireAnchor} allowAnchorlessProxy={AllowProxy}. Active baskets may be anchor-less / zero-shares.",
                basket.RequireAnchorInProduction, basket.AllowAnchorlessProxyInProduction);
        }

        // The standard `with-ingress` Azure Production contract is the
        // full four-source anchored pipeline: StockAnalysis/Schwab
        // anchor + AlphaVantage authoritative tail + Nasdaq universe
        // guardrail. When we are on the anchored path (at least one
        // anchor scraper is enabled AND anchor-less proxy has not been
        // opted into), AlphaVantage is required unless the operator
        // has explicitly acknowledged the narrower Nasdaq-tail-only
        // posture via AllowNasdaqTailOnlyInProduction=true. This
        // prevents a Production deploy from silently drifting into a
        // 3-source (anchor + Nasdaq only) contract.
        var onAnchoredPath = (stockAnalysisEnabled || schwabEnabled)
            && !basket.AllowAnchorlessProxyInProduction;

        if (onAnchoredPath && !alphaEnabled)
        {
            if (!basket.AllowNasdaqTailOnlyInProduction)
            {
                throw new InvalidOperationException(
                    "ReferenceData:Basket standard Production posture requires AlphaVantage to be " +
                    "configured as the authoritative tail (the four-source anchored pipeline: " +
                    "StockAnalysis/Schwab anchor + AlphaVantage tail + Nasdaq guardrail). " +
                    "Either set ReferenceData:Basket:Sources:AlphaVantage:Enabled=true with a " +
                    "real ReferenceData:Basket:Sources:AlphaVantage:ApiKey, OR explicitly " +
                    "opt in to the degraded anchor+Nasdaq-tail-only posture by setting " +
                    "ReferenceData:Basket:AllowNasdaqTailOnlyInProduction=true " +
                    "(re-run the deploy workflow with allow_nasdaq_tail_only=true — the " +
                    "override is mirrored end-to-end through bicep, the startup guard, and " +
                    "phase2-azure-smoke.sh).");
            }

            logger.LogWarning(
                "ReferenceDataStartupGuard: Production posture is DEGRADED (Nasdaq-tail-only) — " +
                "AllowNasdaqTailOnlyInProduction=true is an explicit operator override. " +
                "Standard Production is the four-source anchored pipeline; the merged basket " +
                "will fall back to the Nasdaq tail with no AlphaVantage weights.");
        }

        logger.LogInformation(
            "ReferenceDataStartupGuard: Production posture OK — stockanalysis={SA} schwab={SC} alphavantage={Alpha} nasdaq={Nasdaq} requireAnchor={RequireAnchor} allowNasdaqTailOnly={AllowTailOnly}",
            stockAnalysisEnabled, schwabEnabled, alphaEnabled, nasdaqEnabled, basket.RequireAnchorInProduction, basket.AllowNasdaqTailOnlyInProduction);
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
