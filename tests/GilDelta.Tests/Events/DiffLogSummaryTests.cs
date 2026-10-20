using GilDelta.Events;

namespace GilDelta.Tests.Events;

public class DiffLogSummaryTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Nothing_pending_takes_null()
    {
        var s = new DiffLogSummary(TimeSpan.FromMinutes(1));
        Assert.False(s.HasPending);
        Assert.False(s.IsDue(T0.AddHours(1)));
        Assert.Null(s.Take(T0));
    }

    [Fact]
    public void Window_sums_per_category_and_clears()
    {
        var s = new DiffLogSummary(TimeSpan.FromMinutes(1));
        Assert.Null(s.Add(42, GilEventCategory.RetainerSale, 30_000, T0));
        Assert.Null(s.Add(42, GilEventCategory.RetainerSale, 1_500, T0.AddSeconds(5)));
        Assert.Null(s.Add(42, GilEventCategory.Teleport, -241, T0.AddSeconds(10)));

        Assert.False(s.IsDue(T0.AddSeconds(59)));
        Assert.True(s.IsDue(T0.AddSeconds(60)));

        var line = s.Take(T0.AddSeconds(60));
        Assert.Equal(
            DiffLogSummary.Prefix + " cid=42，60 秒內 3 筆，淨額 +31,259；RetainerSale×2 +31,500、Teleport×1 -241",
            line);
        Assert.False(s.HasPending);
        Assert.Null(s.Take(T0.AddSeconds(61)));
    }

    [Fact]
    public void A_different_character_closes_the_open_window_first()
    {
        var s = new DiffLogSummary(TimeSpan.FromMinutes(1));
        Assert.Null(s.Add(1, GilEventCategory.Misc, 100, T0));
        var closed = s.Add(2, GilEventCategory.Misc, -100, T0.AddSeconds(3));

        Assert.Equal(DiffLogSummary.Prefix + " cid=1，3 秒內 1 筆，淨額 +100；Misc×1 +100", closed);
        Assert.Equal(DiffLogSummary.Prefix + " cid=2，0 秒內 1 筆，淨額 -100；Misc×1 -100", s.Take(T0.AddSeconds(3)));
    }

    [Fact]
    public void Net_zero_is_written_without_a_sign()
    {
        var s = new DiffLogSummary(TimeSpan.FromMinutes(1));
        s.Add(7, GilEventCategory.CharacterTransfer, 500, T0);
        s.Add(7, GilEventCategory.CharacterTransfer, -500, T0);
        Assert.EndsWith("淨額 0；CharacterTransfer×2 0", s.Take(T0));
    }
}
