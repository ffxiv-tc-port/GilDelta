using System.Collections.Generic;
using GilDelta.Wallet;

namespace GilDelta.Events;

/// <summary>
/// Drops the free company chest out of what the views count. Nothing is deleted and
/// nothing stops being recorded: this is a display filter, so it can be turned back
/// off. 🔴 It keys on the <b>wallet kind</b>, not on the category: chest money reaches
/// the log under three different categories, so a category-based filter would leak.
/// </summary>
public static class WalletExclusion
{
    public static bool IsExcluded(WalletKind kind, bool excludeFreeCompanyChest) =>
        excludeFreeCompanyChest && kind == WalletKind.FreeCompanyChest;

    /// <summary>
    /// Filters an event list. Returns the very same instance when there is nothing to
    /// drop, so the default (exclusion off) stays allocation-free.
    /// </summary>
    public static IReadOnlyList<GilEvent> Events(
        IReadOnlyList<GilEvent> events, bool excludeFreeCompanyChest)
    {
        if (!excludeFreeCompanyChest) return events;

        var keep = new List<GilEvent>(events.Count);
        var dropped = false;
        foreach (var ev in events)
        {
            if (ev.Wallet.Kind == WalletKind.FreeCompanyChest) { dropped = true; continue; }
            keep.Add(ev);
        }

        return dropped ? keep : events;
    }

    /// <summary>Filters a wallet list. Same "return the original when nothing changed" rule.</summary>
    public static IReadOnlyList<Wallet.Wallet> Wallets(
        IReadOnlyList<Wallet.Wallet> wallets, bool excludeFreeCompanyChest)
    {
        if (!excludeFreeCompanyChest) return wallets;

        var keep = new List<Wallet.Wallet>(wallets.Count);
        var dropped = false;
        foreach (var w in wallets)
        {
            if (w.Id.Kind == WalletKind.FreeCompanyChest) { dropped = true; continue; }
            keep.Add(w);
        }

        return dropped ? keep : wallets;
    }

    /// <summary>
    /// Filters the attributed form of a wallet list. Same rule, same "return the
    /// original instance when nothing changed".
    /// 🔴 An overload rather than a caller that filters the flat list and then looks
    /// the origins up again: the breakdown has to contain exactly the rows the total
    /// was summed over.
    /// </summary>
    public static IReadOnlyList<WalletOrigin> Origins(
        IReadOnlyList<WalletOrigin> origins, bool excludeFreeCompanyChest)
    {
        if (!excludeFreeCompanyChest) return origins;

        // Counted first so "nothing to drop" allocates nothing and the copy is sized exactly.
        var chests = 0;
        for (var i = 0; i < origins.Count; i++)
            if (origins[i].Wallet.Id.Kind == WalletKind.FreeCompanyChest) chests++;
        if (chests == 0) return origins;

        var keep = new List<WalletOrigin>(origins.Count - chests);
        for (var i = 0; i < origins.Count; i++)
        {
            var o = origins[i];
            if (o.Wallet.Id.Kind != WalletKind.FreeCompanyChest) keep.Add(o);
        }

        return keep;
    }

    /// <summary>Sum of a wallet list with the free company chest left out.</summary>
    /// <remarks>
    /// By index, like <see cref="Total"/>: both run every tick, and enumerating a list
    /// through its interface boxes an enumerator each time.
    /// </remarks>
    public static long TotalExcludingFreeCompany(IReadOnlyList<Wallet.Wallet> wallets)
    {
        long total = 0;
        for (var i = 0; i < wallets.Count; i++)
        {
            var w = wallets[i];
            if (w.Id.Kind != WalletKind.FreeCompanyChest && w.Amount is { } amount)
                total += amount;
        }
        return total;
    }

    /// <summary>
    /// Sum of a wallet list, everything included.
    /// A wallet we could not read contributes nothing, so this is a <em>lower bound</em>
    /// when <see cref="HasUnknown"/> is true.
    /// </summary>
    public static long Total(IReadOnlyList<Wallet.Wallet> wallets)
    {
        long total = 0;
        for (var i = 0; i < wallets.Count; i++)
            if (wallets[i].Amount is { } amount) total += amount;
        return total;
    }

    /// <summary>True when at least one of these wallets could not be read at all.</summary>
    public static bool HasUnknown(IReadOnlyList<Wallet.Wallet> wallets)
    {
        foreach (var w in wallets)
            if (!w.IsKnown) return true;
        return false;
    }
}
