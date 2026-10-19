using System.Collections.Generic;
using System.Linq;
using GilDelta.Events;
using GilDelta.Theme;
using GilDelta.Wallet;
using GilDelta.Windows.Dashboard;

namespace GilDelta.Tests.Windows.Dashboard;

/// <summary>
/// Clicking the balance header: most first, least first, then the original order again.
/// A "?" has no amount to sort by, so it always goes last; nothing the tab adds up moves.
/// </summary>
public class BalanceSortTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-05T14:00:00+09:00");

    private static WalletOrigin Row(
        WalletKind kind, string id, long? amount, ulong cid, DateTimeOffset? at, bool live = false) =>
        new(new GilDelta.Wallet.Wallet(new WalletId(kind, id), amount), cid, at, live);

    private static readonly WalletOrigin[] Mixed =
    {
        Row(WalletKind.Self, "", 500, 1001, T0, live: true),
        Row(WalletKind.Retainer, "A", null, 1001, T0),
        Row(WalletKind.Retainer, "B", 9_000, 1001, T0),
        Row(WalletKind.Retainer, "C", 0, 1001, T0),
        Row(WalletKind.Self, "", 2_000, 2002, T0.AddHours(-1)),
        Row(WalletKind.FreeCompanyChest, "FC", null, 2002, T0.AddHours(-1)),
        Row(WalletKind.Retainer, "D", 40, 2002, T0.AddHours(-1)),
    };

    private static long?[] Amounts(IReadOnlyList<WalletOrigin> rows) =>
        rows.Select(r => r.Wallet.Amount).ToArray();

    // ─── The click cycle ───────────────────────────────────────────────────────

    [Fact]
    public void Three_clicks_go_most_first_then_least_first_then_back()
    {
        var sort = BalanceSort.Default;

        sort = sort.Next();
        Assert.Equal(BalanceSort.AmountDescending, sort);
        sort = sort.Next();
        Assert.Equal(BalanceSort.AmountAscending, sort);
        sort = sort.Next();
        Assert.Equal(BalanceSort.Default, sort);
    }

    [Fact]
    public void An_unknown_saved_value_moves_on_like_the_original_order()
    {
        // A hand-edited or newer config: shown as the original order, so the first click
        // has to do what a first click does rather than appear to do nothing.
        Assert.Equal(BalanceSort.AmountDescending, ((BalanceSort)7).Next());

        var set = BalanceRowSet.From(Mixed);
        Assert.Same(set.All, set.Shown(hideZero: false, (BalanceSort)7));
    }

    [Fact]
    public void The_default_is_zero_so_a_config_without_the_key_keeps_the_original_order()
    {
        Assert.Equal(BalanceSort.Default, default(BalanceSort));
        Assert.Equal(0, (int)BalanceSort.Default);
    }

    // ─── The orders ────────────────────────────────────────────────────────────

    [Fact]
    public void Most_first_with_the_unreadable_ones_last()
    {
        var rows = BalanceRowSet.From(Mixed).Shown(hideZero: false, BalanceSort.AmountDescending);

        Assert.Equal(new long?[] { 9_000, 2_000, 500, 40, 0, null, null }, Amounts(rows));
    }

    [Fact]
    public void Least_first_still_puts_the_unreadable_ones_last()
    {
        var rows = BalanceRowSet.From(Mixed).Shown(hideZero: false, BalanceSort.AmountAscending);

        Assert.Equal(new long?[] { 0, 40, 500, 2_000, 9_000, null, null }, Amounts(rows));
    }

    [Fact]
    public void Switching_direction_really_changes_the_rows()
    {
        var set = BalanceRowSet.From(Mixed);

        var original = Amounts(set.Shown(hideZero: false, BalanceSort.Default));
        var most = Amounts(set.Shown(hideZero: false, BalanceSort.AmountDescending));
        var least = Amounts(set.Shown(hideZero: false, BalanceSort.AmountAscending));

        Assert.NotEqual(original, most);
        Assert.NotEqual(most, least);
        Assert.NotEqual(original, least);
    }

    [Fact]
    public void The_third_click_brings_back_the_very_same_original_list()
    {
        var set = BalanceRowSet.From(Mixed);

        Assert.Same(set.All, set.Shown(hideZero: false, BalanceSort.Default));
        Assert.Same(set.NonZero, set.Shown(hideZero: true, BalanceSort.Default));
    }

    [Fact]
    public void Equal_amounts_keep_the_original_order_between_them()
    {
        // Stable, so ties stay grouped by character in the order the list already had.
        var set = BalanceRowSet.From(new[]
        {
            Row(WalletKind.Self, "", 100, 1001, T0, live: true),
            Row(WalletKind.Retainer, "A", 100, 1001, T0),
            Row(WalletKind.Self, "", 100, 2002, T0.AddHours(-1)),
            Row(WalletKind.FreeCompanyChest, "X", null, 2002, T0.AddHours(-1)),
            Row(WalletKind.Retainer, "B", null, 1001, T0),
        });

        foreach (var sort in new[] { BalanceSort.AmountDescending, BalanceSort.AmountAscending })
        {
            var rows = set.Shown(hideZero: false, sort);
            Assert.Equal(set.All.Where(r => r.Wallet.IsKnown).ToList(), rows.Take(3).ToList());
            Assert.Equal(set.All.Where(r => !r.Wallet.IsKnown).ToList(), rows.Skip(3).ToList());
        }
    }

    [Fact]
    public void Sorting_works_on_what_is_left_after_hiding_zeros()
    {
        var rows = BalanceRowSet.From(Mixed).Shown(hideZero: true, BalanceSort.AmountAscending);

        Assert.Equal(new long?[] { 40, 500, 2_000, 9_000, null, null }, Amounts(rows));
    }

    [Fact]
    public void Sorting_neither_drops_nor_invents_a_row()
    {
        var set = BalanceRowSet.From(Mixed);

        foreach (var hide in new[] { false, true })
        foreach (var sort in new[] { BalanceSort.AmountDescending, BalanceSort.AmountAscending })
        {
            var rows = set.Shown(hide, sort);
            var plain = set.Shown(hide);
            Assert.Equal(plain.Count, rows.Count);
            Assert.True(plain.All(rows.Contains));
        }
    }

    // ─── What does not move ────────────────────────────────────────────────────

    [Fact]
    public void The_total_and_the_unreadable_count_do_not_depend_on_the_order()
    {
        var ctx = new DashboardContext(Mixed, Array.Empty<GilEvent>(), T0, MidnightCoin.Instance);
        var set = BalanceRowSet.From(ctx.Origins);

        foreach (var hide in new[] { false, true })
        foreach (var sort in new[] { BalanceSort.Default, BalanceSort.AmountDescending, BalanceSort.AmountAscending })
        {
            var rows = set.Shown(hide, sort);
            Assert.Equal(ctx.Total, BalanceRows.Total(rows));
            Assert.Equal(2, BalanceRows.UnknownCount(rows));
        }
        Assert.Equal(2, set.CharacterCount);
    }

    // ─── Kept, not rebuilt ─────────────────────────────────────────────────────

    [Fact]
    public void Each_order_is_built_once_per_wallet_list()
    {
        var cache = new BalanceRowCache();
        var origins = Mixed.ToList();

        var first = cache.For(origins).Shown(hideZero: true, BalanceSort.AmountDescending);

        Assert.Same(first, cache.For(origins).Shown(hideZero: true, BalanceSort.AmountDescending));
        Assert.NotSame(first, cache.For(origins).Shown(hideZero: false, BalanceSort.AmountDescending));
        Assert.NotSame(first, cache.For(origins).Shown(hideZero: true, BalanceSort.AmountAscending));
    }

    [Fact]
    public void A_new_wallet_list_is_sorted_afresh()
    {
        var cache = new BalanceRowCache();
        var before = cache.For(new[] { Row(WalletKind.Self, "", 5, 1001, T0), Row(WalletKind.Retainer, "A", 7, 1001, T0) })
            .Shown(hideZero: false, BalanceSort.AmountDescending);
        var after = cache.For(new[] { Row(WalletKind.Self, "", 9, 1001, T0), Row(WalletKind.Retainer, "A", 7, 1001, T0) })
            .Shown(hideZero: false, BalanceSort.AmountDescending);

        Assert.Equal(new long?[] { 7, 5 }, Amounts(before));
        Assert.Equal(new long?[] { 9, 7 }, Amounts(after));
    }

    [Fact]
    public void Flipping_between_orders_after_the_first_build_does_not_allocate()
    {
        var cache = new BalanceRowCache();
        var origins = Mixed.ToList();
        var sorts = new[] { BalanceSort.Default, BalanceSort.AmountDescending, BalanceSort.AmountAscending };

        for (var i = 0; i < 1_000; i++) cache.For(origins).Shown(i % 2 == 0, sorts[i % 3]);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++) cache.For(origins).Shown(i % 2 == 0, sorts[i % 3]);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 1_024, $"{allocated} bytes over 10,000 hits");
    }
}
