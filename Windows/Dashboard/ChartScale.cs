using System;
using System.Collections.Generic;
using GilDelta.Events;

namespace GilDelta.Windows.Dashboard;

/// <summary>
/// The two arithmetic decisions behind the chart, kept out of the drawing code so they
/// can be tested without an ImGui context.
/// </summary>
public static class ChartScale
{
    /// <summary>
    /// The value the tallest drawn bar stands for. A plain maximum flattens the chart: one
    /// huge day makes every small day a single pixel, so the cap is the 90th percentile of
    /// the days that actually moved, which leaves an outlier visibly clipped instead of
    /// letting it set the scale for everything else. Two guards: fewer than three moving
    /// days has no percentile worth the name, so the maximum is used verbatim; and a
    /// largest day already within 2x of the cap is not clipped at all.
    /// </summary>
    /// <returns>Always at least 1, so the caller can divide by it unconditionally.</returns>
    public static long Cap(IReadOnlyList<long> nets, IReadOnlyList<bool> observed, bool fullScale)
    {
        var magnitudes = new List<long>();
        long max = 0;
        for (var i = 0; i < nets.Count; i++)
        {
            if (i >= observed.Count || !observed[i] || nets[i] == 0) continue;
            var m = Math.Abs(nets[i]);
            magnitudes.Add(m);
            if (m > max) max = m;
        }

        if (max == 0) return 1;
        if (fullScale || magnitudes.Count < 3) return max;

        magnitudes.Sort();
        var cap = magnitudes[(int)Math.Floor(0.9 * (magnitudes.Count - 1))];
        if (cap <= 0) return max;
        return max <= cap * 2 ? max : cap;
    }

    /// <summary>
    /// How many days the "Auto" range covers: from the oldest settled day to today.
    /// The clamp matters in both directions: a brand-new install has no records at all,
    /// and a year-old install would otherwise draw one column per day forever.
    /// </summary>
    public static int AutoRangeDays(IReadOnlyList<DailySummary> daily, DateOnly today)
    {
        if (daily.Count == 0) return Configuration.ChartRangeAutoMin;

        var oldest = daily[0].Date;
        foreach (var row in daily)
            if (row.Date < oldest)
                oldest = row.Date;

        // A record dated in the future (a clock that was wound back) would otherwise
        // produce a negative span; Clamp turns that into the minimum window.
        var span = today.DayNumber - oldest.DayNumber + 1;
        return Math.Clamp(span, Configuration.ChartRangeAutoMin, Configuration.ChartRangeAutoMax);
    }
}
