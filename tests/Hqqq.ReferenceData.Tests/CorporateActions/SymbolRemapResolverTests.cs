using Hqqq.ReferenceData.CorporateActions.Contracts;
using Hqqq.ReferenceData.CorporateActions.Services;

namespace Hqqq.ReferenceData.Tests.CorporateActions;

public class SymbolRemapResolverTests
{
    [Fact]
    public void Build_FiltersOutEventsAtOrBeforeAsOfDate()
    {
        var asOf = new DateOnly(2026, 4, 15);
        var runtime = new DateOnly(2026, 4, 20);

        var remap = SymbolRemapResolver.Build(new[]
        {
            Rename("OLD", "NEW", asOf),          // == asOf → excluded (window is exclusive-lower)
            Rename("AAA", "BBB", asOf.AddDays(1)), // in-window
            Rename("ZZZ", "YYY", runtime.AddDays(1)), // after runtime → excluded
        }, asOf, runtime);

        Assert.False(remap.IsEmpty);
        Assert.True(remap.TryResolve("AAA", out var aaa, out _));
        Assert.Equal("BBB", aaa);
        Assert.False(remap.TryResolve("OLD", out _, out _));
        Assert.False(remap.TryResolve("ZZZ", out _, out _));
    }

    [Fact]
    public void Build_HandlesChainedRenames()
    {
        var asOf = new DateOnly(2026, 4, 15);
        var runtime = new DateOnly(2026, 4, 20);

        var remap = SymbolRemapResolver.Build(new[]
        {
            Rename("A", "B", asOf.AddDays(1)),
            Rename("B", "C", asOf.AddDays(2)),
        }, asOf, runtime);

        Assert.True(remap.TryResolve("A", out var a, out var applied));
        Assert.Equal("C", a);
        Assert.Equal(2, applied.Count);
    }

    [Fact]
    public void Build_EmptyInput_ReturnsEmpty()
    {
        var remap = SymbolRemapResolver.Build(
            Array.Empty<SymbolRenameEvent>(),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31));

        Assert.True(remap.IsEmpty);
        Assert.False(remap.TryResolve("ANY", out _, out _));
    }

    [Fact]
    public void Build_IgnoresSelfRename()
    {
        var remap = SymbolRemapResolver.Build(new[]
        {
            Rename("X", "X", new DateOnly(2026, 4, 18)),
        }, new DateOnly(2026, 4, 15), new DateOnly(2026, 4, 20));

        Assert.True(remap.IsEmpty);
    }

    private static SymbolRenameEvent Rename(string old, string @new, DateOnly date) => new()
    {
        OldSymbol = old,
        NewSymbol = @new,
        EffectiveDate = date,
        Source = "test",
    };
}
