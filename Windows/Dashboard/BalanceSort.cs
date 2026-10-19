namespace GilDelta.Windows.Dashboard;

/// <summary>
/// How the balance tab orders its rows. Persisted by number; 0 is the original order, so a
/// config file without the key, or with a value this build does not know, gets that.
/// </summary>
public enum BalanceSort
{
    Default = 0,
    AmountDescending = 1,
    AmountAscending = 2,
}

public static class BalanceSortCycle
{
    /// <summary>
    /// What one click on the balance header does: most first, then least first, then back.
    /// An unknown value is shown as the original order, so it moves on like one.
    /// </summary>
    public static BalanceSort Next(this BalanceSort sort) => sort switch
    {
        BalanceSort.AmountDescending => BalanceSort.AmountAscending,
        BalanceSort.AmountAscending  => BalanceSort.Default,
        _                            => BalanceSort.AmountDescending,
    };
}
