using System;
using System.Collections.Generic;
using System.Linq;
using GilDelta.Wallet;

namespace GilDelta.Windows.Dashboard;

/// <summary>
/// Puts the wallets behind the total into the order a person reads them in.
/// Pure - no ImGui, no plugin state - so the one property that has to hold can actually
/// be tested: <see cref="Total"/> over these rows is the very number printed above them.
/// It holds by construction rather than by arithmetic luck, because the rows ARE the
/// list the context summed - both <c>WidgetContext</c> and <c>DashboardContext</c>
/// derive their wallets from these very origins; this class only reorders them.
/// </summary>
public static class BalanceRows
{
    /// <summary>
    /// The rows, grouped by character: the character whose wallets were read on this
    /// frame first, then the rest newest reading first. Within a character the order is
    /// self, retainers, free company chest - the order the reader takes them in and the
    /// order the widget's own summary line prints them in.
    /// </summary>
    public static List<WalletOrigin> Build(IReadOnlyList<WalletOrigin> origins)
    {
        var groups = new Dictionary<ulong, Group>();
        foreach (var o in origins)
        {
            groups.TryGetValue(o.ContentId, out var g);
            var newest = g.Newest;
            if (o.At is { } at && (newest is not { } known || at > known)) newest = at;
            groups[o.ContentId] = new Group(g.AnyLive || o.IsLive, newest);
        }

        var rows = new List<WalletOrigin>(origins);
        rows.Sort((a, b) => Compare(a, b, groups));
        return rows;
    }

    /// <summary>
    /// Sum of the rows we actually have a figure for - a wallet that could not be read
    /// contributes nothing, exactly as it does to the figure being explained. So this is
    /// a lower bound whenever <see cref="UnknownCount"/> is above zero, and the view
    /// says so instead of leaving the reader to notice.
    /// </summary>
    public static long Total(IReadOnlyList<WalletOrigin> rows)
    {
        long sum = 0;
        foreach (var row in rows)
            if (row.Wallet.Amount is { } amount) sum += amount;
        return sum;
    }

    /// <summary>How many rows are a "?" rather than a number.</summary>
    public static int UnknownCount(IReadOnlyList<WalletOrigin> rows)
    {
        var count = 0;
        foreach (var row in rows)
            if (!row.Wallet.IsKnown) count++;
        return count;
    }

    /// <summary>How many distinct characters these rows came from.</summary>
    public static int CharacterCount(IReadOnlyList<WalletOrigin> rows)
    {
        var seen = new HashSet<ulong>();
        foreach (var row in rows) seen.Add(row.ContentId);
        return seen.Count;
    }

    /// <summary>
    /// A wallet read as exactly 0, whether this frame or carried over from an earlier reading.
    /// 🔴 An unreadable wallet (null) is NOT zero, so it is never hidden as one.
    /// </summary>
    public static bool IsZero(WalletOrigin row) => row.Wallet.Amount == 0;

    /// <summary>
    /// The rows minus every <see cref="IsZero"/> one, order kept. Returns the very same
    /// instance when there is nothing to take out.
    /// </summary>
    public static IReadOnlyList<WalletOrigin> WithoutZero(IReadOnlyList<WalletOrigin> rows)
    {
        var zeros = 0;
        foreach (var row in rows)
            if (IsZero(row)) zeros++;
        if (zeros == 0) return rows;

        var keep = new List<WalletOrigin>(rows.Count - zeros);
        foreach (var row in rows)
            if (!IsZero(row)) keep.Add(row);
        return keep;
    }

    /// <summary>
    /// The rows by amount across the whole list, not within each character: every row
    /// carries its owner in its own column. Equal amounts keep their order from
    /// <paramref name="rows"/>, and an unreadable wallet goes last whichever way.
    /// </summary>
    public static List<WalletOrigin> SortByAmount(IReadOnlyList<WalletOrigin> rows, bool descending)
    {
        var known = new List<WalletOrigin>(rows.Count);
        var unknown = new List<WalletOrigin>();
        foreach (var row in rows)
            (row.Wallet.IsKnown ? known : unknown).Add(row);

        // OrderBy is stable; List.Sort is not.
        var ordered = descending
            ? known.OrderByDescending(r => r.Wallet.Amount!.Value)
            : known.OrderBy(r => r.Wallet.Amount!.Value);

        var sorted = new List<WalletOrigin>(rows.Count);
        sorted.AddRange(ordered);
        sorted.AddRange(unknown);
        return sorted;
    }

    private readonly record struct Group(bool AnyLive, DateTimeOffset? Newest);

    private static int Compare(WalletOrigin a, WalletOrigin b, Dictionary<ulong, Group> groups)
    {
        if (a.ContentId != b.ContentId)
        {
            var ga = groups[a.ContentId];
            var gb = groups[b.ContentId];
            if (ga.AnyLive != gb.AnyLive) return ga.AnyLive ? -1 : 1;

            var byAge = NewestFirst(ga.Newest, gb.Newest);
            if (byAge != 0) return byAge;

            return a.ContentId.CompareTo(b.ContentId);
        }

        var byKind = ((int)a.Wallet.Id.Kind).CompareTo((int)b.Wallet.Id.Kind);
        if (byKind != 0) return byKind;

        return string.CompareOrdinal(a.Wallet.Id.Identifier, b.Wallet.Id.Identifier);
    }

    /// <summary>Newest first; a row with no timestamp at all sorts last, never first.</summary>
    private static int NewestFirst(DateTimeOffset? x, DateTimeOffset? y)
    {
        if (x is { } xv) return y is { } yv ? yv.CompareTo(xv) : -1;
        return y is null ? 0 : 1;
    }
}
