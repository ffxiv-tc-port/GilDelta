using System.Collections.Generic;
using System.IO;
using System.Linq;
using GilDelta.Events;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events;

public class EventStoreSetTests : IDisposable
{
    private readonly string _dir;

    public EventStoreSetTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "GilDeltaTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private string Events => Path.Combine(_dir, "events");

    private static GilEvent E(DateTimeOffset at, long amount, ulong contentId = 0) =>
        new(at, new WalletId(WalletKind.Self, ""), amount, GilEventCategory.Misc, "n")
        {
            ContentId = contentId,
        };

    [Fact]
    public void Each_character_gets_its_own_file()
    {
        var set = new EventStoreSet(Events);
        var t0 = DateTimeOffset.Parse("2026-05-05T14:00:00+09:00");

        set.For(1001).Append(E(t0, 100, 1001));
        set.For(2002).Append(E(t0, 200, 2002));

        Assert.True(File.Exists(Path.Combine(Events, "1001.jsonl")));
        Assert.True(File.Exists(Path.Combine(Events, "2002.jsonl")));
        Assert.Single(File.ReadAllLines(Path.Combine(Events, "1001.jsonl")));
    }

    [Fact]
    public void For_returns_the_same_store_instance_per_character()
    {
        var set = new EventStoreSet(Events);
        Assert.Same(set.For(1001), set.For(1001));
        Assert.NotSame(set.For(1001), set.For(2002));
    }

    [Fact]
    public void KnownContentIds_reads_the_ids_off_the_file_names()
    {
        var set = new EventStoreSet(Events);
        var t0 = DateTimeOffset.Parse("2026-05-05T14:00:00+09:00");
        set.For(2002).Append(E(t0, 200, 2002));
        set.For(1001).Append(E(t0, 100, 1001));

        Assert.Equal(new ulong[] { 1001, 2002 }, set.KnownContentIds().ToArray());
    }

    [Fact]
    public void KnownContentIds_skips_files_that_are_not_a_bare_content_id()
    {
        var set = new EventStoreSet(Events);
        var t0 = DateTimeOffset.Parse("2026-05-05T14:00:00+09:00");
        set.For(1001).Append(E(t0, 100, 1001));

        File.WriteAllText(Path.Combine(Events, "notes.jsonl"), "{}\n");
        File.WriteAllText(Path.Combine(Events, "1001.jsonl.bak"), "{}\n");

        Assert.Equal(new ulong[] { 1001 }, set.KnownContentIds().ToArray());
    }

    [Fact]
    public void LoadAll_attributes_every_row_to_the_character_whose_file_it_is_in()
    {
        var set = new EventStoreSet(Events);
        var t0 = DateTimeOffset.Parse("2026-05-05T14:00:00+09:00");
        set.For(1001).Append(E(t0, 100, 1001));
        set.For(2002).Append(E(t0.AddSeconds(1), 200, 2002));

        var loaded = new EventStoreSet(Events).LoadAll();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(100, loaded.Single(e => e.ContentId == 1001).Amount);
        Assert.Equal(200, loaded.Single(e => e.ContentId == 2002).Amount);
    }

    [Fact]
    public void LoadAll_reports_a_broken_file_and_keeps_going()
    {
        var set = new EventStoreSet(Events);
        var t0 = DateTimeOffset.Parse("2026-05-05T14:00:00+09:00");
        set.For(1001).Append(E(t0, 100, 1001));

        // An exclusively-locked file is the failure shape that actually reaches the
        // catch. (A directory in the file's place would NOT: File.Exists returns false
        // for it and LoadAll would quietly yield nothing, which is indistinguishable
        // from an empty log and would make this test pass for the wrong reason.)
        var blocked = Path.Combine(Events, "2002.jsonl");
        using var handle = new FileStream(blocked, FileMode.Create, FileAccess.Write, FileShare.None);

        var failures = new List<ulong>();
        var loaded = new EventStoreSet(Events).LoadAll((cid, _) => failures.Add(cid));

        Assert.Single(loaded);
        Assert.Equal(new ulong[] { 2002 }, failures.ToArray());
    }
}
