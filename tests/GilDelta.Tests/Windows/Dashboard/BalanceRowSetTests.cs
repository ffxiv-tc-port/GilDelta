using System.Collections.Generic;
using System.Linq;
using GilDelta.Events;
using GilDelta.Theme;
using GilDelta.Wallet;
using GilDelta.Windows.Dashboard;

namespace GilDelta.Tests.Windows.Dashboard;

/// <summary>
/// "Hide wallets with a 0 balance" on the balance tab. What must hold: only a wallet READ as
/// 0 goes, a "?" never does, and nothing the tab adds up moves when the box is flipped.
/// </summary>
public class BalanceRowSetTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-05T14:00:00+09:00");

    private static WalletOrigin Row(
        WalletKind kind, string id, long? amount, ulong cid, DateTimeOffset? at, bool live = false) =>
        new(new GilDelta.Wallet.Wallet(new WalletId(kind, id), amount), cid, at, live);

    private static DashboardContext Context(params WalletOrigin[] origins) =>
        new(origins, Array.Empty<GilEvent>(), T0, MidnightCoin.Instance);

    // ─── What is hidden ────────────────────────────────────────────────────────

    [Fact]
    public void A_zero_is_hidden_and_an_unreadable_wallet_stays()
    {
        var set = BalanceRowSet.From(new[]
        {
            Row(WalletKind.Self, "", 1_000, 1001, T0, live: true),
            Row(WalletKind.Retainer, "Empty", 0, 1001, T0, live: true),
            Row(WalletKind.Retainer, "Unread", null, 1001, T0),
            Row(WalletKind.FreeCompanyChest, "FC", null, 1001, T0),
        });

        var shown = set.Shown(hideZero: true);

        Assert.Equal(1, set.ZeroCount);
        Assert.Equal(3, shown.Count);
        Assert.DoesNotContain(shown, r => r.Wallet.Id.Identifier == "Empty");
        Assert.Equal(2, shown.Count(r => r.Wallet.Amount is null));
    }

    [Fact]
    public void A_zero_carried_over_from_an_older_reading_is_hidden_too()
    {
        var set = BalanceRowSet.From(new[]
        {
            Row(WalletKind.Self, "", 50, 2002, T0.AddDays(-3)),
            Row(WalletKind.Retainer, "Stale", 0, 2002, T0.AddDays(-3)),
            Row(WalletKind.Retainer, "NoTime", 0, 2002, null),
        });

        Assert.Equal(2, set.ZeroCount);
        Assert.Single(set.Shown(hideZero: true));
    }

    [Fact]
    public void With_the_box_off_every_row_is_shown()
    {
        var origins = new[]
        {
            Row(WalletKind.Self, "", 0, 1001, T0),
            Row(WalletKind.Retainer, "A", 0, 1001, T0),
            Row(WalletKind.Retainer, "B", null, 1001, T0),
        };

        var set = BalanceRowSet.From(origins);

        Assert.Same(set.All, set.Shown(hideZero: false));
        Assert.Equal(origins.Length, set.Shown(hideZero: false).Count);
        Assert.Equal(2, set.ZeroCount);
    }

    [Fact]
    public void Nothing_to_hide_hands_back_the_same_list()
    {
        var set = BalanceRowSet.From(new[]
        {
            Row(WalletKind.Self, "", 1, 1001, T0),
            Row(WalletKind.Retainer, "A", null, 1001, T0),
        });

        Assert.Same(set.All, set.NonZero);
        Assert.Equal(0, set.ZeroCount);
    }

    [Fact]
    public void An_empty_list_is_handled()
    {
        var set = BalanceRowSet.From(Array.Empty<WalletOrigin>());

        Assert.Empty(set.All);
        Assert.Empty(set.Shown(hideZero: true));
        Assert.Equal(0, set.ZeroCount);
    }

    // ─── What does not move ────────────────────────────────────────────────────

    [Fact]
    public void The_total_and_the_unreadable_count_are_the_same_either_way()
    {
        var ctx = Context(
            Row(WalletKind.Self, "", 5_000_000, 1001, T0, live: true),
            Row(WalletKind.Retainer, "A", 0, 1001, T0, live: true),
            Row(WalletKind.Retainer, "B", 2_000, 1001, T0, live: true),
            Row(WalletKind.Self, "", 0, 2002, T0.AddHours(-5)),
            Row(WalletKind.FreeCompanyChest, "FC", null, 2002, T0.AddHours(-5)));

        var set = BalanceRowSet.From(ctx.Origins);
        var hidden = set.Shown(hideZero: true);

        Assert.Equal(ctx.Total, BalanceRows.Total(set.All));
        Assert.Equal(ctx.Total, BalanceRows.Total(hidden));
        Assert.Equal(1, BalanceRows.UnknownCount(set.All));
        Assert.Equal(1, BalanceRows.UnknownCount(hidden));
    }

    [Fact]
    public void Hiding_keeps_the_order_of_what_is_left()
    {
        // 2002's newest reading is a zero retainer; hiding it must not reshuffle the
        // characters, or the list would jump every time the box is flipped.
        var set = BalanceRowSet.From(new[]
        {
            Row(WalletKind.Self, "", 10, 1001, T0),
            Row(WalletKind.Self, "", 20, 2002, T0.AddHours(-2)),
            Row(WalletKind.Retainer, "Z", 0, 2002, T0.AddHours(3)),
            Row(WalletKind.Retainer, "A", 30, 1001, T0),
        });

        Assert.Equal(
            set.All.Where(r => r.Wallet.Amount != 0).ToList(),
            set.Shown(hideZero: true).ToList());
        Assert.Equal(2002ul, set.Shown(hideZero: true)[0].ContentId);
    }

    [Fact]
    public void A_character_with_only_zero_wallets_leaves_no_rows_behind()
    {
        // The table has no per-character heading rows today; this is what guarantees that
        // nothing under such a heading could be left empty if one were added.
        var set = BalanceRowSet.From(new[]
        {
            Row(WalletKind.Self, "", 10, 1001, T0),
            Row(WalletKind.Self, "", 0, 2002, T0),
            Row(WalletKind.Retainer, "A", 0, 2002, T0),
        });

        var shown = set.Shown(hideZero: true);

        Assert.DoesNotContain(shown, r => r.ContentId == 2002);
        Assert.Equal(1, BalanceRows.CharacterCount(shown));
        // The character column is decided on every row, so "All characters" still labels 1001's row.
        Assert.Equal(2, set.CharacterCount);
        Assert.Equal(set.CharacterCount, set.Shown(hideZero: false).Select(r => r.ContentId).Distinct().Count());
    }

    // ─── The cache in front of it ──────────────────────────────────────────────

    [Fact]
    public void The_same_wallet_list_is_a_hit()
    {
        var cache = new BalanceRowCache();
        var origins = new[] { Row(WalletKind.Self, "", 0, 1001, T0), Row(WalletKind.Self, "", 5, 2002, T0) };

        var first = cache.For(origins);

        Assert.Same(first, cache.For(origins));
    }

    [Fact]
    public void A_new_wallet_list_is_a_miss_even_with_the_same_rows()
    {
        // The plugin hands over a new list whenever a wallet changes; the instance is the key.
        var cache = new BalanceRowCache();
        var first = cache.For(new[] { Row(WalletKind.Self, "", 5, 1001, T0) });
        var moved = cache.For(new[] { Row(WalletKind.Self, "", 0, 1001, T0) });

        Assert.NotSame(first, moved);
        Assert.Equal(0, first.ZeroCount);
        Assert.Equal(1, moved.ZeroCount);
        Assert.Empty(moved.Shown(hideZero: true));
    }

    [Fact]
    public void Flipping_the_box_needs_no_rebuild_and_shows_at_once()
    {
        // The setting is not part of the key: both lists come out of one build, so a hit
        // can never hand back the list for the other setting.
        var cache = new BalanceRowCache();
        var origins = new[] { Row(WalletKind.Self, "", 7, 1001, T0), Row(WalletKind.Retainer, "A", 0, 1001, T0) };

        var set = cache.For(origins);
        Assert.Single(set.Shown(hideZero: true));

        var again = cache.For(origins);
        Assert.Same(set, again);
        Assert.Equal(2, again.Shown(hideZero: false).Count);
        Assert.Single(again.Shown(hideZero: true));
    }

    [Fact]
    public void A_cache_hit_does_not_allocate()
    {
        var cache = new BalanceRowCache();
        var origins = new List<WalletOrigin>
        {
            Row(WalletKind.Self, "", 7, 1001, T0),
            Row(WalletKind.Retainer, "A", 0, 1001, T0),
        };
        for (var i = 0; i < 1_000; i++) cache.For(origins).Shown(hideZero: i % 2 == 0);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++) cache.For(origins).Shown(hideZero: i % 2 == 0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 1_024, $"{allocated} bytes over 10,000 hits");
    }
}
