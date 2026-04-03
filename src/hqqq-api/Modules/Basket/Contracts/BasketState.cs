namespace Hqqq.Api.Modules.Basket.Contracts;

public sealed class BasketState
{
    public BasketSnapshot? Active { get; set; }
    public BasketSnapshot? Pending { get; set; }
    public DateTimeOffset? PendingEffectiveAtUtc { get; set; }
    public string? LastError { get; set; }
    public string? ActiveFingerprint { get; set; }
    public string? PendingFingerprint { get; set; }
}
