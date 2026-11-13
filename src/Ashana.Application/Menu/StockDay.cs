using Ashana.Application.State;

namespace Ashana.Application.Menu;

/// <summary>
/// Makes sure daily portions belong to the current service day.
/// Applied at the start of every reservation and by the background worker,
/// so a reset can never wipe out a reservation made for the new day.
/// </summary>
public static class StockDay
{
    /// <summary>
    /// The same state if the stock already belongs to <paramref name="serviceDay"/>;
    /// otherwise a new state where every dish has its planned portions back.
    /// </summary>
    public static CanteenState EnsureCurrent(CanteenState state, DateOnly serviceDay)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Settings.MarkStockReset(serviceDay).Match(
            settings => state
                .With(settings)
                .WithAll(state.MenuItems.Values.Where(i => !i.IsArchived).Select(i => i.ResetDailyStock(serviceDay))),
            () => state);
    }
}
