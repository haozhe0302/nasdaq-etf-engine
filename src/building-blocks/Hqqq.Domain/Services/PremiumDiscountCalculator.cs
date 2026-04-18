namespace Hqqq.Domain.Services;

/// <summary>
/// Pure domain calculation for ETF premium/discount percentage — the sign
/// convention mirrors the legacy monolith (market proxy quoted relative to
/// NAV), so the field renders in the frontend with the sign operators expect:
/// positive ⇒ market trades at a premium to NAV, negative ⇒ discount.
/// </summary>
public static class PremiumDiscountCalculator
{
    /// <summary>
    /// Returns <c>(marketPrice - nav) / nav * 100</c>. Returns 0 if <paramref name="nav"/>
    /// is non-positive, so the caller never has to guard against NaN.
    /// </summary>
    public static decimal Calculate(decimal nav, decimal marketPrice)
    {
        if (nav <= 0m) return 0m;
        return (marketPrice - nav) / nav * 100m;
    }
}
