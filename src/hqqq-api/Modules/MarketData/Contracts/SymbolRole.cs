namespace Hqqq.Api.Modules.MarketData.Contracts;

/// <summary>
/// Tracks whether a symbol belongs to the active basket, pending basket,
/// or is a reference instrument (e.g. QQQ). Flags are combinable.
/// </summary>
[Flags]
public enum SymbolRole
{
    None = 0,
    Active = 1,
    Pending = 2,
    Reference = 4,
}
