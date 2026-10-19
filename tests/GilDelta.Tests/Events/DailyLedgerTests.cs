using System.Collections.Generic;
using System.Linq;
using GilDelta.Events;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events;

public class DailyLedgerTests
{
    private const ulong Cid = 18014409509708868;

    private static GilEvent E(string timestamp, long amount,
                              GilEventCategory cat = GilEventCategory.Misc,
                              WalletKind kind = WalletKind.Self,
                              ulong cid = Cid) =>
        new(DateTimeOffset.Parse(timestamp),
            new WalletId(kind, kind == WalletKind.FreeCompanyChest ? "FC" : ""),
            amount, cat, null)
        { ContentId = cid };

    [Fact]
    public void An_event_is_booked_by_its_own_offset_not_by_the_machine_clock()
    {
        // 23:59 and 00:00:01 on either side of midnight, both +08:00.
        var ledger = new DailyLedger();
        ledger.Add(E("2026-09-08T23:59:59+08:00", 100));
        ledger.Add(E("2026-09-09T00:00:01+08:00", 200));

        var rows = ledger.For(Cid);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2026, 9, 8), rows[0].Date);
        Assert.Equal(100, rows[0].NetFor(false));
        Assert.Equal(new DateOnly(2026, 9, 9), rows[1].Date);
        Assert.Equal(200, rows[1].NetFor(false));
    }

    [Fact]
    public void Backfill_counts_every_event_of_a_day_it_has_just_created()
    {
        // The regression this guards: deciding "is this day already known?" per event
        // flips to true the moment the first event creates the row, and silently drops
        // every event after it.
        var ledger = new DailyLedger();
        var events = new[]
        {
            E("2026-09-08T01:00:00+08:00", 1),
            E("2026-09-08T02:00:00+08:00", 2),
            E("2026-09-08T03:00:00+08:00", 4),
        };

        ledger.BackfillFromEvents(events, new DateOnly(2026, 9, 9));

        Assert.Equal(7, ledger.For(Cid).Single().NetFor(false));
        Assert.Equal(3, ledger.For(Cid).Single().EventCountFor(false));
    }

    [Fact]
    public void Backfill_leaves_a_settled_day_alone_so_it_outlives_its_events()
    {
        var ledger = new DailyLedger();
        ledger.GetOrCreate(Cid, new DateOnly(2026, 9, 7))
              .Add(GilEventCategory.RetainerSale, 5_000, isFreeCompanyChest: false);

        // The events behind that day are long gone (retention deleted them), so the log
        // now only carries a later day. The settled figure must not be recomputed to 0.
        ledger.BackfillFromEvents(new[] { E("2026-09-08T10:00:00+08:00", 9) },
                                  new DateOnly(2026, 9, 9));

        var rows = ledger.For(Cid);
        Assert.Equal(5_000, rows.Single(r => r.Date == new DateOnly(2026, 9, 7)).NetFor(false));
        Assert.Equal(9, rows.Single(r => r.Date == new DateOnly(2026, 9, 8)).NetFor(false));
    }

    [Fact]
    public void Backfill_rebuilds_today_from_the_log_and_keeps_the_wallet_readings()
    {
        var today = new DateOnly(2026, 9, 9);
        var ledger = new DailyLedger();

        // Simulates a session that crashed after the ledger had booked one event but
        // before the two later ones reached the file.
        var partial = ledger.GetOrCreate(Cid, today);
        partial.Add(GilEventCategory.Misc, 1, isFreeCompanyChest: false);
        partial.ObserveTotal(1_234, 1_234);

        ledger.BackfillFromEvents(new[]
        {
            E("2026-09-09T01:00:00+08:00", 1),
            E("2026-09-09T02:00:00+08:00", 2),
            E("2026-09-09T03:00:00+08:00", 4),
        }, today);

        var row = ledger.For(Cid).Single();
        Assert.Equal(7, row.NetFor(false));      // not 8: the stale 1 was cleared first
        Assert.Equal(3, row.EventCountFor(false));
        Assert.Equal(1_234, row.OpenTotal);      // the reading survived the rebuild
    }

    [Fact]
    public void Backfill_reports_exactly_the_characters_whose_rows_moved()
    {
        var ledger = new DailyLedger();
        var touched = ledger.BackfillFromEvents(new[]
        {
            E("2026-09-08T10:00:00+08:00", 1, cid: 1),
            E("2026-09-08T10:00:00+08:00", 1, cid: 2),
        }, new DateOnly(2026, 9, 9));

        Assert.Equal(new ulong[] { 1, 2 }, touched.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void A_round_trip_through_the_file_preserves_every_figure()
    {
        var ledger = new DailyLedger();
        var day = ledger.GetOrCreate(Cid, new DateOnly(2026, 9, 9));
        day.Add(GilEventCategory.RetainerSale, 62, isFreeCompanyChest: false);
        day.Add(GilEventCategory.Teleport, -263, isFreeCompanyChest: false);
        day.Add(GilEventCategory.FreeCompanyChestDeposit, 50_000_000, isFreeCompanyChest: true);
        day.ObserveTotal(102_191_519, 52_191_519);

        var jsonl = ledger.Serialize(Cid);

        var reloaded = new DailyLedger();
        reloaded.LoadFrom(Cid, jsonl);
        var row = reloaded.For(Cid).Single();

        Assert.Equal(new DateOnly(2026, 9, 9), row.Date);
        Assert.Equal(49_999_799, row.NetFor(false));
        Assert.Equal(-201, row.NetFor(true));
        Assert.Equal(3, row.EventCountFor(false));
        Assert.Equal(2, row.EventCountFor(true));
        Assert.Equal(102_191_519, row.OpenFor(false));
        Assert.Equal(52_191_519, row.OpenFor(true));
        Assert.Equal(102_191_519, row.CloseFor(false));
        Assert.Equal(62, row.CategoryTotals(true)[GilEventCategory.RetainerSale]);
    }

    [Fact]
    public void A_day_that_only_ever_had_a_wallet_reading_still_round_trips()
    {
        // This is what makes "the plugin was running and nothing happened" tellable
        // apart from "the plugin was not running": the day has a row, net 0.
        var ledger = new DailyLedger();
        ledger.ObserveTotal(Cid, new DateOnly(2026, 9, 9), 500, 500);

        var reloaded = new DailyLedger();
        reloaded.LoadFrom(Cid, ledger.Serialize(Cid));

        var row = reloaded.For(Cid).Single();
        Assert.Equal(0, row.NetFor(false));
        Assert.Equal(0, row.EventCountFor(false));
        Assert.Equal(500, row.OpenTotal);
    }

    [Fact]
    public void A_malformed_or_unknown_line_is_skipped_instead_of_taking_the_book_down()
    {
        var ledger = new DailyLedger();
        ledger.LoadFrom(Cid, string.Join("\n", new[]
        {
            "not json at all",
            "{\"v\":99,\"date\":\"2026-09-01\"}",                 // a version we do not know
            "{\"v\":1,\"date\":\"nonsense\"}",                    // an unparseable date
            "{\"v\":1,\"date\":\"2026-09-09\",\"events\":1,\"byCategory\":{\"Teleport\":-263}}",
        }));

        var row = ledger.For(Cid).Single();
        Assert.Equal(new DateOnly(2026, 9, 9), row.Date);
        Assert.Equal(-263, row.NetFor(false));
    }

    [Fact]
    public void Numeric_and_unknown_category_keys_are_rejected_not_guessed_at()
    {
        var ledger = new DailyLedger();
        ledger.LoadFrom(Cid,
            "{\"v\":1,\"date\":\"2026-09-09\",\"events\":3," +
            "\"byCategory\":{\"Teleport\":-263,\"3\":999,\"SomethingFromTheFuture\":777}}");

        var row = ledger.For(Cid).Single();
        var totals = row.CategoryTotals(false);
        Assert.Single(totals);
        Assert.Equal(-263, totals[GilEventCategory.Teleport]);
        // "3" would otherwise parse as whichever member sits at ordinal 3.
        Assert.False(totals.ContainsKey(GilEventCategory.RetainerWithdraw));
    }

    [Fact]
    public void Rows_written_without_a_cid_inherit_the_file_they_came_from()
    {
        var ledger = new DailyLedger();
        ledger.LoadFrom(Cid, "{\"v\":1,\"date\":\"2026-09-09\",\"events\":1,\"byCategory\":{\"Misc\":5}}");

        Assert.Equal(Cid, ledger.For(Cid).Single().ContentId);
    }

    [Fact]
    public void The_account_wide_view_folds_characters_together_by_date()
    {
        var ledger = new DailyLedger();
        ledger.Add(E("2026-09-08T10:00:00+08:00", 100, cid: 1));
        ledger.Add(E("2026-09-08T11:00:00+08:00", 250, cid: 2));
        ledger.Add(E("2026-09-09T11:00:00+08:00", -25, cid: 2));
        ledger.ObserveTotal(1, new DateOnly(2026, 9, 8), 9_999, 9_999);

        var merged = ledger.For(null);

        Assert.Equal(2, merged.Count);
        Assert.Equal(350, merged[0].NetFor(false));
        Assert.Equal(-25, merged[1].NetFor(false));
        // Even a date only one character touched goes through Aggregate, so one
        // character's opening total is never shown as if it were the account's.
        Assert.All(merged, r => Assert.Null(r.OpenTotal));
    }

    [Fact]
    public void An_unknown_character_has_no_rows_rather_than_throwing()
    {
        Assert.Empty(new DailyLedger().For(12345));
        Assert.Equal(string.Empty, new DailyLedger().Serialize(12345));
    }

    [Fact]
    public void Empty_days_are_not_written_out()
    {
        var ledger = new DailyLedger();
        ledger.GetOrCreate(Cid, new DateOnly(2026, 9, 9));   // touched but never filled

        Assert.Equal(string.Empty, ledger.Serialize(Cid));
    }

    [Fact]
    public void LocalDayOf_uses_the_offset_carried_by_the_timestamp()
    {
        // The same instant, written in two zones: 2026-09-09 00:30 +08:00 is
        // 2026-09-08 16:30 UTC, and each is booked to its own local day.
        Assert.Equal(new DateOnly(2026, 9, 9),
            DailyLedger.LocalDayOf(DateTimeOffset.Parse("2026-09-09T00:30:00+08:00")));
        Assert.Equal(new DateOnly(2026, 9, 8),
            DailyLedger.LocalDayOf(DateTimeOffset.Parse("2026-09-08T16:30:00+00:00")));
    }
}
