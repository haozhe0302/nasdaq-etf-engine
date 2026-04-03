using Hqqq.Api.Modules.MarketData.Contracts;

namespace Hqqq.Api.Modules.MarketData.Services;

/// <summary>
/// Maintains the union of active, pending, and reference symbols with role metadata.
/// Thread-safe for concurrent reads via immutable snapshot swaps.
/// </summary>
public sealed class SubscriptionManager
{
    private volatile IReadOnlyDictionary<string, SymbolRole> _symbolRoles =
        new Dictionary<string, SymbolRole>(StringComparer.OrdinalIgnoreCase);

    private volatile string _fingerprint = "";

    /// <summary>
    /// Recomputes the tracked symbol set from the current active and pending baskets.
    /// QQQ is always included as a reference symbol.
    /// </summary>
    public void UpdateFromBasketState(
        IEnumerable<string>? activeSymbols,
        IEnumerable<string>? pendingSymbols)
    {
        var roles = new Dictionary<string, SymbolRole>(StringComparer.OrdinalIgnoreCase);

        if (activeSymbols is not null)
        {
            foreach (var s in activeSymbols)
            {
                if (!string.IsNullOrWhiteSpace(s))
                    roles[s] = SymbolRole.Active;
            }
        }

        if (pendingSymbols is not null)
        {
            foreach (var s in pendingSymbols)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                roles[s] = roles.TryGetValue(s, out var existing)
                    ? existing | SymbolRole.Pending
                    : SymbolRole.Pending;
            }
        }

        roles["QQQ"] = roles.TryGetValue("QQQ", out var qqRole)
            ? qqRole | SymbolRole.Reference
            : SymbolRole.Reference;

        var fp = string.Join(",",
            roles.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));

        // Write roles before fingerprint so readers that see the new fingerprint
        // are guaranteed to also see the new roles (volatile release semantics).
        _symbolRoles = roles;
        _fingerprint = fp;
    }

    public IReadOnlyDictionary<string, SymbolRole> GetSymbolRoles() => _symbolRoles;

    public IReadOnlySet<string> GetAllSymbols() =>
        _symbolRoles.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public string GetFingerprint() => _fingerprint;
}
