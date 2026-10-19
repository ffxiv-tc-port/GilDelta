using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using GilDelta.Events;
using GilDelta.Localization;

namespace GilDelta.Windows.Dashboard;

/// <summary>
/// Twelve weeks by seven days, one cell per local day. 🔴 The cells come from
/// <see cref="DashboardContext.Daily"/>, not the raw event list: summing events per day
/// cannot express "the plugin never saw this day", and a 0 painted as "no movement" is
/// a claim the plugin has no basis for.
/// 🔴 The two states are drawn as different <em>shapes</em> (stripes vs a solid tick),
/// never two shades of one block - a colour-only distinction dies with the palette.
/// </summary>
public sealed class HeatmapTab : IDashboardTab
{
    public DashboardTab Identity => DashboardTab.Heatmap;
    public string Title => Strings.TabHeatmap;

    private const int Weeks = 12;
    private const float Cell = 14f;
    private const float Gap  = 2f;

    /// <summary>Reused across frames; the tab is redrawn every frame it is open.</summary>
    private readonly Dictionary<DateOnly, DailySummary> _map = new();

    public void Draw(DashboardContext ctx)
    {
        _map.Clear();
        foreach (var row in ctx.Daily) _map[row.Date] = row;

        var today = DateOnly.FromDateTime(ctx.Now.Date);

        var nets     = new long[Weeks, 7];
        var observed = new bool[Weeks, 7];
        long maxAbs  = 1;
        var missing  = 0;

        for (var w = 0; w < Weeks; w++)
        {
            for (var d = 0; d < 7; d++)
            {
                if (_map.TryGetValue(DayAt(today, w, d), out var row))
                {
                    var net = row.NetFor(ctx.ExcludeFreeCompanyChest);
                    observed[w, d] = true;
                    nets[w, d] = net;
                    if (Math.Abs(net) > maxAbs) maxAbs = Math.Abs(net);
                }
                else
                {
                    missing++;
                }
            }
        }

        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var groundColor = ImGui.ColorConvertFloat4ToU32(ctx.Theme.BgSecondary);

        for (var w = 0; w < Weeks; w++)
        {
            for (var d = 0; d < 7; d++)
            {
                var x = origin.X + w * (Cell + Gap);
                var y = origin.Y + d * (Cell + Gap);

                if (!observed[w, d])
                {
                    ChartMarks.Hatch(ctx.Theme, x, y, Cell, Cell);
                    continue;
                }

                var net = nets[w, d];
                if (net == 0)
                {
                    // A recorded day that came to zero: the cell's own ground plus a
                    // solid tick, so it can never be mistaken for the hatch.
                    draw.AddRectFilled(new Vector2(x, y), new Vector2(x + Cell, y + Cell), groundColor, 2f);
                    ChartMarks.ZeroTick(ctx.Theme, x, y, Cell, Cell);
                    continue;
                }

                var ratio = (float)Math.Abs(net) / maxAbs;
                var tint = net > 0 ? ctx.Theme.PositiveDelta : ctx.Theme.NegativeDelta;
                var col = new Vector4(tint.X, tint.Y, tint.Z, 0.15f + 0.85f * ratio);
                draw.AddRectFilled(
                    new Vector2(x, y),
                    new Vector2(x + Cell, y + Cell),
                    ImGui.ColorConvertFloat4ToU32(col),
                    2f);
            }
        }

        ImGui.Dummy(new Vector2(Weeks * (Cell + Gap), 7 * (Cell + Gap)));

        HoverAndClick(ctx, origin, today, nets, observed);

        ImGui.Spacing();
        ImGui.TextDisabled(Strings.ChartLegend);

        // The count of blank days goes on the row, not into a tooltip: "I do not know"
        // is exactly the thing that must not be hidden behind a hover.
        if (missing > 0)
            ImGui.TextDisabled($"{Strings.ChartNoDataDay}: {missing} / {Weeks * 7}");
    }

    /// <summary>
    /// The day a cell stands for. Column 0 is the oldest week and the bottom-right cell
    /// is today.
    /// </summary>
    /// <remarks>Public so the mapping itself can be tested without an ImGui context.</remarks>
    public static DateOnly DayAt(DateOnly today, int week, int weekday) =>
        today.AddDays(-((Weeks - 1 - week) * 7 + (6 - weekday)));

    /// <summary>
    /// Per-cell tooltip and the click that opens that day's records, hit-tested by hand.
    /// 🔴 The <c>IsWindowHovered</c> guard is the part that matters: the hit test is
    /// against raw screen coordinates, so without it the tooltip - and worse, the click -
    /// fires whenever the mouse is over those pixels.
    /// A cell with no records is deliberately left unclickable, and says so through the
    /// cursor: a detail view that opened empty would read as a bug.
    /// </summary>
    private static void HoverAndClick(
        DashboardContext ctx, Vector2 origin, DateOnly today, long[,] nets, bool[,] observed)
    {
        if (!ImGui.IsWindowHovered()) return;

        var mouse = ImGui.GetMousePos();
        var w = (int)Math.Floor((mouse.X - origin.X) / (Cell + Gap));
        var d = (int)Math.Floor((mouse.Y - origin.Y) / (Cell + Gap));
        if (w < 0 || w >= Weeks || d < 0 || d >= 7) return;

        // Floor division alone would also claim the gap after a cell; the hit test has
        // to stop at the cell's own edge or it reports the neighbour's day.
        if (mouse.X > origin.X + w * (Cell + Gap) + Cell) return;
        if (mouse.Y > origin.Y + d * (Cell + Gap) + Cell) return;

        var day = DayAt(today, w, d);
        if (!observed[w, d])
        {
            ImGui.SetTooltip($"{day:yyyy-MM-dd}\n{Strings.ChartNoDataDay}");
            return;
        }

        var lines = $"{day:yyyy-MM-dd}\n{nets[w, d]:+#,0;-#,0;0}";
        if (ctx.DayOf(day) is { } row)
            lines += $"\n{Strings.DailyColumnEvents}: {row.EventCountFor(ctx.ExcludeFreeCompanyChest)}";

        if (ctx.ShowDetail is { } showDetail)
        {
            // "There is something to click here" has to be visible without clicking, so
            // the cursor changes and the hint goes on the tooltip that is already open.
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            lines += $"\n{Strings.DetailOpenHint}";
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                showDetail(DetailQuery.ForDay($"{day:yyyy-MM-dd}", day, nets[w, d]));
        }

        ImGui.SetTooltip(lines);
    }
}
