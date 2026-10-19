using Dalamud.Bindings.ImGui;
using GilDelta.Localization;

namespace GilDelta.Windows.Widget;

public sealed class CompactRenderer : IWidgetRenderer
{
    public WidgetDensity Density => WidgetDensity.Compact;

    public void Draw(WidgetContext ctx)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
        ImGui.TextUnformatted(Strings.TotalWealthUpper);
        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextPrimary);
        ImGui.SetWindowFontScale(1.4f);
        ImGui.TextUnformatted($"{ctx.Total:N0}");
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopStyleColor();
        TotalHotspot.Offer(ctx);

        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
        ImGui.TextUnformatted(Strings.Today);
        ImGui.PopStyleColor();
        ImGui.SameLine();

        var dc = ctx.TodayDelta >= 0 ? ctx.Theme.PositiveDelta : ctx.Theme.NegativeDelta;
        var sign = ctx.TodayDelta >= 0 ? "+" : "";
        ImGui.PushStyleColor(ImGuiCol.Text, dc);
        ImGui.TextUnformatted($"{sign}{ctx.TodayDelta:N0}");
        ImGui.PopStyleColor();

        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
        // The chest segment is dropped entirely rather than printed as 0 when it is being
        // excluded: a 0 there reads as "the chest is empty", which is a different claim
        // from "the chest is not being counted".
        // 🔴 And when it is being counted but could not be read, it prints "?" - the
        // chest reads as unavailable everywhere except at the chest itself.
        if (ctx.ExcludeFreeCompanyChest)
            ImGui.TextUnformatted($"{ctx.SelfTotal:N0}  ·  {ctx.RetainerTotal:N0}");
        else if (ctx.FreeCompanyTotal is { } amount)
            ImGui.TextUnformatted($"{ctx.SelfTotal:N0}  ·  {ctx.RetainerTotal:N0}  ·  {amount:N0}");
        else
            ImGui.TextUnformatted($"{ctx.SelfTotal:N0}  ·  {ctx.RetainerTotal:N0}  ·  ?");
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            if (ctx.ExcludeFreeCompanyChest)
                ImGui.SetTooltip(Strings.ExcludingFcChestNow);
            else if (ctx.FreeCompanyTotal is null)
                ImGui.SetTooltip(Strings.FcChestUnreadableHelp);
        }
    }
}
