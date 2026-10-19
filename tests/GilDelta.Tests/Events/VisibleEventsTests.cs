using System.Collections.Generic;
using System.Linq;
using GilDelta.Events;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events;

/// <summary>
/// The memo has to hand out exactly what filtering the whole log from scratch would, after
/// every kind of edit the log supports; a stale list means a wrong total on screen.
/// </summary>
public class VisibleEventsTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-10-13T14:00:00+08:00");

    private static GilEvent E(int n, WalletKind kind, ulong cid, bool suspect = false) =>
        new(T0.AddSeconds(n), new WalletId(kind, kind == WalletKind.Retainer ? "R" + (n % 3) : kind == WalletKind.FreeCompanyChest ? "FC" : ""),
            n, GilEventCategory.Misc, null)
        { ContentId = cid, Suspect = suspect };

    // The filter the views used before the memo: suspect records out, then the chest.
    private static IReadOnlyList<GilEvent> FromScratch(IReadOnlyList<GilEvent> events, bool excludeFc) =>
        WalletExclusion.Events(events.Where(e => !e.Suspect).ToList(), excludeFc);

    [Fact]
    public void Matches_filtering_from_scratch_through_random_edits()
    {
        var rng = new Random(20261014);
        var log = new EventLog();
        var all = new VisibleEvents();
        var one = new VisibleEvents();
        var excludeFc = true;
        var n = 0;

        for (var step = 0; step < 4000; step++)
        {
            var op = rng.Next(100);
            if (op < 70)
            {
                var kind = (WalletKind)rng.Next(3);
                log.Add(E(n++, kind, (ulong)(1 + rng.Next(3)), rng.Next(25) == 0));
            }
            else if (op < 82 && log.All.Count > 0)
            {
                var victim = log.All[rng.Next(log.All.Count)];
                Assert.True(log.Replace(victim, victim with { Suspect = !victim.Suspect }));
            }
            else if (op < 86 && log.All.Count > 0)
            {
                log.Prune((ulong)(1 + rng.Next(3)), T0.AddSeconds(rng.Next(Math.Max(1, n))));
            }
            else if (op < 88)
            {
                log.Clear();
            }
            else if (op < 92)
            {
                excludeFc = !excludeFc;
            }

            // Skipping some frames: several appends folded in at once must come out the same.
            if (rng.Next(3) == 0) continue;

            var got = all.Get(log.All, log.RewriteVersion, excludeFc);
            Assert.Equal(FromScratch(log.All, excludeFc), got);
            if (FromScratch(log.All, excludeFc).Count == log.All.Count) Assert.Same(log.All, got);

            var mine = log.For(2);
            Assert.Equal(FromScratch(mine, excludeFc), one.Get(mine, log.RewriteVersion, excludeFc));
        }
    }

    [Fact]
    public void Nothing_dropped_hands_out_the_log_itself()
    {
        var log = new EventLog();
        for (var i = 0; i < 10; i++) log.Add(E(i, WalletKind.Self, 1));
        Assert.Same(log.All, new VisibleEvents().Get(log.All, log.RewriteVersion, excludeFreeCompanyChest: true));
    }

    [Fact]
    public void An_append_does_not_copy_the_log_again()
    {
        var log = new EventLog();
        for (var i = 0; i < 20_000; i++) log.Add(E(i, i % 600 == 0 ? WalletKind.FreeCompanyChest : WalletKind.Self, 1));
        var memo = new VisibleEvents();
        var first = memo.Get(log.All, log.RewriteVersion, true);
        log.Add(E(20_000, WalletKind.Self, 1));
        memo.Get(log.All, log.RewriteVersion, true);

        log.Add(E(20_001, WalletKind.Self, 1));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var second = memo.Get(log.All, log.RewriteVersion, true);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Same(first, second);
        Assert.True(bytes < 1024, $"{bytes} B for one appended record");
        Assert.Equal(FromScratch(log.All, true), second);
    }
}
