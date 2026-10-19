using Dalamud.Bindings.ImGui;

namespace GilDelta.Windows.Widget;

public sealed class MinimalRenderer : IWidgetRenderer
{
    public WidgetDensity Density => WidgetDensity.Minimal;

    public void Draw(WidgetContext ctx)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextAccent);
        ImGui.Text("✦");
        ImGui.PopStyleColor();
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextPrimary);
        ImGui.Text(FormatGil(ctx.Total));
        ImGui.PopStyleColor();
        // Before SameLine: the delta drawn next would otherwise be ImGui's "last item".
        TotalHotspot.Offer(ctx);
        ImGui.SameLine();

        var deltaColor = ctx.TodayDelta >= 0 ? ctx.Theme.PositiveDelta : ctx.Theme.NegativeDelta;
        ImGui.PushStyleColor(ImGuiCol.Text, deltaColor);
        ImGui.Text(FormatDelta(ctx.TodayDelta));
        ImGui.PopStyleColor();
    }

    private static string FormatGil(long n) =>
        n >= 1_000_000 ? $"{n / 1_000_000.0:0.0}M"
        : n >= 1_000     ? $"{n / 1_000.0:0.0}k"
        : n.ToString();

    private static string FormatDelta(long n)
    {
        var sign = n >= 0 ? "+" : "-";
        var abs  = System.Math.Abs(n);
        return abs >= 1_000_000 ? $"{sign}{abs / 1_000_000.0:0.00}M"
            : abs >= 1_000     ? $"{sign}{abs / 1_000.0:0.0}k"
            : $"{sign}{abs}";
    }
}
