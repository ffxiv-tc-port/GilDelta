using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using GilDelta.Localization;
using GilDelta.Theme;
using GilDelta.Windows.Dashboard;
using GilDelta.Windows.Widget;

namespace GilDelta.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Configuration _config;

    public ConfigWindow(Configuration config)
        : base($"GilDelta {Strings.SettingsWindowTitle}##config",
               ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        _config = config;
        Size = new System.Numerics.Vector2(420, 360);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("##config"))
        {
            if (ImGui.BeginTabItem(Strings.SettingsTabGeneral))   { DrawGeneral();    ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Strings.SettingsTabWidget))    { DrawWidget();     ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Strings.SettingsTabDashboard)) { DrawDashboard();  ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Strings.SettingsTabData))      { DrawData();       ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    private void DrawGeneral()
    {
        // Every-language-name entries stay in their own language regardless of the
        // currently selected UI language (same convention as "日本語"/"Deutsch"/…
        // below) — that is how a user who can't read the current UI finds their own.
        var langLabels = new[] { Strings.LanguageAuto, "English", "日本語", "Deutsch", "Français", "简体中文", "한국어", "繁體中文" };
        var current = _config.Language + 1;
        if (ImGui.Combo(Strings.LanguageLabel, ref current, langLabels, langLabels.Length))
        {
            _config.Language = current - 1;
            Strings.SetLanguage(ResolveLanguage(_config.Language));
        }

        var theme = (int)_config.Theme;
        if (ImGui.Combo(Strings.ThemeLabel, ref theme, new[] { "Midnight Coin" }, 1))
            _config.Theme = (ThemeName)theme;

        ImGui.Separator();

        // Lives on the General tab rather than under Dashboard because it is not a
        // dashboard setting: the widget totals honour it too. Default off, so an
        // existing user's numbers do not move when they update.
        var excludeFc = _config.ExcludeFreeCompanyChest;
        if (ImGui.Checkbox(Strings.ExcludeFcChest, ref excludeFc))
            _config.ExcludeFreeCompanyChest = excludeFc;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.ExcludeFcChestHelp);
    }

    private void DrawWidget()
    {
        var show = _config.ShowWidget;
        if (ImGui.Checkbox(Strings.ShowWidget, ref show)) _config.ShowWidget = show;

        var density = (int)_config.Density;
        if (ImGui.Combo(Strings.DensityLabel, ref density,
                new[] { Strings.DensityMinimal, Strings.DensityCompact, Strings.DensityTrend, Strings.DensityTile }, 4))
            _config.Density = (WidgetDensity)density;

        var hideCs = _config.HideInCutscene;
        if (ImGui.Checkbox(Strings.HideInCutscene, ref hideCs)) _config.HideInCutscene = hideCs;

        var hideDuty = _config.HideInDuty;
        if (ImGui.Checkbox(Strings.HideInDuty, ref hideDuty)) _config.HideInDuty = hideDuty;

        var locked = _config.LockWidget;
        if (ImGui.Checkbox(Strings.LockWidgetPosition, ref locked)) _config.LockWidget = locked;

        var allCharacters = _config.WidgetAllCharacters;
        if (ImGui.Checkbox(Strings.WidgetAllCharacters, ref allCharacters))
            _config.WidgetAllCharacters = allCharacters;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.AccountTotalHint);

        if (ImGui.Button(Strings.ResetWidgetPosition))
            _config.WidgetPosition = null;
    }

    private void DrawDashboard()
    {
        // The order here IS the DashboardTab ordinal order — the combo index is cast
        // straight back to the enum. A new tab goes on the end in both places.
        var tabLabels = new[]
        {
            Strings.TabTimeline, Strings.TabChart, Strings.TabBreakdown,
            Strings.TabHeatmap, Strings.TabDaily, Strings.BalanceTitle,
        };
        var def = (int)_config.DefaultTab;
        if (ImGui.Combo(Strings.DefaultTabLabel, ref def, tabLabels, tabLabels.Length))
            _config.DefaultTab = (DashboardTab)def;
    }

    private void DrawData()
    {
        var ret = _config.RetentionDays;
        if (ImGui.InputInt(Strings.RetentionDaysLabel, ref ret))
            _config.RetentionDays = System.Math.Max(0, ret);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.RetentionDaysHelp);

        // A red label rather than a confirmation dialog: ImGui settings apply as you
        // type, and a modal on every keystroke would be worse than the warning.
        if (_config.RetentionDays > 0)
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.42f, 0.42f, 1f), Strings.RetentionDaysWarning);

        ImGui.Spacing();

        // A slider rather than free text: the useful range is narrow and both ends of
        // it are wrong in a way the user would have to reason about.
        var recency = _config.AddonRecencySeconds;
        if (ImGui.SliderInt(Strings.AddonRecencyLabel, ref recency,
                            Configuration.AddonRecencySecondsMin,
                            Configuration.AddonRecencySecondsMax))
        {
            _config.AddonRecencySeconds = System.Math.Clamp(
                recency, Configuration.AddonRecencySecondsMin, Configuration.AddonRecencySecondsMax);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.AddonRecencyHelp);

        ImGui.Spacing();

        var pairTransfers = _config.DetectCharacterTransfers;
        if (ImGui.Checkbox(Strings.DetectCharacterTransfers, ref pairTransfers))
            _config.DetectCharacterTransfers = pairTransfers;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.DetectCharacterTransfersHelp);

        // Hidden rather than greyed out: with the pairing off the number decides nothing.
        if (_config.DetectCharacterTransfers)
        {
            var transferWindow = _config.CharacterTransferWindowMinutes;
            if (ImGui.SliderInt(Strings.CharacterTransferWindowLabel, ref transferWindow,
                                Configuration.CharacterTransferWindowMinutesMin,
                                Configuration.CharacterTransferWindowMinutesMax))
            {
                _config.CharacterTransferWindowMinutes = System.Math.Clamp(
                    transferWindow,
                    Configuration.CharacterTransferWindowMinutesMin,
                    Configuration.CharacterTransferWindowMinutesMax);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.CharacterTransferWindowHelp);
        }

        ImGui.Spacing();
        ImGui.TextDisabled(Strings.ComingInV11);
    }

    private static Language ResolveLanguage(int code)
    {
        if (code >= 0 && code < System.Enum.GetValues<Language>().Length) return (Language)code;
        return Language.English;  // fallback; client-language detection is in Plugin.cs
    }
}
