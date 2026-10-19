using System.Collections.Generic;
using System.IO;
using System.Linq;
using GilDelta.Events;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events;

/// <summary>
/// Owner attribution: how a gil record learns which character it belongs to, and what
/// happens to records written before that field existed.
/// </summary>
public class EventStoreCharacterTests : IDisposable
{
    private readonly string _dir;

    public EventStoreCharacterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "GilDeltaTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private string Path_ => Path.Combine(_dir, "1001.jsonl");

    private static GilEvent E(DateTimeOffset at, long amount, ulong contentId = 0) =>
        new(at, new WalletId(WalletKind.Self, ""), amount, GilEventCategory.Misc, "n")
        {
            ContentId = contentId,
        };

    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-05T14:00:00+09:00");

    [Fact]
    public void Append_writes_the_owning_character()
    {
        var store = new EventStore(Path_, 1001);

        store.Append(E(T0, 100, 1001));

        Assert.Contains("\"cid\":1001", File.ReadAllLines(Path_)[0]);
    }

    [Fact]
    public void Append_falls_back_to_the_stores_owner_when_the_event_carries_none()
    {
        var store = new EventStore(Path_, 1001);

        store.Append(E(T0, 100));

        Assert.Contains("\"cid\":1001", File.ReadAllLines(Path_)[0]);
    }

    [Fact]
    public void An_unknown_owner_leaves_the_field_out_entirely()
    {
        // Byte-compatible with what v1 wrote, so an older build of the plugin can
        // still read rows this build produces.
        var store = new EventStore(Path.Combine(_dir, "0.jsonl"));

        store.Append(E(T0, 100));

        var line = File.ReadAllLines(Path.Combine(_dir, "0.jsonl"))[0];
        Assert.DoesNotContain("cid", line);
        Assert.Contains("\"v\":1", line);
    }

    [Fact]
    public void A_round_trip_keeps_the_owner()
    {
        var store = new EventStore(Path_, 1001);
        store.Append(E(T0, 100, 1001));

        var loaded = store.LoadAll().Single();

        Assert.Equal(1001ul, loaded.ContentId);
    }

    [Fact]
    public void Legacy_rows_are_attributed_to_the_file_they_live_in()
    {
        // The file name is a recorded owner, not a guess about who is logged in now,
        // so pre-multi-character rows keep showing up under the character whose log
        // they were written into.
        File.WriteAllLines(Path_, new[]
        {
            """{"v":1,"ts":"2026-05-05T14:00:00+09:00","wallet":{"kind":"Self","id":""},"amount":100,"category":"Misc","note":"legacy"}""",
        });

        var loaded = new EventStore(Path_, 1001).LoadAll().Single();

        Assert.Equal(1001ul, loaded.ContentId);
        Assert.Equal(100, loaded.Amount);
    }

    [Fact]
    public void Legacy_rows_in_the_unknown_file_stay_unknown()
    {
        var path = Path.Combine(_dir, "0.jsonl");
        File.WriteAllLines(path, new[]
        {
            """{"v":1,"ts":"2026-05-05T14:00:00+09:00","wallet":{"kind":"Self","id":""},"amount":100,"category":"Misc","note":"legacy"}""",
        });

        Assert.Equal(0ul, new EventStore(path).LoadAll().Single().ContentId);
    }

    [Fact]
    public void A_row_that_carries_an_owner_wins_over_the_file_name()
    {
        File.WriteAllLines(Path_, new[]
        {
            """{"v":1,"ts":"2026-05-05T14:00:00+09:00","wallet":{"kind":"Self","id":""},"amount":100,"category":"Misc","note":"n","cid":2002}""",
        });

        Assert.Equal(2002ul, new EventStore(Path_, 1001).LoadAll().Single().ContentId);
    }

    [Fact]
    public void Reclassify_does_not_lose_the_owner()
    {
        // The inference rules build a brand new event and know nothing about who owns
        // it — without an explicit carry-over a reclassify would dump every rewritten
        // row into the unknown bucket.
        var store = new EventStore(Path_, 1001);
        store.Append(E(T0, 100, 1001));

        store.Reclassify(T0.AddSeconds(-1), T0.AddSeconds(1),
            new Inferrer(new IInferenceRule[] { new AlwaysRepair() }));

        var loaded = store.LoadAll().Single();
        Assert.Equal(GilEventCategory.Repair, loaded.Category);
        Assert.Equal(1001ul, loaded.ContentId);
    }

    private sealed class AlwaysRepair : IInferenceRule
    {
        public bool TryClassify(WalletDiff diff, GameContext ctx,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out GilEvent? ev)
        {
            ev = new GilEvent(diff.At, diff.Id, diff.Delta, GilEventCategory.Repair, "rewritten");
            return true;
        }
    }
}
