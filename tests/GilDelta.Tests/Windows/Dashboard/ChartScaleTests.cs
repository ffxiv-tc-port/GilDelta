using System.Collections.Generic;
using GilDelta.Events;
using GilDelta.Windows.Dashboard;

namespace GilDelta.Tests.Windows.Dashboard;

public class ChartScaleTests
{
    private static bool[] AllObserved(int n)
    {
        var flags = new bool[n];
        for (var i = 0; i < n; i++) flags[i] = true;
        return flags;
    }

    [Fact]
    public void A_single_outlying_day_is_clipped_instead_of_setting_the_scale()
    {
        // Three days: two around five million, one at fifty million.
        var nets = new long[] { -5_286_161, -5_550_627, 49_999_774 };

        var cap = ChartScale.Cap(nets, AllObserved(3), fullScale: false);

        Assert.Equal(5_550_627, cap);
        Assert.True(nets[2] > cap);   // the outlier is what gets the clipped marker
    }

    [Fact]
    public void Full_scale_hands_the_maximum_straight_back()
    {
        var nets = new long[] { -5_286_161, -5_550_627, 49_999_774 };
        Assert.Equal(49_999_774, ChartScale.Cap(nets, AllObserved(3), fullScale: true));
    }

    [Fact]
    public void Fewer_than_three_moving_days_has_no_percentile_worth_the_name()
    {
        Assert.Equal(900, ChartScale.Cap(new long[] { 100, 900 }, AllObserved(2), false));
        Assert.Equal(900, ChartScale.Cap(new long[] { 900 }, AllObserved(1), false));
    }

    [Fact]
    public void Nothing_is_clipped_when_the_days_are_already_comparable()
    {
        // Largest is within 2x of the 90th percentile, so clipping would only add a
        // marker to a bar that was perfectly readable.
        var nets = new long[] { 100, 150, 180 };
        Assert.Equal(180, ChartScale.Cap(nets, AllObserved(3), false));
    }

    [Fact]
    public void Unobserved_and_zero_days_never_influence_the_scale()
    {
        var nets = new long[] { 999_999_999, 100, 0, 500 };
        var observed = new[] { false, true, true, true };

        // The huge value sits on an unobserved day and must be ignored entirely;
        // the zero day is not a "moving" day either, leaving two values.
        Assert.Equal(500, ChartScale.Cap(nets, observed, false));
    }

    [Fact]
    public void An_all_quiet_range_still_returns_something_safe_to_divide_by()
    {
        Assert.Equal(1, ChartScale.Cap(new long[] { 0, 0, 0 }, AllObserved(3), false));
        Assert.Equal(1, ChartScale.Cap(new long[] { 5, 5 }, new[] { false, false }, false));
        Assert.Equal(1, ChartScale.Cap(System.Array.Empty<long>(), System.Array.Empty<bool>(), false));
    }

    [Fact]
    public void Auto_range_falls_back_to_the_minimum_when_nothing_has_been_recorded()
    {
        Assert.Equal(Configuration.ChartRangeAutoMin,
            ChartScale.AutoRangeDays(System.Array.Empty<DailySummary>(), new DateOnly(2026, 9, 9)));
    }

    [Fact]
    public void Auto_range_never_shrinks_below_the_minimum_for_a_few_days_of_history()
    {
        // Three days of records: the window still opens at the minimum rather than
        // drawing a three-column chart.
        var rows = new List<DailySummary>
        {
            new(1, new DateOnly(2026, 9, 7)),
            new(1, new DateOnly(2026, 9, 8)),
            new(1, new DateOnly(2026, 9, 9)),
        };

        Assert.Equal(Configuration.ChartRangeAutoMin,
            ChartScale.AutoRangeDays(rows, new DateOnly(2026, 9, 9)));
    }

    [Fact]
    public void Auto_range_spans_from_the_oldest_record_to_today()
    {
        var rows = new List<DailySummary>
        {
            new(1, new DateOnly(2026, 8, 25)),
            new(1, new DateOnly(2026, 9, 9)),
        };

        Assert.Equal(16, ChartScale.AutoRangeDays(rows, new DateOnly(2026, 9, 9)));
    }

    [Fact]
    public void Auto_range_is_capped_so_an_old_install_does_not_draw_a_column_per_day()
    {
        var rows = new List<DailySummary> { new(1, new DateOnly(2000, 1, 1)) };

        Assert.Equal(Configuration.ChartRangeAutoMax,
            ChartScale.AutoRangeDays(rows, new DateOnly(2026, 9, 9)));
    }

    [Fact]
    public void A_record_dated_in_the_future_does_not_produce_a_negative_window()
    {
        var rows = new List<DailySummary> { new(1, new DateOnly(2027, 1, 1)) };

        Assert.Equal(Configuration.ChartRangeAutoMin,
            ChartScale.AutoRangeDays(rows, new DateOnly(2026, 9, 9)));
    }
}
