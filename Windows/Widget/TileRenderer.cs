using Dalamud.Bindings.ImGui;
using GilDelta.Localization;

namespace GilDelta.Windows.Widget;

public sealed class TileRenderer : IWidgetRenderer
{
    public WidgetDensity Density => WidgetDensity.Tile;

    public void Draw(WidgetContext ctx)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
        ImGui.TextUnformatted(Strings.TotalWealthUpper);
        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextPrimary);
        ImGui.SetWindowFontScale(1.5f);
        ImGui.TextUnformatted($"{ctx.Total:N0}");
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopStyleColor();
        TotalHotspot.Offer(ctx);

        if (ImGui.BeginTable("##tiles", 3, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoBordersInBody))
        {
            ImGui.TableNextRow();
            DrawTile(Strings.Today,    ctx.TodayDelta, ctx);
            DrawTile(Strings.TileWeek, ctx.WeekDelta,  ctx);
            DrawTile(Strings.TileMonth, ctx.MonthDelta, ctx);
            ImGui.EndTable();
        }
    }

    private static void DrawTile(string label, long delta, WidgetContext ctx)
    {
        ImGui.TableNextColumn();
        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        var col = delta >= 0 ? ctx.Theme.PositiveDelta : ctx.Theme.NegativeDelta;
        ImGui.PushStyleColor(ImGuiCol.Text, col);
        ImGui.TextUnformatted($"{delta:+#,0;-#,0;0}");
        ImGui.PopStyleColor();
    }
}
