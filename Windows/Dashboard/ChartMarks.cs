using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace GilDelta.Windows.Dashboard;

/// <summary>
/// The marks every plot in the dashboard shares, so "no records that day" and "recorded,
/// net came to zero" look the same wherever they are drawn.
/// 🔴 Both marks are deliberately a <em>shape</em>, not a colour. The dashboard is drawn
/// in whatever theme the user has, and two cells that differ only in shade stop being
/// distinguishable the moment the palette changes - which is precisely the case where
/// "I have no data for this day" would silently read as "this day came to nothing".
/// </summary>
public static class ChartMarks
{
    /// <summary>
    /// The "nothing was recorded here" mark: diagonal strokes with no fill colour of its
    /// own. Deliberately neither a zero-height bar nor a blank area - a blank area and a
    /// zero area look identical once they are both empty.
    /// </summary>
    public static void Hatch(GilDelta.Theme.Theme theme, float x, float y, float width, float height)
    {
        var draw = ImGui.GetWindowDrawList();
        var color = ImGui.ColorConvertFloat4ToU32(
            new Vector4(theme.TextMuted.X, theme.TextMuted.Y, theme.TextMuted.Z, 0.45f));

        // An area narrower than the hatch spacing cannot show strokes anyway, and a
        // year-long range would ask for thousands of them. A translucent fill reads the
        // same and costs one call.
        if (width < 3f)
        {
            draw.AddRectFilled(new Vector2(x, y), new Vector2(x + width, y + height), color);
            return;
        }

        // Each stroke is the 45-degree line y = (x - left) + c, clipped to the area.
        const float step = 5f;
        for (var c = y - width; c < y + height; c += step)
        {
            var yStart = Math.Max(y, c);
            var yEnd   = Math.Min(y + height, c + width);
            if (yEnd <= yStart) continue;

            var xStart = x + (yStart - c);
            var xEnd   = x + (yEnd - c);
            draw.AddLine(new Vector2(xStart, yStart), new Vector2(xEnd, yEnd), color, 1f);
        }
    }

    /// <summary>
    /// The "this was recorded and it came to zero" mark: one solid bar through the middle.
    /// A real mark rather than an absence, so it can never be confused with
    /// <see cref="Hatch"/> - and it survives any theme, because what distinguishes the
    /// two is that one is a line and the other is stripes.
    /// </summary>
    public static void ZeroTick(GilDelta.Theme.Theme theme, float x, float y, float width, float height)
    {
        var draw = ImGui.GetWindowDrawList();
        var color = ImGui.ColorConvertFloat4ToU32(theme.TextMuted);

        var mid = y + height / 2f;
        // Kept to at least 1px so it cannot vanish on a very short cell.
        var thickness = Math.Max(1f, Math.Min(3f, height / 5f));
        draw.AddRectFilled(
            new Vector2(x, mid - thickness / 2f),
            new Vector2(x + width, mid + thickness / 2f),
            color);
    }
}
