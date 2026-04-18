using Microsoft.Extensions.Options;

namespace Hqqq.Analytics.Options;

/// <summary>
/// Validates <see cref="AnalyticsOptions"/> at startup so the host fails fast
/// when the operator forgot to supply a report window. Validation is scoped
/// to the only mode implemented in C4 (<see cref="AnalyticsOptions.ReportMode"/>);
/// unknown modes are rejected by the dispatcher rather than here so the
/// validation error surface stays small and predictable.
/// </summary>
public sealed class AnalyticsOptionsValidator : IValidateOptions<AnalyticsOptions>
{
    public ValidateOptionsResult Validate(string? name, AnalyticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Mode))
            failures.Add("Analytics:Mode is required.");

        if (string.IsNullOrWhiteSpace(options.BasketId))
            failures.Add("Analytics:BasketId is required.");

        if (options.MaxRows <= 0)
            failures.Add("Analytics:MaxRows must be > 0.");

        if (options.TopGapCount < 0)
            failures.Add("Analytics:TopGapCount must be >= 0.");

        if (string.Equals(options.Mode, AnalyticsOptions.ReportMode, StringComparison.OrdinalIgnoreCase))
        {
            if (options.StartUtc is null)
                failures.Add("Analytics:StartUtc is required when Mode=report.");
            if (options.EndUtc is null)
                failures.Add("Analytics:EndUtc is required when Mode=report.");
            if (options.StartUtc is not null && options.EndUtc is not null
                && options.StartUtc.Value >= options.EndUtc.Value)
            {
                failures.Add("Analytics:StartUtc must be strictly less than Analytics:EndUtc.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
