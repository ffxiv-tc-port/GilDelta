using System;
using System.Collections.Generic;
using GilDelta.Wallet;

namespace GilDelta.Windows.Dashboard;

/// <summary>
/// The balance tab's rows for one wallet list, ordered, both with and without the wallets
/// read as 0. Both halves are built together, so flipping "hide zero balances" needs no
/// rebuild and cannot show a list left over from the other setting. The same goes for the
/// sort: each order is kept per set, so a click can never meet one built for older rows.
/// </summary>
public sealed class BalanceRowSet
{
    public static readonly BalanceRowSet Empty =
        new(Array.Empty<WalletOrigin>(), Array.Empty<WalletOrigin>());

    /// <summary>Every row. Totals, the unreadable count and the character column are taken from this.</summary>
    public IReadOnlyList<WalletOrigin> All { get; }

    /// <summary><see cref="All"/> minus the zero rows, same order; the same instance when none are zero.</summary>
    public IReadOnlyList<WalletOrigin> NonZero { get; }

    public int ZeroCount => All.Count - NonZero.Count;

    /// <summary>Distinct characters across <see cref="All"/>, hidden rows included.</summary>
    public int CharacterCount { get; }

    private BalanceRowSet(IReadOnlyList<WalletOrigin> all, IReadOnlyList<WalletOrigin> nonZero)
    {
        All = all;
        NonZero = nonZero;
        CharacterCount = BalanceRows.CharacterCount(all);
    }

    public static BalanceRowSet From(IReadOnlyList<WalletOrigin> origins)
    {
        if (origins.Count == 0) return Empty;
        var all = BalanceRows.Build(origins);
        return new BalanceRowSet(all, BalanceRows.WithoutZero(all));
    }

    public IReadOnlyList<WalletOrigin> Shown(bool hideZero) => hideZero ? NonZero : All;

    // The by-amount orders, built on first use and kept for this set: [hide * 2 + ascending].
    private readonly List<WalletOrigin>?[] _sorted = new List<WalletOrigin>?[4];

    /// <summary>The rows the table draws. <see cref="BalanceSort.Default"/> is the order of <see cref="All"/>.</summary>
    public IReadOnlyList<WalletOrigin> Shown(bool hideZero, BalanceSort sort)
    {
        var rows = Shown(hideZero);
        var ascending = sort switch
        {
            BalanceSort.AmountDescending => false,
            BalanceSort.AmountAscending  => true,
            _                            => (bool?)null,
        };
        if (ascending is not { } asc) return rows;

        var slot = (hideZero ? 2 : 0) + (asc ? 1 : 0);
        return _sorted[slot] ??= BalanceRows.SortByAmount(rows, descending: !asc);
    }
}

/// <summary>
/// Remembers the <see cref="BalanceRowSet"/> built for the last wallet list. Keyed on the
/// list instance: the plugin hands a context a new list whenever a wallet changes, and never
/// edits one in place afterwards. Draw thread only.
/// </summary>
public sealed class BalanceRowCache
{
    private IReadOnlyList<WalletOrigin>? _builtFrom;
    private BalanceRowSet _set = BalanceRowSet.Empty;

    public BalanceRowSet For(IReadOnlyList<WalletOrigin> origins)
    {
        if (ReferenceEquals(origins, _builtFrom)) return _set;

        _set = BalanceRowSet.From(origins);
        _builtFrom = origins;
        return _set;
    }
}
