using Hqqq.QuoteEngine.Models;

namespace Hqqq.QuoteEngine.State;

/// <summary>
/// Holds the current active basket. Single-writer semantics: the engine
/// replaces the reference atomically on activation events. Readers see a
/// consistent immutable <see cref="ActiveBasket"/> snapshot.
/// </summary>
public sealed class BasketStateStore
{
    private ActiveBasket? _current;
    private readonly object _sync = new();

    public ActiveBasket? Current
    {
        get
        {
            lock (_sync) return _current;
        }
    }

    public void Replace(ActiveBasket basket)
    {
        lock (_sync)
        {
            _current = basket;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _current = null;
        }
    }
}
