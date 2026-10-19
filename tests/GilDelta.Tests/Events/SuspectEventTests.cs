using System.Collections.Generic;
using System.IO;
using System.Linq;
using GilDelta.Events;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events;

/// <summary>
/// "Exclude this record from the statistics" — the flag, the way it persists, and the
/// adjustment it has to make to a day that is already settled.
/// </summary>
public class SuspectEventTests : IDisposable
{
    private const ulong Cid = 18014409509708868;

    private readonly string _dir;

    public SuspectEventTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "GilDeltaTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private string Path_ => Path.Combine(_dir, "events.jsonl");

    private static GilEvent E(string timestamp, long amount,
                              GilEventCategory cat = GilEventCategory.Misc,
                              WalletKind kind = WalletKind.Self,
                              ulong cid = Cid) =>
        new(DateTimeOffset.Parse(timestamp),
            new WalletId(kind, kind == WalletKind.FreeCompanyChest ? "FC" : ""),
            amount, cat, null)
        { ContentId = cid };

    // ─── The flag itself ───────────────────────────────────────────────────────

    [Fact]
    public void A_new_record_is_not_excluded()
    {
        Assert.False(E("2026-09-09T10:00:00+08:00", 100).Suspect);
    }

    [Fact]
    public void The_flag_survives_a_save_and_load_round_trip()
    {
        var store = new EventStore(Path_, Cid);
        store.Append(E("2026-09-09T10:00:00+08:00", 100) with { Suspect = true });
        store.Append(E("2026-09-09T11:00:00+08:00", 200));

        var loaded = store.LoadAll().ToList();

        Assert.Equal(2, loaded.Count);
        Assert.True(loaded[0].Suspect);
        Assert.False(loaded[1].Suspect);
    }

    [Fact]
    public void An_unflagged_row_gains_no_new_field_on_disk()
    {
        // 🔴 The field is written only when it is true, so an untouched log stays
        // byte-for-byte what the previous build produced - which is what lets the
        // schema version stay at 1.
        var store = new EventStore(Path_, Cid);
        store.Append(E("2026-09-09T10:00:00+08:00", 100));

        Assert.DoesNotContain("suspect", File.ReadAllText(Path_));
    }

    [Fact]
    public void A_row_written_before_the_flag_existed_loads_as_not_excluded()
    {
        File.WriteAllText(Path_,
            "{\"v\":1,\"ts\":\"2026-09-09T10:00:00+08:00\",\"wallet\":{\"kind\":\"Self\",\"id\":\"\"}," +
            "\"amount\":100,\"category\":\"Misc\"}\n");

        var loaded = new EventStore(Path_, Cid).LoadAll().Single();

        Assert.False(loaded.Suspect);
        Assert.Equal(100, loaded.Amount);
    }

    [Fact]
    public void Reclassifying_does_not_quietly_un_exclude_a_record()
    {
        // The rules build a fresh event and know nothing about the user's own decision,
        // so it has to be carried across by hand, exactly like ContentId.
        var store = new EventStore(Path_, Cid);
        store.Append(E("2026-09-09T10:00:00+08:00", 100) with { Suspect = true });

        store.Reclassify(
            DateTimeOffset.Parse("2026-01-01T00:00:00+08:00"),
            DateTimeOffset.Parse("2027-01-01T00:00:00+08:00"),
            new Inferrer(new IInferenceRule[] { new RewriteToMisc() }));

        var loaded = store.LoadAll().Single();
        Assert.True(loaded.Suspect);
        Assert.Equal(Cid, loaded.ContentId);
    }

    // ─── EventLog.Replace ──────────────────────────────────────────────────────

    [Fact]
    public void Replace_updates_the_flat_list_and_the_character_bucket_together()
    {
        var log = new EventLog();
        var ev = E("2026-09-09T10:00:00+08:00", 100);
        log.Add(ev);

        Assert.True(log.Replace(ev, ev with { Suspect = true }));

        Assert.True(log.All.Single().Suspect);
        Assert.True(log.For(Cid).Single().Suspect);
    }

    [Fact]
    public void Replace_reports_a_record_that_is_not_there()
    {
        var log = new EventLog();
        log.Add(E("2026-09-09T10:00:00+08:00", 100));

        Assert.False(log.Replace(E("2026-09-09T23:00:00+08:00", 999), E("2026-09-09T23:00:00+08:00", 999)));
    }

    [Fact]
    public void Replace_refuses_to_move_a_record_to_another_character()
    {
        // It would land in the wrong bucket and the two views would disagree for good.
        var log = new EventLog();
        var ev = E("2026-09-09T10:00:00+08:00", 100);
        log.Add(ev);

        Assert.False(log.Replace(ev, ev with { ContentId = 42 }));
        Assert.Single(log.For(Cid));
        Assert.Empty(log.For(42));
    }

    // ─── The settled day ───────────────────────────────────────────────────────

    [Fact]
    public void Retract_exactly_reverses_Add()
    {
        var day = new DailySummary(Cid, new DateOnly(2026, 9, 9));
        day.Add(E("2026-09-09T10:00:00+08:00", 100, GilEventCategory.NpcShopSell));
        day.Add(E("2026-09-09T11:00:00+08:00", 50_000_000, GilEventCategory.FreeCompanyChestDeposit,
                  WalletKind.FreeCompanyChest));

        day.Retract(E("2026-09-09T11:00:00+08:00", 50_000_000, GilEventCategory.FreeCompanyChestDeposit,
                      WalletKind.FreeCompanyChest));

        Assert.Equal(100, day.Net);
        Assert.Equal(0, day.FreeCompanyNet);
        Assert.Equal(1, day.EventCount);
        Assert.Equal(0, day.FreeCompanyEventCount);

        // The category is removed rather than left sitting at 0: "no chest deposits" and
        // "chest deposits that came to nothing" are different claims.
        Assert.DoesNotContain(
            GilEventCategory.FreeCompanyChestDeposit,
            day.CategoryTotals(excludeFreeCompanyChest: false).Keys);
    }

    [Fact]
    public void Retracting_leaves_the_days_wallet_readings_alone()
    {
        // The opening / closing totals are what the wallet actually held. Excluding a
        // record does not change what was in the purse that day.
        var day = new DailySummary(Cid, new DateOnly(2026, 9, 9));
        day.ObserveTotal(1_000, 1_000);
        day.Add(E("2026-09-09T10:00:00+08:00", 100));
        day.ObserveTotal(1_100, 1_100);

        day.Retract(E("2026-09-09T10:00:00+08:00", 100));

        Assert.Equal(1_000, day.OpenTotal);
        Assert.Equal(1_100, day.CloseTotal);
        Assert.Equal(0, day.Net);
    }

    [Fact]
    public void Excluding_and_restoring_a_record_leaves_the_day_where_it_started()
    {
        var ledger = new DailyLedger();
        var ev = E("2026-09-09T10:00:00+08:00", 50_000_000, GilEventCategory.FreeCompanyChestDeposit,
                   WalletKind.FreeCompanyChest);
        ledger.Add(E("2026-09-09T09:00:00+08:00", 250, GilEventCategory.NpcShopSell));
        ledger.Add(ev);

        Assert.Equal(50_000_250, ledger.For(Cid).Single().Net);

        Assert.True(ledger.Retract(ev));
        Assert.Equal(250, ledger.For(Cid).Single().Net);
        Assert.Equal(1, ledger.For(Cid).Single().EventCount);

        ledger.Reinstate(ev);
        Assert.Equal(50_000_250, ledger.For(Cid).Single().Net);
        Assert.Equal(2, ledger.For(Cid).Single().EventCount);
    }

    [Fact]
    public void Retracting_from_a_day_that_has_no_row_reports_that_it_did_nothing()
    {
        var ledger = new DailyLedger();
        Assert.False(ledger.Retract(E("2026-09-09T10:00:00+08:00", 100)));
    }

    [Fact]
    public void An_excluded_record_is_never_booked_by_the_backfill()
    {
        // 🔴 Today's row is rebuilt from the event log at every start. Without this the
        // record would come straight back the next time the game launched, and only the
        // in-memory session would have looked right.
        var today = new DateOnly(2026, 9, 9);
        var ledger = new DailyLedger();

        ledger.BackfillFromEvents(new[]
        {
            E("2026-09-09T09:00:00+08:00", 250),
            E("2026-09-09T10:00:00+08:00", 50_000_000) with { Suspect = true },
        }, today);

        var row = ledger.For(Cid).Single();
        Assert.Equal(250, row.Net);
        Assert.Equal(1, row.EventCount);
    }

    [Fact]
    public void An_excluded_record_does_not_by_itself_make_a_day_observed()
    {
        // A day whose only record is one the user threw out was not really observed, and
        // claiming otherwise would put a "net 0" cell on the chart where a "no records"
        // cell belongs.
        var ledger = new DailyLedger();

        ledger.BackfillFromEvents(new[]
        {
            E("2026-09-09T10:00:00+08:00", 50_000_000) with { Suspect = true },
        }, new DateOnly(2026, 9, 9));

        Assert.Empty(ledger.For(Cid));
    }

    [Fact]
    public void The_retraction_is_what_gets_written_out()
    {
        var ledger = new DailyLedger();
        var ev = E("2026-09-09T10:00:00+08:00", 50_000_000, GilEventCategory.FreeCompanyChestDeposit,
                   WalletKind.FreeCompanyChest);
        ledger.Add(E("2026-09-09T09:00:00+08:00", 250, GilEventCategory.NpcShopSell));
        ledger.Add(ev);
        ledger.Retract(ev);

        var reloaded = new DailyLedger();
        reloaded.LoadFrom(Cid, ledger.Serialize(Cid));

        var row = reloaded.For(Cid).Single();
        Assert.Equal(250, row.Net);
        Assert.Equal(1, row.EventCount);
        Assert.Equal(0, row.FreeCompanyNet);
    }

    /// <summary>Rewrites everything, so the reclassify definitely rebuilt the record.</summary>
    private sealed class RewriteToMisc : IInferenceRule
    {
        public bool TryClassify(
            WalletDiff diff, GameContext ctx,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out GilEvent? ev)
        {
            ev = new GilEvent(diff.At, diff.Id, diff.Delta, GilEventCategory.Misc, "rewritten");
            return true;
        }
    }
}
