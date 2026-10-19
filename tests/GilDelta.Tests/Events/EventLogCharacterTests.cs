using System.Linq;
using GilDelta.Events;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events;

public class EventLogCharacterTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-05T14:00:00+09:00");

    private static GilEvent E(long amount, ulong contentId, int secondsOffset = 0) =>
        new(T0.AddSeconds(secondsOffset), new WalletId(WalletKind.Self, ""),
            amount, GilEventCategory.Misc, null)
        {
            ContentId = contentId,
        };

    [Fact]
    public void All_holds_the_whole_account()
    {
        var log = new EventLog();
        log.AddRange(new[] { E(100, 1001), E(200, 2002, 1), E(300, 1001, 2) });

        Assert.Equal(3, log.All.Count);
        Assert.Equal(600, log.All.Sum(e => e.Amount));
    }

    [Fact]
    public void For_returns_only_one_characters_records()
    {
        var log = new EventLog();
        log.AddRange(new[] { E(100, 1001), E(200, 2002, 1), E(300, 1001, 2) });

        Assert.Equal(new long[] { 100, 300 }, log.For(1001).Select(e => e.Amount).ToArray());
        Assert.Equal(new long[] { 200 }, log.For(2002).Select(e => e.Amount).ToArray());
    }

    [Fact]
    public void For_an_unseen_character_yields_an_empty_list_rather_than_null()
    {
        var log = new EventLog();
        log.Add(E(100, 1001));

        Assert.Empty(log.For(9999));
    }

    [Fact]
    public void Records_with_no_owner_land_in_the_unknown_bucket()
    {
        var log = new EventLog();
        log.AddRange(new[] { E(100, 0), E(200, 1001, 1) });

        Assert.Single(log.For(0));
        Assert.Equal(100, log.For(0)[0].Amount);
    }

    [Fact]
    public void AddRange_accumulates_instead_of_replacing()
    {
        // Logging in as a second character loads that character's file on top of what
        // is already cached; it must not wipe the first character's records.
        var log = new EventLog();
        log.AddRange(new[] { E(100, 1001) });
        log.AddRange(new[] { E(200, 2002, 1) });

        Assert.Equal(2, log.All.Count);
        Assert.Single(log.For(1001));
        Assert.Single(log.For(2002));
    }

    [Fact]
    public void ContentIds_lists_every_character_with_records()
    {
        var log = new EventLog();
        log.AddRange(new[] { E(100, 1001), E(200, 2002, 1), E(300, 1001, 2) });

        Assert.Equal(new ulong[] { 1001, 2002 }, log.ContentIds.OrderBy(c => c).ToArray());
    }

    [Fact]
    public void Clear_empties_the_per_character_buckets_too()
    {
        var log = new EventLog();
        log.AddRange(new[] { E(100, 1001), E(200, 2002, 1) });

        log.Clear();

        Assert.Empty(log.All);
        Assert.Empty(log.ContentIds);
        Assert.Empty(log.For(1001));
    }
}
