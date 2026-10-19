using Dalamud.Bindings.ImGui;

namespace GilDelta.Windows.Widget;

/// <summary>
/// Turns the total wealth figure a renderer has just drawn into the way into the
/// dashboard's daily settlement.
/// One helper rather than four copies: every density draws that number, and a click
/// that works on three of them is worse than one that works on none - the user learns
/// the number is not clickable and stops trying.
/// </summary>
internal static class TotalHotspot
{
    /// <summary>
    /// Call immediately after the text of the total, while it is still ImGui's "last
    /// item" and before anything else is drawn.
    /// </summary>
    public static void Offer(WidgetContext ctx)
    {
        if (ctx.OpenDailySettlement is not { } open) return;
        if (!ImGui.IsItemHovered()) return;

        // The affordance has to be visible without clicking: the cursor changes here,
        // and the window turns the flag below into a line in the widget's one tooltip.
        ctx.TotalHovered = true;
        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        // Acted on at mouse-DOWN, the same as the chart and heatmap cells. The widget
        // has no title bar, so a press anywhere on it also begins an ImGui window drag;
        // taking the press means a click opens the dashboard while a drag still moves
        // the widget, instead of the two fighting over the release.
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) open();
    }
}
