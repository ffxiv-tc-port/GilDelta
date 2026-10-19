using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using GilDelta.Characters;
using GilDelta.Localization;

namespace GilDelta.Windows.Dashboard;

public sealed class DashboardWindow : Window
{
    private readonly Configuration _config;
    private readonly IReadOnlyDictionary<DashboardTab, IDashboardTab> _tabs;
    private readonly Func<DashboardContext?> _ctxProvider;

    /// <summary>
    /// True only on the very first Draw() after the window opens. Used to
    /// honor <see cref="Configuration.DefaultTab"/> exactly once instead of
    /// re-selecting it every frame (which would prevent the user from ever
    /// switching tabs).
    /// </summary>
    private bool _selectDefaultTabOnce;

    /// <summary>
    /// A tab a caller asked for, honored on the next Draw() and then cleared.
    /// Takes precedence over <see cref="_selectDefaultTabOnce"/>: setting
    /// <c>IsOpen</c> is a plain field write, so <see cref="OnOpen"/> does not run until
    /// the window system's next draw pass - by which time both flags are set and the
    /// explicit request has to be the one that wins.
    /// </summary>
    private DashboardTab? _forceTab;

    public DashboardWindow(
        Configuration config,
        IEnumerable<IDashboardTab> tabs,
        Func<DashboardContext?> ctxProvider)
        : base($"GilDelta {Strings.SettingsTabDashboard}##dashboard", ImGuiWindowFlags.None)
    {
        _config      = config;
        _tabs        = tabs.ToDictionary(t => t.Identity);
        _ctxProvider = ctxProvider;

        Size          = config.DashboardSize;
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 320),
            MaximumSize = new Vector2(1600, 1200),
        };
    }

    public override void OnOpen() => _selectDefaultTabOnce = true;

    /// <summary>
    /// Opens the dashboard on one specific tab, raising it if it is already up. Never a
    /// toggle: it has a title bar, a close button and a slash command, and closing it on a
    /// second click would mean clicking the total while reading a different tab dismisses
    /// the whole dashboard.
    /// Called from the draw thread (the widget's click) and from the framework thread (the
    /// slash command); both only write two fields, and <c>BringToFront</c> raises a flag.
    /// </summary>
    public void ShowTab(DashboardTab tab)
    {
        // IsOpen first: BringToFront() returns early on a closed window.
        IsOpen = true;
        _forceTab = tab;
        BringToFront();
    }

    public override void Draw()
    {
        var ctx = _ctxProvider();
        if (ctx is null)
        {
            ImGui.TextDisabled(Strings.WaitingForGameState);
            return;
        }

        DrawCharacterPicker(ctx);
        ImGui.Separator();

        if (ImGui.BeginTabBar("##dashtabs"))
        {
            foreach (var tab in _tabs.Values)
            {
                // Only force-select the default tab on the first frame after
                // the window opens; otherwise the user can never switch away.
                var wanted = _forceTab ?? (_selectDefaultTabOnce ? _config.DefaultTab : (DashboardTab?)null);
                var flags = tab.Identity == wanted
                    ? ImGuiTabItemFlags.SetSelected
                    : ImGuiTabItemFlags.None;

                if (ImGui.BeginTabItem(tab.Title, flags))
                {
                    tab.Draw(ctx);
                    ImGui.EndTabItem();
                }
            }
            ImGui.EndTabBar();
        }

        _selectDefaultTabOnce = false;
        _forceTab = null;
    }

    /// <summary>
    /// Character picker. "Follow current character" is first and is the default, so
    /// the dashboard behaves exactly as it did before multi-character support unless
    /// the user asks for something else.
    /// </summary>
    private void DrawCharacterPicker(DashboardContext ctx)
    {
        var values = new List<string>(ctx.Characters.Count + 2)
        {
            CharacterScope.FollowCurrent,
            CharacterScope.AllCharacters,
        };
        var labels = new List<string>(values.Capacity)
        {
            Strings.CharacterFollowCurrent,
            Strings.CharacterAll,
        };

        foreach (var character in ctx.Characters)
        {
            values.Add(character.ContentId.ToString());
            labels.Add(character.Label);
        }

        // A stored selection that is not in the roster (hand-edited config, or an
        // event file that was deleted) shows as "follow current" but is NOT written
        // back, so it starts working again if that character reappears.
        var index = values.IndexOf(_config.DashboardCharacter ?? CharacterScope.FollowCurrent);
        if (index < 0) index = 0;

        ImGui.SetNextItemWidth(240);
        if (ImGui.Combo(Strings.CharacterLabel, ref index, labels.ToArray(), labels.Count))
            _config.DashboardCharacter = values[index];

        var hint = BuildScopeHint(ctx);
        if (hint.Length == 0) return;

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(hint);
    }

    private static string BuildScopeHint(DashboardContext ctx)
    {
        var parts = new List<string>(3);

        if (ctx.IsAccountWide)
            parts.Add(Strings.AccountTotalHint);
        else if (ctx.SelectedContentId == CharacterInfo.UnknownContentId)
            parts.Add(Strings.CharacterUnknownHint);

        if (!ctx.HasWalletData)
            parts.Add(Strings.NoWalletReadingYet);
        else if (ctx.WalletReadingAt is { } at)
            parts.Add($"{Strings.OldestReadingAt}: {at.LocalDateTime:yyyy-MM-dd HH:mm}");

        return string.Join("\n", parts);
    }

    public override void OnClose() => _config.DashboardSize = Size ?? _config.DashboardSize;
}
