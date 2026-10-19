using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using GilDelta.Events;
using GilDelta.Localization;

namespace GilDelta.Windows.Dashboard;

/// <summary>
/// One row per local day: net, event count, and the wallet total at the start and at
/// the end of the day. The rows come from <see cref="DailyLedger"/>, not the event
/// list, and that is the point of the tab: a settled day keeps standing after
/// retention has deleted the events behind it, and a day that is simply missing means
/// the plugin never saw that day — which is why the opening / closing columns print
/// "?" instead of a zero whenever no wallet reading was taken.
/// </summary>
public sealed class DailyTab : IDashboardTab
{
    public DashboardTab Identity => DashboardTab.Daily;
    public string Title => Strings.TabDaily;

    public void Draw(DashboardContext ctx)
    {
        if (ctx.ExcludeFreeCompanyChest)
        {
            ImGui.TextDisabled(Strings.ExcludingFcChestNow);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.ExcludeFcChestHelp);
        }

        if (ctx.Daily.Count == 0)
        {
            ImGui.TextDisabled(Strings.DailyNoRecords);
            return;
        }

        DrawTotalsLine(ctx);

        // Newest first: the day a user wants is almost always the one that just ended.
        var rows = ctx.Daily.OrderByDescending(r => r.Date).ToList();

        if (!ImGui.BeginTable("##daily", 5,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders |
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY))
        {
            return;
        }

        ImGui.TableSetupColumn(Strings.DailyColumnDate,   ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn(Strings.DailyColumnEvents, ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn(Strings.DailyColumnNet,    ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Strings.DailyColumnOpen,   ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn(Strings.DailyColumnClose,  ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableHeadersRow();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            DrawDateCell(ctx, row, i);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.EventCountFor(ctx.ExcludeFreeCompanyChest).ToString());

            ImGui.TableNextColumn();
            var net = row.NetFor(ctx.ExcludeFreeCompanyChest);
            ImGui.PushStyleColor(ImGuiCol.Text, net >= 0 ? ctx.Theme.PositiveDelta : ctx.Theme.NegativeDelta);
            ImGui.TextUnformatted($"{net:+#,0;-#,0;0}");
            ImGui.PopStyleColor();
            // No tooltip of its own: the row-spanning selectable in the date
            // cell already covers this column, and it carries the same per-category
            // breakdown plus the "click for the records" hint.

            ImGui.TableNextColumn();
            DrawTotalCell(ctx, row, row.OpenFor(ctx.ExcludeFreeCompanyChest));

            ImGui.TableNextColumn();
            DrawTotalCell(ctx, row, row.CloseFor(ctx.ExcludeFreeCompanyChest));
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// The date cell doubles as the row's hit area: a selectable spanning every column,
    /// so a click anywhere on the row opens the records behind that day.
    /// 🔴 <c>SetItemAllowOverlap</c> is not decoration. A selectable with
    /// <c>SpanAllColumns</c> owns the hovered id for the whole row, and without it the
    /// plain text in the later columns stops answering <c>IsItemHovered</c>, silently
    /// killing the "?" tooltip that explains an unknown opening / closing total.
    /// </summary>
    private static void DrawDateCell(DashboardContext ctx, DailySummary row, int index)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
        var pressed = ImGui.Selectable(
            $"{row.Date:yyyy-MM-dd}##gdday{index}", false, ImGuiSelectableFlags.SpanAllColumns);
        ImGui.PopStyleColor();
        ImGui.SetItemAllowOverlap();

        if (ImGui.IsItemHovered())
        {
            var tooltip = CategoryTooltip(ctx, row);
            // "There is something to click here" has to be visible without clicking.
            if (ctx.ShowDetail is not null)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                tooltip += $"\n{Strings.DetailOpenHint}";
            }
            ImGui.SetTooltip(tooltip);
        }

        if (pressed && ctx.ShowDetail is { } showDetail)
            showDetail(DetailQuery.ForDay(
                $"{row.Date:yyyy-MM-dd}", row.Date, row.NetFor(ctx.ExcludeFreeCompanyChest)));
    }

    /// <summary>
    /// A total that was never read prints "?" in muted text, never 0 — the whole reason
    /// the opening/closing figures are nullable all the way down from the ledger.
    /// </summary>
    private static void DrawTotalCell(DashboardContext ctx, DailySummary row, long? value)
    {
        if (value is { } amount)
        {
            ImGui.TextUnformatted($"{amount:N0}");
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
        ImGui.TextUnformatted("?");
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(row.IsAggregate ? Strings.DailyAggregateHint : Strings.DailyUnknownTotalHint);
    }

    private static void DrawTotalsLine(DashboardContext ctx)
    {
        long net = 0;
        var events = 0;
        foreach (var row in ctx.Daily)
        {
            net += row.NetFor(ctx.ExcludeFreeCompanyChest);
            events += row.EventCountFor(ctx.ExcludeFreeCompanyChest);
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
        ImGui.TextUnformatted($"{Strings.DailyTotalRow}: {ctx.Daily.Count}");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, net >= 0 ? ctx.Theme.PositiveDelta : ctx.Theme.NegativeDelta);
        ImGui.TextUnformatted($"{net:+#,0;-#,0;0}");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.TextDisabled($"({Strings.DailyColumnEvents}: {events})");

        ImGui.TextDisabled(Strings.DailySettlementKept);
        ImGui.Separator();
    }

    private static string CategoryTooltip(DashboardContext ctx, DailySummary row)
    {
        var totals = row.CategoryTotals(ctx.ExcludeFreeCompanyChest);
        if (totals.Count == 0) return Strings.NoEventsInPeriod;

        var lines = new List<string>(totals.Count + 1) { row.Date.ToString("yyyy-MM-dd") };
        foreach (var kv in totals.OrderByDescending(kv => Math.Abs(kv.Value)))
            lines.Add($"{EventTable.CategoryLabel(kv.Key)}: {kv.Value:+#,0;-#,0;0}");

        return string.Join("\n", lines);
    }
}
