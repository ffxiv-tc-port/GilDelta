using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using GilDelta.Events;
using GilDelta.Localization;

namespace GilDelta.Windows.Dashboard;

/// <summary>
/// The records themselves, newest first, and the one place a single record can be taken
/// out of the statistics or put back. 🔴 The entry point excludes; it never deletes: a
/// deletion cannot be undone, whereas an exclusion comes straight back off.
/// 🔴 The list is cut off at <see cref="PageSize"/> records so one frame is never asked
/// to lay out a year of history; the cut is stated on the row and can be pushed further,
/// because the failure mode of a silent cap is that the user believes it.
/// </summary>
public sealed class TimelineTab : IDashboardTab
{
    public DashboardTab Identity => DashboardTab.Timeline;
    public string Title => Strings.TabTimeline;

    /// <summary>How many more records each press of "show more" reveals.</summary>
    private const int PageSize = 200;

    /// <summary>
    /// Session-only, like the breakdown tab's period selector: how far down this list
    /// somebody looked once is not a preference worth writing to disk.
    /// </summary>
    private int _limit = PageSize;

    /// <summary>
    /// The merged, newest-first list and the context it was built from. Keyed on the context
    /// instance because the plugin hands out a new one whenever the log or the filter changes;
    /// a context whose lists changed in place would keep showing the old order.
    /// </summary>
    private DashboardContext? _sortedFor;
    private List<GilEvent> _sorted = new();
    private List<GilEvent>? _page;

    public void Draw(DashboardContext ctx)
    {
        var all = SortedFor(ctx);

        if (all.Count == 0)
        {
            ImGui.TextDisabled(Strings.NoEventsRecordedYet);
            return;
        }

        var limit = Math.Max(PageSize, _limit);
        var shown = Math.Min(limit, all.Count);
        var events = PageOf(all, shown);

        // How many records are being left out belongs on the row, not in a tooltip: the
        // numbers everywhere else in the window depend on it.
        if (ctx.Excluded.Count > 0)
        {
            ImGui.TextDisabled($"{ctx.Excluded.Count} {Strings.TimelineExcludedCount}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.TimelineExcludeHelp);
        }

        // Same rule for the cap itself.
        if (shown < all.Count)
        {
            ImGui.TextDisabled($"{Strings.DetailRowsShown}: {shown} / {all.Count}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"{Strings.TimelineShowMore}##gdmore")) _limit = limit + PageSize;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.TimelineShowMoreHelp);
        }

        EventTable.Draw(ctx, events, "gdrow", scrollY: false);
    }

    private List<GilEvent> SortedFor(DashboardContext ctx)
    {
        if (ReferenceEquals(ctx, _sortedFor)) return _sorted;

        // Excluded records are not in ctx.Events on purpose - no tab can count them by
        // forgetting to filter - so this is the one view that has to put them back in.
        // Refilled in place: only this tab ever holds the list, and a new one per rebuild is a
        // full copy of the log each time a record arrives.
        var all = _sorted;
        all.Clear();
        all.AddRange(ctx.Events);
        all.AddRange(ctx.Excluded);
        all.Sort(static (a, b) => b.Timestamp.CompareTo(a.Timestamp));

        _sortedFor = ctx;
        _page = null;
        return all;
    }

    private List<GilEvent> PageOf(List<GilEvent> all, int shown)
    {
        if (shown == all.Count) return all;
        if (_page is null || _page.Count != shown) _page = all.GetRange(0, shown);
        return _page;
    }
}
