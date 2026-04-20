using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hqqq.Infrastructure.Hosting;

/// <summary>
/// DI registration for <see cref="OperatingModeOptions"/>. Binds the value
/// from configuration with a tolerant scalar/section parser (so both
/// <c>OperatingMode=standalone</c> and <c>OperatingMode:Mode=standalone</c>
/// work) and registers a singleton <see cref="OperatingModeOptions"/>
/// snapshot for callsites that just want the resolved enum without
/// dragging in <see cref="IOptions{TOptions}"/>.
/// </summary>
public static class OperatingModeRegistration
{
    /// <summary>
    /// Binds <see cref="OperatingModeOptions"/> and exposes it as a
    /// singleton. Logs the resolved mode at <see cref="LogLevel.Information"/>.
    /// </summary>
    public static IServiceCollection AddHqqqOperatingMode(
        this IServiceCollection services,
        IConfiguration configuration,
        ILogger? logger = null)
    {
        var resolved = ResolveMode(configuration);

        services.Configure<OperatingModeOptions>(opts => opts.Mode = resolved);
        services.AddSingleton(sp =>
            sp.GetRequiredService<IOptions<OperatingModeOptions>>().Value);

        logger?.LogInformation("HQQQ operating mode resolved: {Mode}", resolved);
        return services;
    }

    /// <summary>
    /// Reads the operating mode from configuration without DI side effects.
    /// Order:
    /// <list type="number">
    ///   <item><c>OperatingMode</c> as a scalar (e.g. set via env <c>OperatingMode=standalone</c>).</item>
    ///   <item><c>OperatingMode:Mode</c> as a section (.NET hierarchical form).</item>
    /// </list>
    /// Unrecognized values fall back to <see cref="OperatingMode.Hybrid"/>.
    /// </summary>
    public static OperatingMode ResolveMode(IConfiguration configuration)
    {
        // Scalar value (preferred for the simple env-var case).
        var scalar = configuration["OperatingMode"];
        if (TryParse(scalar, out var fromScalar))
            return fromScalar;

        // Hierarchical fallback.
        var nested = configuration["OperatingMode:Mode"];
        if (TryParse(nested, out var fromNested))
            return fromNested;

        return OperatingMode.Hybrid;
    }

    private static bool TryParse(string? value, out OperatingMode mode)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse<OperatingMode>(value.Trim(), ignoreCase: true, out var parsed))
        {
            mode = parsed;
            return true;
        }
        mode = OperatingMode.Hybrid;
        return false;
    }
}
