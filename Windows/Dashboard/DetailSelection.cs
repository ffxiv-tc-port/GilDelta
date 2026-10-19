using System;
using System.Collections.Generic;
using GilDelta.Events;

namespace GilDelta.Windows.Dashboard;

/// <summary>
/// Picks the records behind one summary figure. Pure - no ImGui, no plugin state - so
/// the one property that matters can actually be tested: the detail adds up to the
/// number it is explaining.
/// </summary>
public static class DetailSelection
{
    /// <summary>
    /// How many rows the view will draw. The totals are computed over EVERY match, not
    /// over the drawn rows, so the arithmetic on screen stays exact however long the
    /// list is; the cap only bounds how much ImGui is asked to lay out in one frame.
    /// </summary>
    public const int MaxRows = 500;

    /// <summary>
    /// Re-reads the figure being explained out of the live context instead of trusting
    /// the one captured when the cell was clicked. 🔴 The detail view carries the exclude
    /// button, and a captured figure would sit there disagreeing with the rows by exactly
    /// the amount the user had just removed.
    /// A day that is no longer in the settlement book resolves to null, i.e. to "no claim":
    /// better to say nothing than to compare the rows against a number nobody can see.
    /// </summary>
    public static DetailQuery Refresh(DashboardContext ctx, DetailQuery query)
    {
        if (query.Day is { } day)
            return query with { SummaryAmount = ctx.DayOf(day)?.NetFor(ctx.ExcludeFreeCompanyChest) };

        if (query is { Category: { } category, From: { } from, To: { } to })
        {
            ctx.NetByCategory(from, to).TryGetValue(category, out var net);
            return query with { SummaryAmount = net };
        }

        return query;
    }

    /// <summary>
    /// <paramref name="counted"/> is what the dashboard is counting (already free of
    /// excluded records and, if the user asked, of the free company chest);
    /// <paramref name="excluded"/> is the records the user threw out, filtered the same
    /// way. They are kept apart all the way through: excluded rows are SHOWN, so the
    /// user can see why the detail is shorter than they expected and put one back, but
    /// they are never added into <see cref="DetailResult.Total"/>.
    /// </summary>
    public static DetailResult Build(
        IReadOnlyList<GilEvent> counted,
        IReadOnlyList<GilEvent> excluded,
        DetailQuery query)
    {
        var rows = new List<GilEvent>();
        long total = 0;
        var countedMatches = 0;
        var excludedMatches = 0;

        foreach (var ev in counted)
        {
            if (!query.Matches(ev)) continue;
            countedMatches++;
            total += ev.Amount;
            rows.Add(ev);
        }

        foreach (var ev in excluded)
        {
            if (!query.Matches(ev)) continue;
            excludedMatches++;
            rows.Add(ev);
        }

        // Newest first, the same order the timeline uses. Sorted after both lists are in
        // so an excluded record sits at the moment it happened rather than in a clump at
        // the bottom - it is easier to recognise a bogus record next to its neighbours.
        rows.Sort(static (a, b) => b.Timestamp.CompareTo(a.Timestamp));

        var matched = rows.Count;
        if (rows.Count > MaxRows) rows.RemoveRange(MaxRows, rows.Count - MaxRows);

        return new DetailResult(rows, total, countedMatches, excludedMatches, matched, query);
    }
}

/// <summary>The records behind one figure, plus everything the view has to say about them.</summary>
public sealed class DetailResult
{
    public DetailResult(
        IReadOnlyList<GilEvent> rows,
        long total,
        int countedCount,
        int excludedCount,
        int matchedCount,
        DetailQuery query)
    {
        Rows = rows;
        Total = total;
        CountedCount = countedCount;
        ExcludedCount = excludedCount;
        MatchedCount = matchedCount;
        Query = query;
    }

    /// <summary>Rows to draw, newest first, capped at <see cref="DetailSelection.MaxRows"/>.</summary>
    public IReadOnlyList<GilEvent> Rows { get; }

    /// <summary>Sum of the COUNTED records only - what the figure above should equal.</summary>
    public long Total { get; }

    public int CountedCount { get; }
    public int ExcludedCount { get; }

    /// <summary>Counted plus excluded, before the row cap. What the "showing n / m" line reports.</summary>
    public int MatchedCount { get; }

    public DetailQuery Query { get; }

    public bool IsTruncated => MatchedCount > Rows.Count;

    /// <summary>
    /// How far the detail falls short of the figure on screen, or null when there was no
    /// figure to reconcile against. Zero is the normal answer; anything else is a fact
    /// the user has to be told rather than left to work out from two numbers that do not
    /// match.
    /// </summary>
    public long? Difference => Query.SummaryAmount is { } summary ? summary - Total : null;

    public bool Reconciles => Difference is null or 0;
}
