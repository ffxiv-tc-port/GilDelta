using GilDelta.Events;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events;

public class DailySummaryTests
{
    private static readonly DateOnly Day = new(2026, 9, 9);
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-09-09T00:49:59+08:00");

    private static GilEvent E(WalletKind kind, long amount, GilEventCategory cat) =>
        new(At, new WalletId(kind, kind == WalletKind.FreeCompanyChest ? "FC" : ""), amount, cat, null);

    [Fact]
    public void Chest_events_are_counted_separately_so_both_answers_stay_exact()
    {
        var day = new DailySummary(1, Day);
        day.Add(E(WalletKind.Self, 2, GilEventCategory.RetainerSale));
        day.Add(E(WalletKind.Self, 60, GilEventCategory.RetainerSale));
        day.Add(E(WalletKind.Self, -263, GilEventCategory.Teleport));
        day.Add(E(WalletKind.FreeCompanyChest, 50_000_000, GilEventCategory.FreeCompanyChestDeposit));

        Assert.Equal(49_999_799, day.NetFor(false));
        Assert.Equal(-201, day.NetFor(true));
        Assert.Equal(4, day.EventCountFor(false));
        Assert.Equal(3, day.EventCountFor(true));
    }

    [Fact]
    public void CategoryTotals_merge_the_chest_back_in_only_when_it_is_included()
    {
        var day = new DailySummary(1, Day);
        day.Add(E(WalletKind.Self, -400, GilEventCategory.Misc));
        day.Add(E(WalletKind.FreeCompanyChest, -1_000, GilEventCategory.Misc));

        Assert.Equal(-1_400, day.CategoryTotals(false)[GilEventCategory.Misc]);
        Assert.Equal(-400, day.CategoryTotals(true)[GilEventCategory.Misc]);
    }

    [Fact]
    public void The_first_reading_of_the_day_becomes_the_opening_total()
    {
        var day = new DailySummary(1, Day);
        Assert.Null(day.OpenTotal);
        Assert.Null(day.CloseTotal);

        Assert.True(day.ObserveTotal(1_000, 900));
        Assert.True(day.ObserveTotal(2_000, 1_800));

        Assert.Equal(1_000, day.OpenTotal);
        Assert.Equal(2_000, day.CloseTotal);
        Assert.Equal(900, day.OpenFor(true));
        Assert.Equal(1_800, day.CloseFor(true));
    }

    [Fact]
    public void An_unchanged_reading_reports_no_change_so_the_file_is_not_rewritten_forever()
    {
        var day = new DailySummary(1, Day);
        Assert.True(day.ObserveTotal(1_000, 900));
        Assert.False(day.ObserveTotal(1_000, 900));
    }

    [Fact]
    public void ResetEvents_clears_the_event_side_and_keeps_the_wallet_readings()
    {
        var day = new DailySummary(1, Day);
        day.ObserveTotal(1_000, 900);
        day.Add(E(WalletKind.Self, 500, GilEventCategory.Misc));

        day.ResetEvents();

        Assert.Equal(0, day.NetFor(false));
        Assert.Equal(0, day.EventCountFor(false));
        Assert.Empty(day.CategoryTotals(false));
        Assert.Equal(1_000, day.OpenTotal);
        Assert.Equal(1_000, day.CloseTotal);
    }

    [Fact]
    public void An_aggregate_row_adds_nets_but_refuses_to_invent_an_account_wide_total()
    {
        var a = new DailySummary(1, Day);
        a.Add(E(WalletKind.Self, 500, GilEventCategory.Misc));
        a.ObserveTotal(1_000, 1_000);

        var b = new DailySummary(2, Day);
        b.Add(E(WalletKind.Self, -200, GilEventCategory.Teleport));
        b.ObserveTotal(7_000, 7_000);

        var merged = DailySummary.Aggregate(Day, new[] { a, b });

        Assert.True(merged.IsAggregate);
        Assert.Equal(300, merged.NetFor(false));
        Assert.Equal(2, merged.EventCountFor(false));
        // Each character's wallet is read at a different moment, so summing them would
        // be a number that never existed. "?" is the honest answer.
        Assert.Null(merged.OpenTotal);
        Assert.Null(merged.CloseTotal);
    }

    [Fact]
    public void An_untouched_day_is_empty_and_a_reading_alone_is_enough_to_make_it_real()
    {
        var day = new DailySummary(1, Day);
        Assert.True(day.IsEmpty);

        day.ObserveTotal(1_000, 1_000);
        Assert.False(day.IsEmpty);
    }
}
