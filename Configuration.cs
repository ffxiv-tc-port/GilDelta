using System.Numerics;
using Dalamud.Configuration;
using GilDelta.Characters;
using GilDelta.Theme;
using GilDelta.Windows.Dashboard;
using GilDelta.Windows.Widget;

namespace GilDelta;

public sealed class Configuration : IPluginConfiguration
{
    // Bumped when the schema changes; Plugin.LoadConfig handles migration.
    public int Version { get; set; } = 0;

    // -1 = follow the FFXIV client language; 0..6 = explicit Localization.Language enum value.
    public int Language { get; set; } = -1;
    public ThemeName Theme { get; set; } = ThemeName.MidnightCoin;

    public WidgetDensity Density { get; set; } = WidgetDensity.Compact;
    public bool ShowWidget { get; set; } = true;
    public bool HideInCutscene { get; set; } = true;
    public bool HideInDuty { get; set; } = false;
    public bool LockWidget { get; set; } = false;

    // null = use the default position (anchored top-right of the screen);
    // any concrete value is honoured verbatim, including (0, 0).
    public Vector2? WidgetPosition { get; set; } = null;

    public DashboardTab DefaultTab { get; set; } = DashboardTab.Chart;
    public Vector2 DashboardSize { get; set; } = new(720, 480);

    // 0 = keep events forever; otherwise prune events older than N days.
    public int RetentionDays { get; set; } = 0;

    // How far back "this addon was open recently" reaches when classifying a gil
    // change. The window exists because WalletWatcher only sees a change on the tick
    // AFTER the game applies it, by which point the window may already be shut.
    // Clamped to AddonRecencySecondsMin..AddonRecencySecondsMax where it is read.
    public int AddonRecencySeconds { get; set; } = 10;

    public const int AddonRecencySecondsMin = 1;
    public const int AddonRecencySecondsMax = 60;

    // Which character's records the dashboard shows. See CharacterScope:
    // "" = follow the character being played (v1 behaviour, and the default),
    // "*" = aggregate every character on the account,
    // anything else = that content id verbatim ("0" = the unknown-owner bucket).
    public string DashboardCharacter { get; set; } = CharacterScope.FollowCurrent;

    // false = the widget only counts the character being played (v1 behaviour);
    // true = it shows the whole-account total, including characters that are not
    // logged in (using the last wallet reading taken for each of them).
    public bool WidgetAllCharacters { get; set; } = false;

    // false (the default, and therefore what every existing user keeps) = the free
    // company chest counts towards the totals. true = chest wallets and chest events
    // are left out of every total, chart, breakdown and daily settlement. Nothing
    // stops being RECORDED either way - it is a display filter, so it can be turned
    // back off. See Events/WalletExclusion for why the filter keys on the wallet kind.
    public bool ExcludeFreeCompanyChest { get; set; } = false;

    // How many days the chart tab covers. 0 = fit the window to the records that
    // actually exist (clamped to ChartRangeAutoMin..ChartRangeAutoMax), which is why
    // it is the default: a fixed 30 is 30 empty columns on a fresh install.
    public int ChartRangeDays { get; set; } = 0;

    public const int ChartRangeAutoMin = 7;
    public const int ChartRangeAutoMax = 365;

    // Pairs a gil change nothing could explain on one character against an equal,
    // opposite one on another character of the same account, and labels both
    // CharacterTransfer. Display only; a paired record stays in the event log but
    // leaves the settlement book, because the same gil is on both characters.
    public bool DetectCharacterTransfers { get; set; } = true;

    // How far apart the two halves may sit. 30 rather than the ~7 minutes the
    // observed case needed: a transfer is collected whenever the other character is
    // next logged in. Clamped to the Min/Max below where it is read.
    public int CharacterTransferWindowMinutes { get; set; } = 30;

    public const int CharacterTransferWindowMinutesMin = 1;
    public const int CharacterTransferWindowMinutesMax = 720;

    // The balance tab leaves out wallets read as exactly 0; a wallet that could not be read
    // is not 0 and always stays listed. Display only - no total moves. true also for an
    // existing config file, which has no such key and so keeps this initialiser.
    public bool HideZeroBalances { get; set; } = true;

    // The balance tab's order, set by clicking its balance header. Default (0) is the
    // original order, and is what a config file without the key gets.
    public BalanceSort BalanceSortOrder { get; set; } = BalanceSort.Default;
}
