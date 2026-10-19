using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using GilDelta.Localization;

namespace GilDelta.Windows.Widget;

public sealed class WidgetWindow : Window
{
    private readonly IReadOnlyDictionary<WidgetDensity, IWidgetRenderer> _renderers;
    private readonly Configuration _config;
    private readonly Func<WidgetContext?> _ctxProvider;

    public WidgetWindow(
        Configuration config,
        IEnumerable<IWidgetRenderer> renderers,
        Func<WidgetContext?> ctxProvider)
        : base("GilDelta##widget", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar)
    {
        _config = config;
        _renderers = renderers.ToDictionary(r => r.Density);
        _ctxProvider = ctxProvider;

        RespectCloseHotkey = false;

        // Kept in step with the setting every frame by PreOpenCheck below; set here
        // too so the window's state is already right for anything that reads IsOpen
        // before the first draw.
        IsOpen = config.ShowWidget;
    }

    /// <summary>
    /// Makes "Show widget" take effect the moment it is unticked. 🔴 Nothing else in
    /// GilDelta toggles this window - the dashboard, the detail view and the settings have their own
    /// open state - so owning IsOpen here costs nothing.
    /// </summary>
    public override void PreOpenCheck() => IsOpen = _config.ShowWidget;

    /// <summary>
    /// Honours "Hide in cutscene" and "Hide in duty". See <see cref="WidgetVisibility"/>
    /// for why they are answered here rather than through <c>IsOpen</c>.
    /// </summary>
    public override bool DrawConditions() => WidgetVisibility.ShouldDraw(
        new WidgetHideSettings(_config.HideInCutscene, _config.HideInDuty),
        ReadGameState());

    /// <summary>
    /// Samples the condition flags behind the two hide settings.
    /// ⚠️ <c>WatchingCutscene</c> and <c>WatchingCutscene78</c> are different flags
    /// for different kinds of cutscene - checking only one leaves the other kind on
    /// screen - and <c>OccupiedInCutSceneEvent</c> covers the NPC event scenes that set
    /// neither. The three BoundByDuty flags are the same story on the duty side.
    /// </summary>
    private static WidgetGameState ReadGameState() => new(
        InCutscene: Service.Condition[ConditionFlag.WatchingCutscene] ||
                    Service.Condition[ConditionFlag.WatchingCutscene78] ||
                    Service.Condition[ConditionFlag.OccupiedInCutSceneEvent],
        InDuty: Service.Condition[ConditionFlag.BoundByDuty] ||
                Service.Condition[ConditionFlag.BoundByDuty56] ||
                Service.Condition[ConditionFlag.BoundByDuty95]);

    public override void PreDraw()
    {
        if (_config.LockWidget)
            Flags |= ImGuiWindowFlags.NoMove;
        else
            Flags &= ~ImGuiWindowFlags.NoMove;

        if (_config.WidgetPosition is { } pos)
            ImGui.SetNextWindowPos(pos, ImGuiCond.FirstUseEver);
        else
            ImGui.SetNextWindowPos(new Vector2(ImGui.GetIO().DisplaySize.X - 280, 80), ImGuiCond.FirstUseEver);
    }

    public override void Draw()
    {
        var ctx = _ctxProvider();
        if (ctx is null)
        {
            ImGui.TextDisabled(Strings.WaitingForGameState);
            return;
        }

        if (!_renderers.TryGetValue(_config.Density, out var renderer))
        {
            ImGui.TextDisabled($"renderer for {_config.Density} not registered");
            return;
        }

        // The context outlives the frame, so this frame's hover has to start from false.
        ctx.TotalHovered = false;
        renderer.Draw(ctx);

        DrawTooltip(ctx);
    }

    /// <summary>
    /// The widget's single tooltip. 🔴 Composed here rather than left to the renderers:
    /// <c>SetTooltip</c> overrides whatever an earlier call in the same frame set, so a
    /// renderer that also wanted to say something would have its text silently replaced.
    /// </summary>
    private static void DrawTooltip(WidgetContext ctx)
    {
        string? text = null;

        // The scope note first when it is the only one: it is the one that answers
        // "whose money is this".
        if (ctx.IsAccountWide) text = Strings.AccountTotalHint;

        if (ctx.TotalHovered)
        {
            text = text is null
                ? Strings.TotalClickHint
                : $"{Strings.TotalClickHint}{Environment.NewLine}{Environment.NewLine}{text}";
        }

        if (text is null || !ImGui.IsWindowHovered()) return;
        ImGui.SetTooltip(text);
    }

    public override void PostDraw()
    {
        var current = ImGui.GetWindowPos();
        if (_config.WidgetPosition != current)
            _config.WidgetPosition = current;
    }
}
