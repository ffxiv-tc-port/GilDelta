using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using GilDelta.Events;
using GilDelta.Localization;

namespace GilDelta.Windows.Dashboard;

public sealed class BreakdownTab : IDashboardTab
{
    public DashboardTab Identity => DashboardTab.Breakdown;
    public string Title => Strings.TabBreakdown;

    private static string[] Periods => new[]
    {
        Strings.PeriodToday, Strings.Period7Days, Strings.Period30Days, Strings.PeriodAll,
    };
    private int _periodIndex = 1;

    public void Draw(DashboardContext ctx)
    {
        var periods = Periods;
        ImGui.SetNextItemWidth(120);
        ImGui.Combo("##period", ref _periodIndex, periods, periods.Length);

        var (from, to) = PeriodWindow(ctx.Now, _periodIndex);
        var grouped = ctx.NetByCategory(from, to);
        if (grouped.Count == 0)
        {
            ImGui.TextDisabled(Strings.NoEventsInPeriod);
            return;
        }

        var max = grouped.Values.Select(Math.Abs).DefaultIfEmpty(1L).Max();
        var ordered = grouped.OrderByDescending(kv => Math.Abs(kv.Value)).ToList();

        if (ImGui.BeginTable("##breakdown", 3,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("category", ImGuiTableColumnFlags.WidthFixed, 160);
            ImGui.TableSetupColumn("bar",      ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("amount",   ImGuiTableColumnFlags.WidthFixed, 130);

            for (var i = 0; i < ordered.Count; i++)
            {
                var kv = ordered[i];
                var label = EventTable.CategoryLabel(kv.Key);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextPrimary);
                // The label doubles as the row's hit area, so a click anywhere on the row
                // opens the records behind it. SetItemAllowOverlap keeps the spanning
                // selectable from swallowing hover for the rest of the row.
                var pressed = ImGui.Selectable(
                    $"{label}##gdcat{i}", false, ImGuiSelectableFlags.SpanAllColumns);
                ImGui.PopStyleColor();
                ImGui.SetItemAllowOverlap();

                if (ctx.ShowDetail is not null && ImGui.IsItemHovered())
                {
                    // "There is something to click here" has to be visible without clicking.
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    ImGui.SetTooltip(Strings.DetailOpenHint);
                }

                // 🔴 The same (from, to) the figure was computed from, and both bounds are
                // inclusive there - see NetByCategory. Rebuilding the window here, or
                // making it half-open, would drop a record that landed on the boundary and
                // the detail would stop adding up to the number above it.
                if (pressed && ctx.ShowDetail is { } showDetail)
                    showDetail(DetailQuery.ForRange(
                        $"{label} - {periods[_periodIndex]}", from, to, kv.Key, kv.Value));

                ImGui.TableNextColumn();
                var ratio = max == 0 ? 0f : (float)Math.Abs(kv.Value) / max;
                var barColor = kv.Value >= 0 ? ctx.Theme.PositiveDelta : ctx.Theme.NegativeDelta;
                var width = ImGui.GetContentRegionAvail().X * ratio;
                var p0 = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRectFilled(
                    p0, new System.Numerics.Vector2(p0.X + width, p0.Y + 14),
                    ImGui.ColorConvertFloat4ToU32(barColor));
                ImGui.Dummy(new System.Numerics.Vector2(0, 16));

                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, kv.Value >= 0 ? ctx.Theme.PositiveDelta : ctx.Theme.NegativeDelta);
                ImGui.TextUnformatted($"{kv.Value:+#,0;-#,0;0}");
                ImGui.PopStyleColor();
            }
            ImGui.EndTable();
        }
    }

    private static (DateTimeOffset from, DateTimeOffset to) PeriodWindow(DateTimeOffset now, int idx)
    {
        var to = now;
        var dayStart = new DateTimeOffset(now.Date, now.Offset);
        return idx switch
        {
            0 => (dayStart, to),
            1 => (dayStart.AddDays(-7), to),
            2 => (dayStart.AddDays(-30), to),
            _ => (DateTimeOffset.MinValue, to),
        };
    }
}
