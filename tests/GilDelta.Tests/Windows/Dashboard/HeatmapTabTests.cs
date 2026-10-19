using System.Collections.Generic;
using GilDelta.Windows.Dashboard;

namespace GilDelta.Tests.Windows.Dashboard;

/// <summary>
/// The heatmap's cell-to-day mapping. Everything else in the tab is ImGui drawing and
/// is verified in game, but this is arithmetic and an off-by-one here would silently
/// report the wrong day in every tooltip.
/// </summary>
public class HeatmapTabTests
{
    private static readonly DateOnly Today = new(2026, 9, 9);

    [Fact]
    public void The_last_cell_is_today()
    {
        Assert.Equal(Today, HeatmapTab.DayAt(Today, 11, 6));
    }

    [Fact]
    public void The_first_cell_is_twelve_weeks_back()
    {
        // 12 weeks x 7 days = 84 cells, and the last of them is today.
        Assert.Equal(Today.AddDays(-83), HeatmapTab.DayAt(Today, 0, 0));
    }

    [Fact]
    public void Rows_step_one_day_and_columns_step_one_week()
    {
        Assert.Equal(HeatmapTab.DayAt(Today, 5, 3).AddDays(1), HeatmapTab.DayAt(Today, 5, 4));
        Assert.Equal(HeatmapTab.DayAt(Today, 5, 3).AddDays(7), HeatmapTab.DayAt(Today, 6, 3));
    }

    [Fact]
    public void Every_cell_maps_to_a_distinct_day()
    {
        var seen = new HashSet<DateOnly>();
        for (var w = 0; w < 12; w++)
            for (var d = 0; d < 7; d++)
                Assert.True(seen.Add(HeatmapTab.DayAt(Today, w, d)));

        Assert.Equal(84, seen.Count);
    }
}
