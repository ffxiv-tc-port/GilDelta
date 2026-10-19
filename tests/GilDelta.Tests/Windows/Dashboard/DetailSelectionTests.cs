using System.Collections.Generic;
using System.Linq;
using GilDelta.Events;
using GilDelta.Theme;
using GilDelta.Wallet;
using GilDelta.Windows.Dashboard;

namespace GilDelta.Tests.Windows.Dashboard;

/// <summary>
/// "Click a figure, see the records behind it." 🔴 The property every test here exists
/// for is that the detail ADDS UP to the figure it was opened from: a drill-down that
/// quietly disagrees with the number above it makes the user distrust both.
/// </summary>
public class DetailSelectionTests
{
    private const ulong Cid = 18014409509708868;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-09T18:00:00+08:00");

    private static GilEvent E(
        string timestamp,
        long amount,
        GilEventCategory cat = GilEventCategory.Misc,
        WalletKind kind = WalletKind.Self) =>
        new(DateTimeOffset.Parse(timestamp),
            new WalletId(kind, kind == WalletKind.Retainer ? "R" : ""),
            amount, cat, null)
        { ContentId = Cid };

    private static DashboardContext Ctx(
        IReadOnlyList<GilEvent> events,
        IReadOnlyList<GilEvent>? excluded = null) =>
        new(Array.Empty<GilDelta.Wallet.Wallet>(), events, Now, MidnightCoin.Instance)
        {
            Excluded = excluded ?? Array.Empty<GilEvent>(),
        };

    // ─── The breakdown tab: category over a window ─────────────────────────────

    [Fact]
    public void A_breakdown_row_drills_down_to_exactly_its_own_figure()
    {
        var events = new[]
        {
            E("2026-09-08T10:00:00+08:00", +500_000, GilEventCategory.RetainerSale),
            E("2026-09-09T11:00:00+08:00", +400_000, GilEventCategory.RetainerSale),
            E("2026-09-09T12:00:00+08:00", -100_000, GilEventCategory.NpcShopBuy),
            E("2026-09-09T13:00:00+08:00",     -300, GilEventCategory.Teleport),
        };
        var ctx = Ctx(events);

        var from = Now.AddDays(-7);
        var grouped = ctx.NetByCategory(from, Now);

        foreach (var kv in grouped)
        {
            var result = DetailSelection.Build(
                ctx.Events, ctx.Excluded,
                DetailQuery.ForRange("t", from, Now, kv.Key, kv.Value));

            Assert.Equal(kv.Value, result.Total);
            Assert.Equal(0, result.Difference);
            Assert.True(result.Reconciles);
        }
    }

    [Fact]
    public void The_window_is_inclusive_at_both_ends_like_the_figure_it_explains()
    {
        // 🔴 NetByCategory uses `< from` / `> to`, i.e. both bounds count. A half-open
        // window here would drop the record sitting exactly on the boundary and the
        // detail would be short by exactly that record, silently.
        var from = DateTimeOffset.Parse("2026-09-09T10:00:00+08:00");
        var to   = DateTimeOffset.Parse("2026-09-09T12:00:00+08:00");

        var events = new[]
        {
            E("2026-09-09T10:00:00+08:00", 100),   // exactly on `from`
            E("2026-09-09T11:00:00+08:00", 200),
            E("2026-09-09T12:00:00+08:00", 400),   // exactly on `to`
            E("2026-09-09T12:00:01+08:00", 800),   // just outside
        };
        var ctx = Ctx(events);

        var figure = ctx.NetByCategory(from, to)[GilEventCategory.Misc];
        var result = DetailSelection.Build(
            ctx.Events, ctx.Excluded,
            DetailQuery.ForRange("t", from, to, GilEventCategory.Misc, figure));

        Assert.Equal(700, figure);
        Assert.Equal(700, result.Total);
        Assert.Equal(3, result.CountedCount);
        Assert.True(result.Reconciles);
    }

    [Fact]
    public void A_category_query_ignores_the_other_categories()
    {
        var ctx = Ctx(new[]
        {
            E("2026-09-09T10:00:00+08:00", 100, GilEventCategory.Repair),
            E("2026-09-09T10:30:00+08:00", 999, GilEventCategory.Teleport),
        });

        var result = DetailSelection.Build(
            ctx.Events, ctx.Excluded,
            DetailQuery.ForRange("t", Now.AddDays(-1), Now, GilEventCategory.Repair, 100));

        Assert.Equal(100, result.Total);
        Assert.Single(result.Rows);
    }

    // ─── The heatmap / chart / daily tabs: one settled day ─────────────────────

    [Fact]
    public void A_settled_day_drills_down_to_exactly_its_own_net()
    {
        var day = new DateOnly(2026, 9, 9);
        var events = new[]
        {
            E("2026-09-08T23:00:00+08:00", 7_777),                    // the day before
            E("2026-09-09T09:00:00+08:00",   250, GilEventCategory.NpcShopSell),
            E("2026-09-09T21:00:00+08:00", -1_000, GilEventCategory.Repair),
        };

        var ledger = new DailyLedger();
        foreach (var ev in events) ledger.Add(ev);
        var row = ledger.For(Cid).Single(r => r.Date == day);

        var ctx = Ctx(events);
        var result = DetailSelection.Build(
            ctx.Events, ctx.Excluded,
            DetailQuery.ForDay("t", day, row.NetFor(excludeFreeCompanyChest: false)));

        Assert.Equal(-750, result.Total);
        Assert.Equal(row.NetFor(false), result.Total);
        Assert.Equal(2, result.CountedCount);
        Assert.True(result.Reconciles);
    }

    [Fact]
    public void A_day_is_matched_by_the_offset_the_record_carried()
    {
        // 🔴 The settlement book buckets on the event's own wall clock. Matching with a
        // from/to window built from the viewer's offset would move a late-evening record
        // recorded in another time zone onto the wrong side of midnight, and the day's
        // detail would then be short by it - with the figure above unchanged.
        var ev = E("2026-09-09T23:30:00+09:00", 500);
        var ctx = Ctx(new[] { ev });

        var onItsOwnDay = DetailSelection.Build(
            ctx.Events, ctx.Excluded, DetailQuery.ForDay("t", new DateOnly(2026, 9, 9), 500));
        var onTheNextDay = DetailSelection.Build(
            ctx.Events, ctx.Excluded, DetailQuery.ForDay("t", new DateOnly(2026, 9, 10), 0));

        Assert.Equal(DailyLedger.LocalDayOf(ev.Timestamp), new DateOnly(2026, 9, 9));
        Assert.Equal(500, onItsOwnDay.Total);
        Assert.True(onItsOwnDay.Reconciles);
        Assert.Empty(onTheNextDay.Rows);
    }

    [Fact]
    public void The_free_company_filter_lands_on_both_sides_at_once()
    {
        // The dashboard hands the views an already-filtered event list and reads the
        // matching half of the settlement, so the two have to keep agreeing when the
        // setting is on.
        var day = new DateOnly(2026, 9, 9);
        var events = new[]
        {
            E("2026-09-09T09:00:00+08:00", 250, GilEventCategory.NpcShopSell),
            E("2026-09-09T10:00:00+08:00", 50_000_000,
              GilEventCategory.FreeCompanyChestDeposit, WalletKind.FreeCompanyChest),
        };

        var ledger = new DailyLedger();
        foreach (var ev in events) ledger.Add(ev);
        var row = ledger.For(Cid).Single();

        var visible = WalletExclusion.Events(events, excludeFreeCompanyChest: true);
        var ctx = Ctx(visible);

        var result = DetailSelection.Build(
            ctx.Events, ctx.Excluded,
            DetailQuery.ForDay("t", day, row.NetFor(excludeFreeCompanyChest: true)));

        Assert.Equal(250, result.Total);
        Assert.True(result.Reconciles);
    }

    // ─── Excluded records ─────────────────────────────────────────────────────

    [Fact]
    public void An_excluded_record_is_shown_but_never_added_in()
    {
        // Showing it is the point: without the row on screen the detail simply looks
        // shorter than the count above it, and "where did my 50,000,000 go" has no answer.
        var day = new DateOnly(2026, 9, 9);
        var counted = new[] { E("2026-09-09T09:00:00+08:00", 250, GilEventCategory.NpcShopSell) };
        var excluded = new[]
        {
            E("2026-09-09T10:00:00+08:00", 50_000_000, GilEventCategory.FreeCompanyChestDeposit)
                with { Suspect = true },
        };

        var ctx = Ctx(counted, excluded);
        var result = DetailSelection.Build(
            ctx.Events, ctx.Excluded, DetailQuery.ForDay("t", day, 250));

        Assert.Equal(250, result.Total);
        Assert.Equal(1, result.CountedCount);
        Assert.Equal(1, result.ExcludedCount);
        Assert.Equal(2, result.Rows.Count);
        Assert.Contains(result.Rows, r => r.Suspect);
        Assert.True(result.Reconciles);
    }

    [Fact]
    public void Excluding_a_record_moves_the_settlement_and_the_detail_together()
    {
        // The end-to-end shape of the feature: the user drills into a day, spots the
        // fabricated deposit, throws it out - and both numbers have to move.
        var day = new DateOnly(2026, 9, 9);
        var good = E("2026-09-09T09:00:00+08:00", 250, GilEventCategory.NpcShopSell);
        var bogus = E("2026-09-09T10:00:00+08:00", 50_000_000,
                      GilEventCategory.FreeCompanyChestDeposit);

        var ledger = new DailyLedger();
        ledger.Add(good);
        ledger.Add(bogus);
        Assert.Equal(50_000_250, ledger.For(Cid).Single().Net);

        var before = DetailSelection.Build(
            new[] { good, bogus }, Array.Empty<GilEvent>(),
            DetailQuery.ForDay("t", day, ledger.For(Cid).Single().NetFor(false)));
        Assert.True(before.Reconciles);

        var flagged = bogus with { Suspect = true };
        ledger.Retract(flagged);

        var after = DetailSelection.Build(
            new[] { good }, new[] { flagged },
            DetailQuery.ForDay("t", day, ledger.For(Cid).Single().NetFor(false)));

        Assert.Equal(250, after.Total);
        Assert.True(after.Reconciles);
        Assert.Equal(1, after.ExcludedCount);
    }

    // ─── When it genuinely cannot add up ──────────────────────────────────────

    [Fact]
    public void A_settled_day_whose_records_retention_deleted_reports_the_difference()
    {
        // 🔴 This is the case the reconciliation line exists for. The settlement outlives
        // the events behind it, so the detail is legitimately short - and the user has to
        // be TOLD that, not left to notice that two numbers disagree.
        var result = DetailSelection.Build(
            Array.Empty<GilEvent>(), Array.Empty<GilEvent>(),
            DetailQuery.ForDay("t", new DateOnly(2026, 1, 1), 1_234_567));

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.Total);
        Assert.Equal(1_234_567, result.Difference);
        Assert.False(result.Reconciles);
        Assert.True(result.Query.SummaryFromLedger);
    }

    [Fact]
    public void No_figure_to_reconcile_against_means_no_claim_is_made()
    {
        var ctx = Ctx(new[] { E("2026-09-09T09:00:00+08:00", 250) });

        var result = DetailSelection.Build(
            ctx.Events, ctx.Excluded,
            DetailQuery.ForDay("t", new DateOnly(2026, 9, 9), null));

        Assert.Null(result.Difference);
        Assert.True(result.Reconciles);   // nothing was claimed, so nothing is contradicted
    }

    // ─── The row cap ──────────────────────────────────────────────────────────

    [Fact]
    public void The_row_cap_never_touches_the_arithmetic()
    {
        // The cap bounds what ImGui is asked to lay out. If it bounded the total instead,
        // a busy day would show a figure that is simply wrong.
        var count = DetailSelection.MaxRows + 37;
        var start = DateTimeOffset.Parse("2026-09-09T00:00:00+08:00");
        var events = Enumerable.Range(0, count)
            .Select(i => new GilEvent(start.AddSeconds(i), new WalletId(WalletKind.Self, ""),
                                      10, GilEventCategory.Misc, null) { ContentId = Cid })
            .ToArray();

        var result = DetailSelection.Build(
            events, Array.Empty<GilEvent>(),
            DetailQuery.ForDay("t", new DateOnly(2026, 9, 9), 10L * count));

        Assert.Equal(DetailSelection.MaxRows, result.Rows.Count);
        Assert.Equal(count, result.MatchedCount);
        Assert.True(result.IsTruncated);
        Assert.Equal(10L * count, result.Total);
        Assert.True(result.Reconciles);
    }

    // ─── The figure is re-read live, not captured ─────────────────────────────

    [Fact]
    public void The_day_figure_is_re_read_from_the_settlement_book_every_time()
    {
        // 🔴 The detail view carries the exclude button, so the figure it reconciles
        // against has to move when the user presses it. A figure captured at click time
        // would leave the view reporting a difference the user had just created on
        // purpose - and blaming retention for it in the tooltip.
        var day = new DateOnly(2026, 9, 9);
        var good = E("2026-09-09T09:00:00+08:00", 250, GilEventCategory.NpcShopSell);
        var bogus = E("2026-09-09T10:00:00+08:00", 50_000_000,
                      GilEventCategory.FreeCompanyChestDeposit);

        var ledger = new DailyLedger();
        ledger.Add(good);
        ledger.Add(bogus);

        // What the cell said when it was clicked.
        var clicked = DetailQuery.ForDay("t", day, 50_000_250);

        var flagged = bogus with { Suspect = true };
        ledger.Retract(flagged);

        var ctx = new DashboardContext(
            Array.Empty<GilDelta.Wallet.Wallet>(), new[] { good }, Now, MidnightCoin.Instance)
        {
            Excluded = new[] { flagged },
            Daily = ledger.For(Cid),
        };

        var live = DetailSelection.Refresh(ctx, clicked);
        var result = DetailSelection.Build(ctx.Events, ctx.Excluded, live);

        Assert.Equal(250, live.SummaryAmount);
        Assert.Equal(250, result.Total);
        Assert.True(result.Reconciles);
    }

    [Fact]
    public void A_day_that_left_the_settlement_book_makes_no_claim_at_all()
    {
        var ctx = Ctx(Array.Empty<GilEvent>());

        var live = DetailSelection.Refresh(
            ctx, DetailQuery.ForDay("t", new DateOnly(2026, 9, 9), 999));

        Assert.Null(live.SummaryAmount);
    }

    [Fact]
    public void A_category_figure_is_re_read_from_the_events_every_time()
    {
        var from = Now.AddDays(-7);
        var ctx = Ctx(new[]
        {
            E("2026-09-09T09:00:00+08:00", 250, GilEventCategory.NpcShopSell),
            E("2026-09-09T10:00:00+08:00", 750, GilEventCategory.NpcShopSell),
        });

        var live = DetailSelection.Refresh(
            ctx, DetailQuery.ForRange("t", from, Now, GilEventCategory.NpcShopSell, 1));
        var result = DetailSelection.Build(ctx.Events, ctx.Excluded, live);

        Assert.Equal(1_000, live.SummaryAmount);
        Assert.Equal(1_000, result.Total);
        Assert.True(result.Reconciles);
    }

    [Fact]
    public void Rows_come_back_newest_first_with_the_excluded_ones_in_place()
    {
        var counted = new[]
        {
            E("2026-09-09T09:00:00+08:00", 1),
            E("2026-09-09T11:00:00+08:00", 2),
        };
        var excluded = new[] { E("2026-09-09T10:00:00+08:00", 3) with { Suspect = true } };

        var result = DetailSelection.Build(
            counted, excluded, DetailQuery.ForDay("t", new DateOnly(2026, 9, 9), 3));

        Assert.Equal(
            new[] { 2L, 3L, 1L },
            result.Rows.Select(r => r.Amount).ToArray());
    }
}
