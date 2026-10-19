using System.Collections.Generic;
using System.Linq;
using GilDelta.Events;
using GilDelta.Theme;
using GilDelta.Wallet;
using GilDelta.Windows.Dashboard;
using GilDelta.Windows.Widget;

namespace GilDelta.Tests.Windows.Widget;

/// <summary>
/// The balance list: which wallets add up to the widget's total wealth figure. The
/// property that matters is the first test here - the rows add up to the number printed
/// above them, because the rows ARE the list the total was summed over.
/// </summary>
public class BalanceRowsTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-05T14:00:00+09:00");

    private static WalletOrigin Row(
        WalletKind kind, string id, long? amount, ulong cid, DateTimeOffset? at, bool live = false) =>
        new(new GilDelta.Wallet.Wallet(new WalletId(kind, id), amount), cid, at, live);

    private static WidgetContext Context(params WalletOrigin[] origins) =>
        new(origins, Array.Empty<GilEvent>(), T0, MidnightCoin.Instance);

    // ─── The one property the view promises ────────────────────────────────────

    [Fact]
    public void The_rows_add_up_to_the_total_the_widget_prints()
    {
        var ctx = Context(
            Row(WalletKind.Self, "", 5_000_000, 1001, T0, live: true),
            Row(WalletKind.Retainer, "Ayame", 2_000_000, 1001, T0, live: true),
            Row(WalletKind.Self, "", 750_000, 2002, T0.AddHours(-30)),
            Row(WalletKind.FreeCompanyChest, "FC", 18_000_000, 1001, T0));

        var rows = BalanceRows.Build(ctx.Origins);

        Assert.Equal(ctx.Total, BalanceRows.Total(rows));
        Assert.Equal(25_750_000, BalanceRows.Total(rows));
    }

    [Fact]
    public void An_unreadable_wallet_contributes_nothing_rather_than_a_zero()
    {
        // 🔴 An unreadable chest counted as 0 is how a deposit gets fabricated. The list has
        // to leave it out of the sum and say so, and the sum still has to match the widget -
        // which leaves it out the same way.
        var ctx = Context(
            Row(WalletKind.Self, "", 1_000, 1001, T0, live: true),
            Row(WalletKind.FreeCompanyChest, "FC", null, 1001, T0));

        var rows = BalanceRows.Build(ctx.Origins);

        Assert.Equal(1_000, BalanceRows.Total(rows));
        Assert.Equal(ctx.Total, BalanceRows.Total(rows));
        Assert.Equal(1, BalanceRows.UnknownCount(rows));
    }

    [Fact]
    public void Build_neither_drops_nor_invents_a_row()
    {
        var origins = new[]
        {
            Row(WalletKind.Self, "", 1, 1001, T0),
            Row(WalletKind.Retainer, "A", 2, 1001, T0),
            Row(WalletKind.Self, "", 3, 2002, T0.AddHours(-1)),
        };

        var rows = BalanceRows.Build(origins);

        Assert.Equal(origins.Length, rows.Count);
        Assert.Equal(
            origins.OrderBy(o => o.Wallet.Amount).ToList(),
            rows.OrderBy(o => o.Wallet.Amount).ToList());
    }

    // ─── Order ─────────────────────────────────────────────────────────────────

    [Fact]
    public void The_character_being_read_right_now_comes_first()
    {
        // Even though the other character's stored reading carries the newer timestamp:
        // "what I am holding" is what the person opening this list came to see.
        var rows = BalanceRows.Build(new[]
        {
            Row(WalletKind.Self, "", 10, 2002, T0.AddHours(5)),
            Row(WalletKind.Self, "", 20, 1001, T0, live: true),
        });

        Assert.Equal(1001ul, rows[0].ContentId);
        Assert.Equal(2002ul, rows[1].ContentId);
    }

    [Fact]
    public void Otherwise_the_newest_reading_comes_first()
    {
        var rows = BalanceRows.Build(new[]
        {
            Row(WalletKind.Self, "", 10, 1001, T0),
            Row(WalletKind.Self, "", 20, 2002, T0.AddHours(5)),
            Row(WalletKind.Self, "", 30, 3003, T0.AddHours(2)),
        });

        Assert.Equal(new ulong[] { 2002, 3003, 1001 }, rows.Select(r => r.ContentId).ToArray());
    }

    [Fact]
    public void A_character_whose_rows_have_no_timestamp_sorts_last_not_first()
    {
        // "I do not know when" must never be treated as "just now".
        var rows = BalanceRows.Build(new[]
        {
            Row(WalletKind.Self, "", 10, 1001, null),
            Row(WalletKind.Self, "", 20, 2002, T0.AddYears(-1)),
        });

        Assert.Equal(2002ul, rows[0].ContentId);
        Assert.Equal(1001ul, rows[1].ContentId);
    }

    [Fact]
    public void Within_one_character_it_is_self_then_retainers_then_the_chest()
    {
        var rows = BalanceRows.Build(new[]
        {
            Row(WalletKind.FreeCompanyChest, "FC", 3, 1001, T0),
            Row(WalletKind.Retainer, "Zoe", 2, 1001, T0),
            Row(WalletKind.Retainer, "Ayame", 1, 1001, T0),
            Row(WalletKind.Self, "", 0, 1001, T0),
        });

        Assert.Equal(
            new[] { WalletKind.Self, WalletKind.Retainer, WalletKind.Retainer, WalletKind.FreeCompanyChest },
            rows.Select(r => r.Wallet.Id.Kind).ToArray());

        // Retainers among themselves are by name, so the list does not reshuffle itself
        // when the game hands the slots back in a different order.
        Assert.Equal("Ayame", rows[1].Wallet.Id.Identifier);
        Assert.Equal("Zoe", rows[2].Wallet.Id.Identifier);
    }

    [Fact]
    public void One_live_row_is_enough_to_bring_its_character_to_the_top()
    {
        // A character can have a live purse and a carried-forward retainer row at the
        // same time; the group must not split in two.
        var rows = BalanceRows.Build(new[]
        {
            Row(WalletKind.Self, "", 10, 2002, T0.AddHours(9)),
            Row(WalletKind.Retainer, "Old", 20, 1001, T0.AddHours(-4)),
            Row(WalletKind.Self, "", 30, 1001, T0.AddHours(-4), live: true),
        });

        Assert.Equal(new ulong[] { 1001, 1001, 2002 }, rows.Select(r => r.ContentId).ToArray());
    }

    // ─── Counting ──────────────────────────────────────────────────────────────

    [Fact]
    public void CharacterCount_counts_distinct_characters()
    {
        var rows = BalanceRows.Build(new[]
        {
            Row(WalletKind.Self, "", 1, 1001, T0),
            Row(WalletKind.Retainer, "A", 2, 1001, T0),
            Row(WalletKind.Self, "", 3, 2002, T0),
        });

        Assert.Equal(2, BalanceRows.CharacterCount(rows));
    }

    [Fact]
    public void UnknownCount_is_zero_when_everything_was_read()
    {
        var rows = BalanceRows.Build(new[] { Row(WalletKind.Self, "", 1, 1001, T0) });
        Assert.Equal(0, BalanceRows.UnknownCount(rows));
    }

    [Fact]
    public void An_empty_list_is_handled_rather_than_thrown_at()
    {
        var rows = BalanceRows.Build(Array.Empty<WalletOrigin>());

        Assert.Empty(rows);
        Assert.Equal(0, BalanceRows.Total(rows));
        Assert.Equal(0, BalanceRows.CharacterCount(rows));
    }

    // ─── The list and the widget cannot describe different wallets ─────────────

    [Fact]
    public void Excluding_the_chest_takes_it_out_of_both_the_list_and_the_total()
    {
        var origins = new[]
        {
            Row(WalletKind.Self, "", 1_000, 1001, T0, live: true),
            Row(WalletKind.FreeCompanyChest, "FC", 50_000_000, 1001, T0),
        };

        var ctx = Context(WalletExclusion.Origins(origins, excludeFreeCompanyChest: true).ToArray());
        var rows = BalanceRows.Build(ctx.Origins);

        Assert.Single(rows);
        Assert.Equal(1_000, ctx.Total);
        Assert.Equal(ctx.Total, BalanceRows.Total(rows));
    }

    [Fact]
    public void The_context_derives_its_wallets_from_its_origins()
    {
        // Not two lists that happen to agree: one list, read two ways. Anything else and
        // the sum under the rows could disagree with the figure above them.
        var ctx = Context(
            Row(WalletKind.Self, "", 7, 1001, T0),
            Row(WalletKind.Retainer, "A", 11, 1001, T0));

        Assert.Equal(
            ctx.Origins.Select(o => o.Wallet).ToList(),
            ctx.Wallets.ToList());
    }
}
