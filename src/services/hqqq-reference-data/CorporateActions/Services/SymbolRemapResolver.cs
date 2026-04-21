using Hqqq.ReferenceData.CorporateActions.Contracts;

namespace Hqqq.ReferenceData.CorporateActions.Services;

/// <summary>
/// Pure, stateless resolver that flattens a list of
/// <see cref="SymbolRenameEvent"/>s into a canonical <c>old → new</c>
/// lookup map across a lookup window. Handles chained renames
/// (<c>A→B</c> then <c>B→C</c>) by following the chain to the terminal
/// symbol.
/// </summary>
/// <remarks>
/// The resolver does not touch the monolith; it is a pure function over
/// event rows. Used by <see cref="CorporateActionAdjustmentService"/>
/// before it rewrites constituent symbols.
/// </remarks>
public static class SymbolRemapResolver
{
    /// <summary>
    /// Builds the canonical <c>old → new</c> lookup for the window
    /// <c>(snapshotAsOf, runtimeDate]</c>. Events outside the window are
    /// ignored. The returned map is keyed by the <em>original</em>
    /// (oldest) symbol; callers can look up once per constituent.
    /// </summary>
    /// <param name="renames">All rename events retrieved from the provider.</param>
    /// <param name="snapshotAsOf">Exclusive lower bound of the adjustment window.</param>
    /// <param name="runtimeDate">Inclusive upper bound of the adjustment window.</param>
    public static CanonicalRemap Build(
        IEnumerable<SymbolRenameEvent> renames,
        DateOnly snapshotAsOf,
        DateOnly runtimeDate)
    {
        ArgumentNullException.ThrowIfNull(renames);

        // Only events strictly after the basket as-of date contribute.
        var inWindow = renames
            .Where(r => r.EffectiveDate > snapshotAsOf && r.EffectiveDate <= runtimeDate)
            .Select(r => new SymbolRenameEvent
            {
                OldSymbol = r.OldSymbol.Trim().ToUpperInvariant(),
                NewSymbol = r.NewSymbol.Trim().ToUpperInvariant(),
                EffectiveDate = r.EffectiveDate,
                Description = r.Description,
                Source = r.Source,
            })
            .Where(r => !string.IsNullOrEmpty(r.OldSymbol)
                && !string.IsNullOrEmpty(r.NewSymbol)
                && !string.Equals(r.OldSymbol, r.NewSymbol, StringComparison.Ordinal))
            .OrderBy(r => r.EffectiveDate)
            .ToList();

        // Per-oldSymbol history (preserving order); the resolver follows
        // the chain from each old key and collapses to the terminal
        // symbol, collecting the contributing events along the way.
        var initialMap = new Dictionary<string, List<SymbolRenameEvent>>(StringComparer.Ordinal);
        foreach (var r in inWindow)
        {
            if (!initialMap.TryGetValue(r.OldSymbol, out var list))
            {
                list = new List<SymbolRenameEvent>();
                initialMap[r.OldSymbol] = list;
            }
            list.Add(r);
        }

        var map = new Dictionary<string, Resolved>(StringComparer.Ordinal);

        foreach (var startSymbol in initialMap.Keys)
        {
            if (map.ContainsKey(startSymbol)) continue;

            var chain = new List<SymbolRenameEvent>();
            var current = startSymbol;
            var visited = new HashSet<string>(StringComparer.Ordinal) { current };

            while (initialMap.TryGetValue(current, out var hops))
            {
                // Follow the earliest hop out of the current symbol that
                // lands somewhere new. Later hops contribute but don't
                // drive the walk.
                var next = hops[0];
                chain.Add(next);
                current = next.NewSymbol;
                if (!visited.Add(current))
                {
                    // Cycle detected — stop (defensive; shouldn't happen
                    // in honest corp-action feeds).
                    break;
                }
            }

            map[startSymbol] = new Resolved(TerminalSymbol: current, AppliedRenames: chain);
        }

        return new CanonicalRemap(map);
    }
}

/// <summary>Canonical old→new rename map over a fixed adjustment window.</summary>
public sealed class CanonicalRemap
{
    private readonly IReadOnlyDictionary<string, Resolved> _map;

    internal CanonicalRemap(IReadOnlyDictionary<string, Resolved> map)
    {
        _map = map;
    }

    /// <summary>True when no rename events applied in the lookup window.</summary>
    public bool IsEmpty => _map.Count == 0;

    /// <summary>
    /// Attempts to resolve <paramref name="currentSymbol"/> to its
    /// terminal symbol (after following all chained renames in the
    /// window). Returns <c>false</c> when the symbol is not renamed.
    /// </summary>
    public bool TryResolve(string currentSymbol, out string terminalSymbol, out IReadOnlyList<SymbolRenameEvent> appliedRenames)
    {
        if (_map.TryGetValue(currentSymbol.Trim().ToUpperInvariant(), out var resolved))
        {
            terminalSymbol = resolved.TerminalSymbol;
            appliedRenames = resolved.AppliedRenames;
            return true;
        }

        terminalSymbol = currentSymbol;
        appliedRenames = Array.Empty<SymbolRenameEvent>();
        return false;
    }

    /// <summary>Returns all old→terminal mappings in the window.</summary>
    public IEnumerable<(string OldSymbol, string TerminalSymbol, IReadOnlyList<SymbolRenameEvent> AppliedRenames)> Entries
        => _map.Select(kv => (kv.Key, kv.Value.TerminalSymbol, kv.Value.AppliedRenames));
}

internal readonly record struct Resolved(string TerminalSymbol, IReadOnlyList<SymbolRenameEvent> AppliedRenames);
