using System;
using GilDelta.Events;

namespace GilDelta.Windows.Dashboard;

/// <summary>
/// "Which records add up to the number I just clicked on." Either a whole local day or
/// a category inside a time window, expressed as a predicate over the event list rather
/// than a pre-built list - the list is rebuilt from the live context every frame.
/// 🔴 A day is matched with <see cref="DailyLedger.LocalDayOf"/>, NOT a from/to window:
/// the settlement book buckets an event by the wall clock the event itself carried, so a
/// window built from the viewer's offset puts records on the wrong side of midnight.
/// </summary>
public sealed record DetailQuery
{
    /// <summary>What the user clicked, spelled out on the detail view's first line.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The local day this figure covers; null for a range query.</summary>
    public DateOnly? Day { get; init; }

    /// <summary>Inclusive lower bound. Inclusive on BOTH ends, to match <c>DashboardContext.NetByCategory</c> exactly.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Inclusive upper bound.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Restrict to one category; null means every category.</summary>
    public GilEventCategory? Category { get; init; }

    /// <summary>
    /// The number that was on screen. The detail view subtracts its own total from this
    /// and says so when the two disagree, instead of leaving the user to notice.
    /// </summary>
    public long? SummaryAmount { get; init; }

    /// <summary>
    /// True when <see cref="SummaryAmount"/> came out of the settlement book rather than
    /// out of the event list. That is the only case where the two can legitimately
    /// disagree - a settled day outlives the records behind it - and the explanation
    /// shown on screen depends on knowing which it was.
    /// </summary>
    public bool SummaryFromLedger { get; init; }

    /// <summary>
    /// One local day, as the heatmap, the chart and the daily tab all show it.
    /// <paramref name="net"/> is the settled figure on screen, so the detail view can
    /// say whether the records still add up to it.
    /// </summary>
    public static DetailQuery ForDay(string title, DateOnly day, long? net) => new()
    {
        Title = title,
        Day = day,
        SummaryAmount = net,
        SummaryFromLedger = true,
    };

    /// <summary>
    /// A time window, optionally narrowed to one category - the shape the breakdown tab
    /// works in. Both bounds are inclusive, matching
    /// <see cref="DashboardContext.NetByCategory"/> exactly; a half-open window here
    /// would drop a record that landed on the boundary and the totals would disagree.
    /// </summary>
    public static DetailQuery ForRange(
        string title,
        DateTimeOffset from,
        DateTimeOffset to,
        GilEventCategory? category,
        long? net) => new()
    {
        Title = title,
        From = from,
        To = to,
        Category = category,
        SummaryAmount = net,
        SummaryFromLedger = false,
    };

    public bool Matches(GilEvent ev)
    {
        if (Day is { } day && DailyLedger.LocalDayOf(ev.Timestamp) != day) return false;
        if (From is { } from && ev.Timestamp < from) return false;
        if (To is { } to && ev.Timestamp > to) return false;
        if (Category is { } category && ev.Category != category) return false;
        return true;
    }
}
