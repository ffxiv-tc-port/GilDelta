using System.Collections.Generic;
using GilDelta.Wallet;
using GilDelta.Windows;

namespace GilDelta.Tests.Windows;

/// <summary>
/// The widget and the dashboard ask for their context every frame; the cache is what
/// turns that into "rebuild when something changed". A false hit leaves a stale figure on
/// screen and a false miss brings the per-frame rebuild back, so both directions are tested.
/// </summary>
public class ViewCacheTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-05T18:00:30+09:00");

    private static ViewInputs Inputs(
        long events = 1,
        long wallets = 1,
        ulong? scope = null,
        bool excludeFc = false,
        IReadOnlyDictionary<ulong, string>? labels = null,
        DateTimeOffset? at = null)
    {
        var now = at ?? Now;
        return new ViewInputs(
            EventsVersion: events,
            WalletsVersion: wallets,
            LedgerVersion: 0,
            ActiveContentId: 1001,
            Scope: scope,
            ExcludeFreeCompanyChest: excludeFc,
            Labels: labels,
            Characters: null,
            Minute: ViewInputs.MinuteOf(now),
            Offset: now.Offset);
    }

    private static List<GilDelta.Wallet.Wallet> Reading(long self) =>
        new() { new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Self, ""), self) };

    [Fact]
    public void Nothing_is_cached_before_the_first_store()
    {
        var cache = new ViewCache<object>();

        Assert.False(cache.TryGet(Inputs(), Reading(1), out var value));
        Assert.Null(value);
    }

    [Fact]
    public void A_new_reading_list_with_the_same_balances_is_a_hit()
    {
        // The watcher publishes a fresh list every tick; comparing it by reference would
        // make every frame a miss.
        var cache = new ViewCache<object>();
        var built = new object();
        cache.Store(Inputs(), Reading(1000), built);

        Assert.True(cache.TryGet(Inputs(), Reading(1000), out var value));
        Assert.Same(built, value);
    }

    [Fact]
    public void A_moved_balance_is_a_miss()
    {
        var cache = new ViewCache<object>();
        cache.Store(Inputs(), Reading(1000), new object());

        Assert.False(cache.TryGet(Inputs(), Reading(1001), out _));
        Assert.False(cache.TryGet(Inputs(), null, out _));
    }

    [Fact]
    public void Every_changed_input_is_a_miss()
    {
        var labels = new Dictionary<ulong, string> { [1001] = "A" };
        var cache = new ViewCache<object>();
        cache.Store(Inputs(labels: labels), Reading(1), new object());

        Assert.True(cache.TryGet(Inputs(labels: labels), Reading(1), out _));
        Assert.False(cache.TryGet(Inputs(events: 2, labels: labels), Reading(1), out _));
        Assert.False(cache.TryGet(Inputs(wallets: 2, labels: labels), Reading(1), out _));
        Assert.False(cache.TryGet(Inputs(scope: 1001, labels: labels), Reading(1), out _));
        Assert.False(cache.TryGet(Inputs(excludeFc: true, labels: labels), Reading(1), out _));

        // Compared by reference: the owner hands out a new map when the labels change.
        var sameContents = new Dictionary<ulong, string> { [1001] = "A" };
        Assert.False(cache.TryGet(Inputs(labels: sameContents), Reading(1), out _));
    }

    [Fact]
    public void The_next_minute_is_a_miss_and_the_same_minute_is_not()
    {
        // The minute is what rolls "today" over at midnight without any data changing.
        var cache = new ViewCache<object>();
        cache.Store(Inputs(), Reading(1), new object());

        Assert.True(cache.TryGet(Inputs(at: Now.AddSeconds(29)), Reading(1), out _));
        Assert.False(cache.TryGet(Inputs(at: Now.AddSeconds(30)), Reading(1), out _));

        // Same wall-clock minute under another offset: a different "today" boundary.
        var otherOffset = new DateTimeOffset(Now.DateTime, TimeSpan.FromHours(8));
        Assert.False(cache.TryGet(Inputs(at: otherOffset), Reading(1), out _));
    }

    [Fact]
    public void A_null_context_is_remembered_too()
    {
        // "Waiting for game state" is an answer as well; it must not rebuild every frame.
        var cache = new ViewCache<object>();
        cache.Store(Inputs(), null, null);

        Assert.True(cache.TryGet(Inputs(), null, out var value));
        Assert.Null(value);
    }

    [Fact]
    public void A_hit_does_not_allocate()
    {
        var cache = new ViewCache<object>();
        var inputs = Inputs();
        cache.Store(inputs, Reading(1000), new object());
        var reading = Reading(1000);

        for (var i = 0; i < 1_000; i++) cache.TryGet(inputs, reading, out _);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++) cache.TryGet(inputs, reading, out _);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 1_024, $"{allocated} bytes over 10,000 hits");
    }
}
