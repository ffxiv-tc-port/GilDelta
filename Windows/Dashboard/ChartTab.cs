using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using GilDelta.Events;
using GilDelta.Localization;

namespace GilDelta.Windows.Dashboard;

/// <summary>
/// Wealth over time plus daily net.
/// 🔴 Drawn by hand rather than through <c>PlotLines</c> / <c>PlotHistogram</c>, for the
/// one thing those two cannot express: a day with no records has to look different from
/// a day whose net came to zero. Handing ImGui a float array forces every unobserved day
/// to become a 0, which is a claim the plugin has no basis for.
/// </summary>
public sealed class ChartTab : IDashboardTab
{
    public DashboardTab Identity => DashboardTab.Chart;
    public string Title => Strings.TabChart;

    private const float WealthHeight = 108f;
    private const float BarsHeight   = 92f;

    private readonly Configuration _config;

    /// <summary>
    /// Session-only, like BreakdownTab's period selector: it is a way of looking at the
    /// same data, not a preference worth writing to disk.
    /// </summary>
    private bool _fullScale;

    private readonly Dictionary<DateOnly, DailySummary> _map = new();

    public ChartTab(Configuration config) => _config = config;

    public void Draw(DashboardContext ctx)
    {
        DrawHeader(ctx);
        var days = DrawRangePicker(ctx);

        _map.Clear();
        foreach (var row in ctx.Daily) _map[row.Date] = row;

        var today = DateOnly.FromDateTime(ctx.Now.Date);
        var firstDay = today.AddDays(-(days - 1));

        var nets = new long[days];
        var observed = new bool[days];
        var missing = 0;
        for (var i = 0; i < days; i++)
        {
            if (_map.TryGetValue(firstDay.AddDays(i), out var row))
            {
                observed[i] = true;
                nets[i] = row.NetFor(ctx.ExcludeFreeCompanyChest);
            }
            else
            {
                missing++;
            }
        }

        ImGui.Spacing();
        DrawWealth(ctx, firstDay, nets, observed);
        ImGui.Spacing();
        DrawDailyNet(ctx, firstDay, nets, observed);

        DrawFooter(days, missing);
    }

    // --- Header --------------------------------------------------------------

    private static void DrawHeader(DashboardContext ctx)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
        ImGui.TextUnformatted(Strings.TotalWealthUpper);
        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextPrimary);
        ImGui.SetWindowFontScale(1.4f);
        // "?" rather than "0" when no wallet reading exists for this character:
        // an unknown total must not be shown as a real number.
        ImGui.TextUnformatted(ctx.HasWalletData ? $"{ctx.Total:N0}" : "?");
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopStyleColor();
        if (!ctx.HasWalletData && ImGui.IsItemHovered())
            ImGui.SetTooltip(Strings.NoWalletReadingYet);

        // A total that quietly leaves an unreadable wallet out is a lower bound, and
        // that has to be visible on the row itself - a tooltip alone hides the fact that
        // there is anything to hover over.
        if (ctx.HasUnknownWallet)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Strings.FcChestUnreadableNow);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.FcChestUnreadableHelp);
        }

        if (!ctx.ExcludeFreeCompanyChest) return;

        ImGui.SameLine();
        ImGui.TextDisabled(Strings.ExcludingFcChestNow);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.ExcludeFcChestHelp);
    }

    /// <summary>
    /// Range selector. "Auto" spans from the oldest day on record to today, which is
    /// what stops a fresh install from staring at a month of empty columns.
    /// </summary>
    private int DrawRangePicker(DashboardContext ctx)
    {
        var labels = new[]
        {
            Strings.ChartRangeAuto, Strings.Period7Days, Strings.Period30Days, Strings.Period90Days,
        };
        var values = new[] { 0, 7, 30, 90 };

        var index = Array.IndexOf(values, _config.ChartRangeDays);
        if (index < 0) index = 0;   // a hand-edited value falls back to Auto

        ImGui.SetNextItemWidth(120);
        if (ImGui.Combo(Strings.ChartRangeLabel, ref index, labels, labels.Length))
            _config.ChartRangeDays = values[index];

        ImGui.SameLine();
        var full = _fullScale;
        if (ImGui.Checkbox(Strings.ChartFullScale, ref full)) _fullScale = full;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.ChartFullScaleHelp);

        return values[index] > 0
            ? values[index]
            : ChartScale.AutoRangeDays(ctx.Daily, DateOnly.FromDateTime(ctx.Now.Date));
    }

    // --- Wealth line ---------------------------------------------------------

    /// <summary>
    /// Cumulative wealth, walked backwards from the current total.
    /// 🔴 It stops at the first day with no records, and everything before that is drawn
    /// as an "unknown" band instead of a line. Walking back across a gap would be a
    /// fabrication: gil moves while the plugin is not running, and nothing in the log
    /// says by how much.
    /// </summary>
    private static void DrawWealth(DashboardContext ctx, DateOnly firstDay, long[] nets, bool[] observed)
    {
        var days = nets.Length;
        var (origin, width) = Reserve(ctx, WealthHeight);

        // The trailing run of observed days is the only stretch the current total can anchor.
        var runStart = days;
        while (runStart > 0 && observed[runStart - 1]) runStart--;

        var slot = width / days;
        DrawHatchRange(ctx, origin, WealthHeight, slot, 0, runStart);

        if (!ctx.HasWalletData || runStart >= days)
        {
            Caption(ctx, Strings.ChartWealthOverlay);
            return;
        }

        var points = new long[days - runStart];
        var running = ctx.Total;
        for (var i = days - 1; i >= runStart; i--)
        {
            points[i - runStart] = running;
            running -= nets[i];
        }

        long min = points[0], max = points[0];
        foreach (var v in points)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }
        var span = Math.Max(1L, max - min);

        float Y(long v) => origin.Y + WealthHeight - 6f -
                           (float)((v - min) / (double)span) * (WealthHeight - 12f);
        Vector2 Point(int i) => new(origin.X + (i + 0.5f) * slot, Y(points[i - runStart]));

        var draw = ImGui.GetWindowDrawList();
        var lineColor = ImGui.ColorConvertFloat4ToU32(ctx.Theme.PositiveDelta);
        for (var i = runStart; i < days - 1; i++)
            draw.AddLine(Point(i), Point(i + 1), lineColor, 2f);

        // A single observed day has no segment to draw, so it gets a dot rather than
        // silently rendering nothing at all.
        if (days - runStart == 1) draw.AddCircleFilled(Point(runStart), 3f, lineColor);

        HoverAndClick(ctx, origin, width, WealthHeight, slot, days, firstDay, nets, observed);
        Caption(ctx, Strings.ChartWealthOverlay);

        if (runStart <= 0) return;

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.ChartWealthUnknownBefore);
    }

    // --- Daily net bars ------------------------------------------------------

    private void DrawDailyNet(DashboardContext ctx, DateOnly firstDay, long[] nets, bool[] observed)
    {
        var days = nets.Length;
        var (origin, width) = Reserve(ctx, BarsHeight);

        var draw = ImGui.GetWindowDrawList();
        var slot = width / days;
        var mid = origin.Y + BarsHeight / 2f;
        var half = BarsHeight / 2f - 5f;

        var cap = ChartScale.Cap(nets, observed, _fullScale);
        var mutedColor = ImGui.ColorConvertFloat4ToU32(ctx.Theme.TextMuted);
        var accent = ImGui.ColorConvertFloat4ToU32(ctx.Theme.TextAccent);
        var barWidth = Math.Max(1f, slot - 2f);
        var clipped = false;

        draw.AddLine(new Vector2(origin.X, mid), new Vector2(origin.X + width, mid), mutedColor, 1f);

        for (var i = 0; i < days; i++)
        {
            var x = origin.X + i * slot + 1f;

            if (!observed[i])
            {
                DrawHatch(ctx, x, origin.Y, barWidth, BarsHeight);
                continue;
            }

            if (nets[i] == 0)
            {
                // A recorded day that came to zero: a solid mark on the axis, so it can
                // never be mistaken for the hatch that means "nothing was recorded".
                draw.AddRectFilled(new Vector2(x, mid - 1.5f), new Vector2(x + barWidth, mid + 1.5f), mutedColor);
                continue;
            }

            var magnitude = Math.Abs(nets[i]);
            var isClipped = magnitude > cap;
            if (isClipped) clipped = true;

            // 🔴 Floor of 3px. Gil moves over five orders of magnitude in a day - a 200
            // gil teleport next to a 50,000,000 gil transfer rounds to nothing, and a
            // day that vanished would read as a day that came to zero, which is exactly
            // the confusion the hatch and the flat tick exist to prevent.
            var height = Math.Max(3f, (float)(Math.Min(magnitude, cap) / (double)cap) * half);
            var color = ImGui.ColorConvertFloat4ToU32(
                nets[i] >= 0 ? ctx.Theme.PositiveDelta : ctx.Theme.NegativeDelta);

            var top    = nets[i] >= 0 ? mid - height : mid;
            var bottom = nets[i] >= 0 ? mid : mid + height;
            draw.AddRectFilled(new Vector2(x, top), new Vector2(x + barWidth, bottom), color);

            if (!isClipped) continue;

            // The clipped end gets a notch instead of just running off the edge, so a bar
            // that is taller than it looks says so on the row and not only on hover.
            var notchY = nets[i] >= 0 ? top : bottom;
            draw.AddRectFilled(new Vector2(x, notchY - 2f), new Vector2(x + barWidth, notchY + 2f), accent);
        }

        HoverAndClick(ctx, origin, width, BarsHeight, slot, days, firstDay, nets, observed);

        var caption = Strings.ChartDailyNetOverlay;
        if (clipped) caption = $"{caption}   {Strings.ChartScaleCap} {cap:N0} ({Strings.ChartClipped})";
        Caption(ctx, caption);
    }

    // --- Shared drawing ------------------------------------------------------

    private static (Vector2 Origin, float Width) Reserve(DashboardContext ctx, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = Math.Max(120f, ImGui.GetContentRegionAvail().X);

        ImGui.GetWindowDrawList().AddRectFilled(
            origin, new Vector2(origin.X + width, origin.Y + height),
            ImGui.ColorConvertFloat4ToU32(ctx.Theme.BgSecondary), 3f);

        // Dummy reserves the layout space the hand-drawn plot occupies; without it the
        // next widget would be laid out on top of the drawing.
        ImGui.Dummy(new Vector2(width, height));
        return (origin, width);
    }

    private static void DrawHatchRange(
        DashboardContext ctx, Vector2 origin, float height, float slot, int from, int to)
    {
        for (var i = from; i < to; i++)
            DrawHatch(ctx, origin.X + i * slot + 1f, origin.Y, Math.Max(1f, slot - 2f), height);
    }

    /// <summary>
    /// The "nothing was recorded on this day" mark. The drawing itself lives in
    /// <see cref="ChartMarks"/> so the heatmap draws the identical stripes - two plots
    /// that disagree about what "no data" looks like are worse than either one alone.
    /// </summary>
    private static void DrawHatch(DashboardContext ctx, float x, float y, float width, float height) =>
        ChartMarks.Hatch(ctx.Theme, x, y, width, height);

    /// <summary>
    /// Per-day tooltip and the click that opens that day's records, hit-tested by hand.
    /// The <c>IsWindowHovered</c> guard is the part that matters: without it the tooltip -
    /// and a click - fires whenever the mouse is over these screen coordinates, including
    /// when another window is sitting on top of the dashboard.
    /// 🔴 The figure handed to the detail view is the DAY'S NET, not the wealth point
    /// under the cursor: the wealth line plots a running total, which no records add up to.
    /// </summary>
    private static void HoverAndClick(
        DashboardContext ctx, Vector2 origin, float width, float height, float slot, int days,
        DateOnly firstDay, long[] nets, bool[] observed)
    {
        if (!ImGui.IsWindowHovered()) return;

        var mouse = ImGui.GetMousePos();
        if (mouse.X < origin.X || mouse.X >= origin.X + width) return;
        if (mouse.Y < origin.Y || mouse.Y >= origin.Y + height) return;

        var index = (int)((mouse.X - origin.X) / slot);
        if (index < 0 || index >= days) return;

        var day = firstDay.AddDays(index);
        if (!observed[index])
        {
            ImGui.SetTooltip($"{day:yyyy-MM-dd}\n{Strings.ChartNoDataDay}");
            return;
        }

        var lines = $"{day:yyyy-MM-dd}\n{nets[index]:+#,0;-#,0;0}";
        if (ctx.DayOf(day) is { } row)
        {
            var open  = row.OpenFor(ctx.ExcludeFreeCompanyChest);
            var close = row.CloseFor(ctx.ExcludeFreeCompanyChest);
            lines += $"\n{Strings.DailyColumnEvents}: {row.EventCountFor(ctx.ExcludeFreeCompanyChest)}";
            lines += $"\n{Strings.DailyColumnOpen}: {(open is { } o ? o.ToString("N0") : "?")}";
            lines += $"\n{Strings.DailyColumnClose}: {(close is { } c ? c.ToString("N0") : "?")}";
        }

        if (ctx.ShowDetail is { } showDetail)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            lines += $"\n{Strings.DetailOpenHint}";
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                showDetail(DetailQuery.ForDay($"{day:yyyy-MM-dd}", day, nets[index]));
        }

        ImGui.SetTooltip(lines);
    }

    private static void Caption(DashboardContext ctx, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static void DrawFooter(int days, int missing)
    {
        ImGui.Spacing();
        ImGui.TextDisabled(Strings.ChartLegend);

        // The count of blank days goes on the row, not into a tooltip: "I do not know"
        // is exactly the thing that must not be hidden behind a hover.
        if (missing > 0)
            ImGui.TextDisabled($"{Strings.ChartNoDataDay}: {missing} / {days}");
    }
}
