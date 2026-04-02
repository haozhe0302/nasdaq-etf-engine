namespace Hqqq.Api.Modules.Basket.Contracts;

/// <summary>
/// Holds the dual-basket state that prevents artificial iNAV jumps when
/// holdings are refreshed outside regular market hours.
/// <para>
///   <b>Active</b>: the basket currently used by pricing.<br/>
///   <b>Pending</b>: a newly fetched basket waiting to become active at
///   the next regular market open.
/// </para>
/// </summary>
public sealed class BasketState
{
    public BasketSnapshot? Active { get; set; }
    public BasketSnapshot? Pending { get; set; }

    /// <summary>
    /// The UTC instant when <see cref="Pending"/> should replace <see cref="Active"/>.
    /// Null when there is no pending basket.
    /// </summary>
    public DateTimeOffset? PendingEffectiveAtUtc { get; set; }

    /// <summary>Last error encountered during refresh, if any.</summary>
    public string? LastError { get; set; }
}
