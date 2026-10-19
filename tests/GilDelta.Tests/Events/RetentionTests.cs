using System.IO;
using System.Linq;
using GilDelta.Events;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events;

/// <summary>
/// Retention deletes user data permanently, so every boundary here errs towards
/// keeping records.
/// </summary>
public class RetentionTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-07T23:00:00+08:00");

    private readonly string _dir;

    public RetentionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "GilDeltaTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private static GilEvent E(DateTimeOffset at, long amount, ulong contentId) =>
        new(at, new WalletId(WalletKind.Self, ""), amount, GilEventCategory.Misc, "n")
        {
            ContentId = contentId,
        };

    // --- the policy itself ---

    [Fact]
    public void Zero_days_means_nothing_is_ever_deleted()
    {
        // The default, and therefore what every existing user has: zero data loss.
        Assert.Null(Retention.CutoffFor(0, Now));
    }

    [Fact]
    public void A_negative_window_also_deletes_nothing()
    {
        Assert.Null(Retention.CutoffFor(-30, Now));
    }

    [Fact]
    public void A_positive_window_cuts_off_that_many_days_back()
    {
        Assert.Equal(Now.AddDays(-30), Retention.CutoffFor(30, Now));
    }

    // --- the boundary ---

    [Fact]
    public void A_record_exactly_on_the_boundary_is_kept()
    {
        var cutoff = Retention.CutoffFor(30, Now)!.Value;
        var log = new EventLog();
        log.Add(E(cutoff, 100, 1001));

        Assert.Equal(0, log.Prune(1001, cutoff));
        Assert.Single(log.For(1001));
    }

    [Fact]
    public void A_record_one_second_older_than_the_boundary_goes()
    {
        var cutoff = Retention.CutoffFor(30, Now)!.Value;
        var log = new EventLog();
        log.Add(E(cutoff.AddSeconds(-1), 100, 1001));

        Assert.Equal(1, log.Prune(1001, cutoff));
        Assert.Empty(log.For(1001));
    }

    [Fact]
    public void Prune_removes_from_the_account_wide_list_as_well_as_the_bucket()
    {
        var cutoff = Retention.CutoffFor(30, Now)!.Value;
        var log = new EventLog();
        log.Add(E(cutoff.AddDays(-1), 100, 1001));   // out
        log.Add(E(Now, 200, 1001));                  // in

        Assert.Equal(1, log.Prune(1001, cutoff));
        Assert.Single(log.All);
        Assert.Equal(200, log.All[0].Amount);
    }

    [Fact]
    public void Prune_returns_zero_when_nothing_is_old_enough()
    {
        // 0 removed is what tells the caller not to rewrite the file and not to log.
        var cutoff = Retention.CutoffFor(30, Now)!.Value;
        var log = new EventLog();
        log.Add(E(Now, 100, 1001));

        Assert.Equal(0, log.Prune(1001, cutoff));
    }

    [Fact]
    public void Prune_on_an_unseen_character_is_a_no_op()
    {
        Assert.Equal(0, new EventLog().Prune(9999, Now));
    }

    // --- per character ---

    [Fact]
    public void Pruning_one_character_leaves_the_others_alone()
    {
        var cutoff = Retention.CutoffFor(30, Now)!.Value;
        var log = new EventLog();
        log.Add(E(cutoff.AddDays(-5), 100, 1001));
        log.Add(E(cutoff.AddDays(-5), 200, 2002));

        Assert.Equal(1, log.Prune(1001, cutoff));

        Assert.Empty(log.For(1001));
        Assert.Single(log.For(2002));
        Assert.Single(log.All);
    }

    [Fact]
    public void The_unknown_owner_bucket_is_pruned_too()
    {
        var cutoff = Retention.CutoffFor(30, Now)!.Value;
        var log = new EventLog();
        log.Add(E(cutoff.AddDays(-5), 100, 0));
        log.Add(E(Now, 200, 0));

        Assert.Equal(1, log.Prune(0, cutoff));
        Assert.Single(log.For(0));
    }

    // --- rewrite ---

    [Fact]
    public void A_rewritten_file_loads_back_with_exactly_the_kept_records()
    {
        var path = Path.Combine(_dir, "1001.jsonl");
        var store = new EventStore(path, 1001);
        var cutoff = Retention.CutoffFor(30, Now)!.Value;

        store.Append(E(cutoff.AddDays(-10), 100, 1001));  // out
        store.Append(E(cutoff.AddDays(-1), 200, 1001));   // out
        store.Append(E(Now, 300, 1001));                  // in

        var log = new EventLog();
        log.LoadFromStore(store);
        Assert.Equal(2, log.Prune(1001, cutoff));

        // What the plugin does: serialize on the framework thread, write it out.
        GilDelta.Storage.AtomicFile.WriteAllText(path, store.SerializeAll(log.For(1001)));

        var reloaded = new EventStore(path, 1001).LoadAll().ToList();
        Assert.Single(reloaded);
        Assert.Equal(300, reloaded[0].Amount);
        Assert.Equal(1001ul, reloaded[0].ContentId);
    }

    [Fact]
    public void SerializeAll_of_an_empty_log_produces_an_empty_file_that_still_loads()
    {
        var path = Path.Combine(_dir, "1001.jsonl");
        var store = new EventStore(path, 1001);
        store.Append(E(Now, 100, 1001));

        GilDelta.Storage.AtomicFile.WriteAllText(path, store.SerializeAll(Array.Empty<GilEvent>()));

        Assert.Equal(string.Empty, File.ReadAllText(path));
        Assert.Empty(new EventStore(path, 1001).LoadAll());
    }

    [Fact]
    public void SerializeAll_matches_what_Append_writes()
    {
        // The rewrite must not quietly change the on-disk format.
        var appendPath = Path.Combine(_dir, "append.jsonl");
        var appendStore = new EventStore(appendPath, 1001);
        var events = new[] { E(Now, 100, 1001), E(Now.AddSeconds(1), -50, 1001) };
        foreach (var ev in events) appendStore.Append(ev);

        Assert.Equal(File.ReadAllText(appendPath), appendStore.SerializeAll(events));
    }
}
