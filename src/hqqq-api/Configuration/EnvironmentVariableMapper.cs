namespace Hqqq.Api.Configuration;

/// <summary>
/// Maps flat environment variable names (e.g. TIINGO_API_KEY) to hierarchical
/// configuration keys (e.g. Tiingo:ApiKey) so that <c>IOptions&lt;T&gt;</c>
/// binding works with <c>.env</c>-style variable names.
/// </summary>
public static class EnvironmentVariableMapper
{
    private static readonly (string EnvVar, string ConfigKey)[] Mappings =
    [
        ("TIINGO_API_KEY",                     "Tiingo:ApiKey"),
        ("TIINGO_WS_URL",                      "Tiingo:WebSocketUrl"),
        ("TIINGO_REST_BASE_URL",               "Tiingo:RestBaseUrl"),
        ("TIINGO_REST_POLLING_INTERVAL_SECONDS","Tiingo:RestPollingIntervalSeconds"),
        ("TIINGO_RECONNECT_BASE_DELAY_SECONDS","Tiingo:ReconnectBaseDelaySeconds"),
        ("TIINGO_STALE_AFTER_SECONDS",         "Tiingo:StaleAfterSeconds"),

        ("HQQQ_BASKET_HOLDINGS_URL",           "Basket:HoldingsSourceUrl"),
        ("HQQQ_BASKET_REFRESH_TIME",           "Basket:RefreshTimeLocal"),
        ("HQQQ_BASKET_CACHE_FILE",             "Basket:CacheFilePath"),
        ("HQQQ_BASKET_RAW_CACHE_DIR",          "Basket:RawCacheDir"),
        ("HQQQ_BASKET_MERGED_HISTORY_DIR",     "Basket:MergedHistoryDir"),
        ("ALPHA_VANTAGE_API_KEY",              "Basket:AlphaVantageApiKey"),
        ("ALPHA_VANTAGE_BASE_URL",             "Basket:AlphaVantageBaseUrl"),

        ("HQQQ_SCALE_STATE_FILE",              "Pricing:ScaleStateFilePath"),
        ("HQQQ_SERIES_FILE",                   "Pricing:SeriesFilePath"),
        ("HQQQ_QUOTE_BROADCAST_INTERVAL_MS",   "Pricing:QuoteBroadcastIntervalMs"),
        ("HQQQ_SERIES_CAPACITY",               "Pricing:SeriesCapacity"),
        ("HQQQ_MARKET_TIME_ZONE",              "Pricing:MarketTimeZone"),

        ("HQQQ_ENABLE_LIVE_MODE",              "Feature:EnableLiveMode"),
        ("HQQQ_ENABLE_MOCK_FALLBACK",          "Feature:EnableMockFallback"),
    ];

    public static IConfigurationBuilder AddFlatEnvironmentVariables(
        this IConfigurationBuilder builder)
    {
        var mapped = new List<KeyValuePair<string, string?>>();

        foreach (var (envVar, configKey) in Mappings)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (value is not null)
                mapped.Add(new(configKey, value));
        }

        return builder.AddInMemoryCollection(mapped);
    }
}
