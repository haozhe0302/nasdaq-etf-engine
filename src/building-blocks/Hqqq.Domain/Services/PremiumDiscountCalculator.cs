namespace Hqqq.Domain.Services;

/// <summary>
/// Pure domain calculation for premium/discount percentage.
/// Used by quote-engine and analytics — no infrastructure dependencies.
/// </summary>
public static class PremiumDiscountCalculator
{
    public static decimal Calculate(decimal nav, decimal marketPrice)
    {
        if (marketPrice <= 0)
            return 0;

        return (nav - marketPrice) / marketPrice * 100;
    }
}
