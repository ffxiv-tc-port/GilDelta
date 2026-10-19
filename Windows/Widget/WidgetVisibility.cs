namespace GilDelta.Windows.Widget;

/// <summary>
/// The two "hide the widget when..." settings, lifted out of <c>Configuration</c>.
/// 🔴 A separate type, not two <c>bool</c> parameters (adjacent bools silently accept
/// the game state instead of the settings) nor <c>Configuration</c> itself, which needs
/// a Dalamud reference the test project deliberately does not have.
/// </summary>
public readonly record struct WidgetHideSettings(bool InCutscene, bool InDuty);

/// <summary>
/// The game states the widget is allowed to hide itself for, sampled once per frame.
/// Plain booleans rather than a call into the client, so the decision below stays
/// testable: reading condition flags needs a live game, deciding what they mean does not.
/// </summary>
public readonly record struct WidgetGameState(bool InCutscene, bool InDuty);

/// <summary>
/// Whether the widget should be drawn on this frame. 🔴 A draw-time decision, not an
/// open/closed one: Dalamud's <c>Window.DrawConditions()</c> skips the window without
/// firing <c>OnClose</c> and without touching focus, so the widget comes back by itself.
/// Flipping <c>IsOpen</c> would leave it shut for good on a frame where the way out was
/// missed. "Show widget" is deliberately NOT answered here - that is an open/closed
/// state, so <c>WidgetWindow.PreOpenCheck</c> owns it.
/// </summary>
public static class WidgetVisibility
{
    public static bool ShouldDraw(WidgetHideSettings hide, WidgetGameState state)
    {
        if (hide.InCutscene && state.InCutscene) return false;
        if (hide.InDuty && state.InDuty) return false;
        return true;
    }
}
