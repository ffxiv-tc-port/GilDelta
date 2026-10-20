using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using GilDelta.Characters;
using GilDelta.Events;
using GilDelta.Events.Rules;
using GilDelta.Ipc;
using GilDelta.Localization;
using GilDelta.Storage;
using GilDelta.Wallet;
using GilDelta.Windows;
using GilDelta.Windows.Dashboard;
using GilDelta.Windows.Widget;

namespace GilDelta;

public sealed partial class Plugin : IDalamudPlugin
{
    public string Name => "GilDelta";

    /// <summary>
    /// How far apart the two halves of a cross-character transfer may sit.
    /// </summary>
    private TimeSpan CharacterTransferWindow() => TimeSpan.FromMinutes(
        Math.Clamp(_config.CharacterTransferWindowMinutes,
                   Configuration.CharacterTransferWindowMinutesMin,
                   Configuration.CharacterTransferWindowMinutesMax));

    /// <summary>
    /// How far back "this addon was open recently" reaches. User-configurable because
    /// automation opens and closes windows faster than a per-frame probe can follow.
    /// </summary>
    private TimeSpan AddonRecencyWindow => TimeSpan.FromSeconds(
        Math.Clamp(_config.AddonRecencySeconds,
                   Configuration.AddonRecencySecondsMin,
                   Configuration.AddonRecencySecondsMax));

    /// <summary>How often the account-totals cache is allowed to hit the disk.</summary>
    private static readonly TimeSpan SnapshotPersistInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How often the daily settlement book is allowed to hit the disk. It is also
    /// flushed when the local date rolls over and in Dispose, so this only bounds how
    /// much of the current day could be lost to a hard crash.
    /// </summary>
    private static readonly TimeSpan LedgerPersistInterval = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How many ticks to keep asking the game for a character's display name and home
    /// world before settling for whatever came back. Without a bound, a home world
    /// that never resolves would allocate a name string and a sheet lookup every frame.
    /// </summary>
    private const int IdentityAttemptLimit = 600;

    private const string LoginReadyTemplate = "GilDelta: 角色 {Label} 登入，帳本就緒（{Chest}）{Note}";

    private readonly Configuration _config;
    private readonly EventStoreSet _stores;
    private readonly EventLog _log = new();
    private readonly Inferrer _inferrer;
    private readonly WindowSystem _windowSystem = new("GilDelta");

    private readonly CharacterRegistry _characters = new();
    private readonly WalletSnapshotStore _snapshots = new();
    private readonly SerialJsonWriter _characterWriter;
    private readonly SerialJsonWriter _snapshotWriter;
    private readonly HashSet<ulong> _loadedCharacters = new();

    /// <summary>
    /// One writer per character's JSONL, created when retention first has to rewrite
    /// that file. Appends do not go through here - they stay the cheap
    /// File.AppendAllText they always were.
    /// </summary>
    private readonly Dictionary<ulong, SerialJsonWriter> _eventWriters = new();

    /// <summary>Characters whose file is being rewritten right now. Framework thread only.</summary>
    private readonly HashSet<ulong> _rewriteInFlight = new();

    /// <summary>
    /// Characters that produced a gil event while their file was being rewritten. That
    /// append was deliberately skipped (the rewrite would have clobbered it), so the
    /// file is re-rendered from memory once the rewrite lands.
    /// </summary>
    private readonly HashSet<ulong> _appendSkippedDuringRewrite = new();

    /// <summary>Written by the background writer, drained on the framework thread.</summary>
    private readonly ConcurrentQueue<ulong> _rewriteCompleted = new();

    /// <summary>
    /// The daily settlement book. Kept beside the event log rather than derived from it,
    /// because a settled day has to outlive the events behind it (retention deletes
    /// those) and because the event list cannot express "this day was never observed".
    /// </summary>
    private readonly DailyLedger _ledger = new();
    private readonly Dictionary<ulong, SerialJsonWriter> _dailyWriters = new();

    /// <summary>Characters whose settlement file no longer matches memory. Framework thread only.</summary>
    private readonly HashSet<ulong> _ledgerDirty = new();

    private string _dailyRoot = string.Empty;
    private DateTimeOffset _lastLedgerPersist = DateTimeOffset.MinValue;
    private DateTime _lastLedgerDate = DateTime.MinValue;

    /// <summary>
    /// Bumped on every write to <see cref="_ledger"/>. The dashboard's view of the book
    /// is cached against it, so nothing has to be compared to notice a change.
    /// </summary>
    private long _ledgerVersion;

    private IReadOnlyList<DailySummary>? _ledgerViewCache;
    private ulong? _ledgerViewScope;
    private long _ledgerViewVersion = -1;

    /// <summary>
    /// Exclude / restore presses waiting to be applied.
    /// 🔴 The button is drawn in a UI callback, and <see cref="EventLog"/>,
    /// <see cref="DailyLedger"/> and the file writers are all framework-thread-only.
    /// Acting on the press where it happens would mean betting that the two callbacks
    /// run on the same thread; queueing costs one frame and removes the bet.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<GilEvent> _toggleRequests = new();

    private readonly List<Wallet.WalletDiff> _recentDiffs = new();
    private readonly DiffLogSummary _diffSummary = new(TimeSpan.FromMinutes(1));
    private readonly AddonStateTracker _addonState = new();

    /// <summary>The addon sample of the current tick. Reused; framework thread only.</summary>
    private readonly List<string> _openAddons = new();

    /// <summary>
    /// Classification hints other plugins push in over <c>GilDelta.Hint</c>. Written
    /// from the calling plugin's thread, read only on the framework thread — see the
    /// remarks on <see cref="GilHintStore"/>.
    /// </summary>
    private readonly GilHintStore _hints;

    private GilDeltaIpc? _ipc;
    private WalletReader? _reader;
    private WalletWatcher? _watcher;
    private WidgetWindow? _widget;
    private DashboardWindow? _dashboard;

    /// <summary>The records behind whichever dashboard figure was last clicked.</summary>
    private DetailWindow? _detail;

    private ConfigWindow? _configWindow;

    /// <summary>
    /// The character currently being played; 0 while logged out or at the title
    /// screen. This is what gates recording — an event with no owner would land in
    /// the unknown bucket, so we do not record one at all.
    /// </summary>
    private ulong _activeContentId;

    /// <summary>
    /// The last character actually played. Unlike <see cref="_activeContentId"/> this
    /// is never cleared, so "follow current character" keeps showing the character you
    /// were just playing instead of jumping to the unknown bucket on logout.
    /// </summary>
    private ulong _displayContentId;

    private bool _identityIncomplete;
    private int _identityAttempts;

    private readonly LoginSummaryGate _loginSummaries = new();

    /// <summary>Login events since the active character last changed; reported on the next change.</summary>
    private int _loginEventsSinceSwitch;

    /// <summary>
    /// Local date the retention sweep last ran on. Retention is a once-a-day job; only
    /// the date comparison happens per frame.
    /// </summary>
    private DateTime _lastRetentionDate = DateTime.MinValue;
    private bool _snapshotsDirty;
    private DateTimeOffset _lastSnapshotPersist = DateTimeOffset.MinValue;

    private List<CharacterInfo>? _characterListCache;
    private Dictionary<ulong, string>? _characterLabelCache;
    private Language _characterCacheLanguage = Language.English;

    // Built contexts, reused until one of their inputs changes: building one walks the whole
    // event log, and the widget is drawn every frame.
    private readonly ViewCache<WidgetContext> _widgetView = new();
    private readonly ViewCache<DashboardContext> _dashboardView = new();
    private readonly VisibleEvents _widgetEvents = new();
    private readonly VisibleEvents _dashboardEvents = new();

    public Plugin(IDalamudPluginInterface pi)
    {
        // Dalamud SDK 15: Inject is non-generic and takes an instance whose
        // [PluginService] members (static or instance) it fills via reflection.
        // The Service instance is throwaway because all properties are static.
        pi.Inject(new Service());

        _config = Service.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Strings.SetLanguage(ResolveLanguage(_config.Language));

        // Information level on purpose: an unknown category coming in over IPC means
        // a consumer plugin's hints are being dropped, and that is exactly the line
        // to ask a user for when their events keep landing in Misc.
        _hints = new GilHintStore(log: message => Service.Log.Information("{Message}", message));

        _inferrer = new Inferrer(
            RuleChain.Build(CharacterTransferWindow, RelabelAsCharacterTransfer),
            // Warning, not Information: a rule that throws is a defect in us, and until
            // it is fixed every event it would have owned quietly becomes Misc.
            onRuleFault: (e, message) => Service.Log.Warning(e, message));

        var configDir = Service.PluginInterface.GetPluginConfigDirectory();
        _stores = new EventStoreSet(Path.Combine(configDir, "events"));
        _dailyRoot = Path.Combine(configDir, "daily");
        _characterWriter = new SerialJsonWriter(
            Path.Combine(configDir, "characters.json"),
            e => Service.Log.Warning(e, "GilDelta: writing characters.json failed"));
        _snapshotWriter = new SerialJsonWriter(
            Path.Combine(configDir, "wallets.json"),
            e => Service.Log.Warning(e, "GilDelta: writing wallets.json failed"));

        LoadPersistedState(configDir);

        // One framework subscription, taken BEFORE the WalletWatcher's, so that the
        // active-character check and the addon snapshot are both up to date by the
        // time the watcher diffs wallets in the same tick.
        Service.Framework.Update += OnFrameworkTick;

        _reader = new WalletReader();
        // The gate is what makes character switching safe: while no character is
        // loaded the watcher drops its baseline, so the next character's first
        // reading seeds a fresh one instead of being diffed against the old one.
        _watcher = new WalletWatcher(Service.Framework, _reader, () => _activeContentId != 0);
        _watcher.OnDiff += HandleDiff;

        var renderers = new IWidgetRenderer[]
        {
            new MinimalRenderer(),
            new CompactRenderer(),
            new TrendRenderer(),
            new TileRenderer(),
        };
        _widget = new WidgetWindow(_config, renderers, WidgetView);
        _windowSystem.AddWindow(_widget);

        var tabs = new IDashboardTab[]
        {
            new TimelineTab(),
            new ChartTab(_config),
            new DailyTab(),
            new BreakdownTab(),
            new HeatmapTab(),
            // Last: the display order is this array, and appending leaves every tab a
            // user already knows exactly where it was.
            new BalanceTab(_config),
        };
        _dashboard = new DashboardWindow(_config, tabs, DashboardView);
        _windowSystem.AddWindow(_dashboard);

        _detail = new DetailWindow(DashboardView);
        _windowSystem.AddWindow(_detail);

        _configWindow = new ConfigWindow(_config);
        _windowSystem.AddWindow(_configWindow);

        Service.ClientState.Login  += OnLogin;
        Service.ClientState.Logout += OnLogout;

        Service.CommandManager.AddHandler("/gildelta", new CommandInfo(OnCommand)
        {
            HelpMessage = Strings.CommandHelpMain,
        });
        Service.CommandManager.AddHandler("/gd", new CommandInfo(OnCommand)
        {
            HelpMessage = Strings.CommandHelpAlias,
            ShowInHelp = false,
        });

        Service.PluginInterface.UiBuilder.Draw         += DrawUi;
        Service.PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
        Service.PluginInterface.UiBuilder.OpenMainUi   += OnOpenMainUi;

        // Registered last, so no other plugin can reach a half-built Plugin: an IPC
        // func runs inline on the caller's thread the moment it is registered.
        _ipc = new GilDeltaIpc(Service.PluginInterface, _hints);
        Service.Log.Information(
            "GilDelta: IPC 端點 {Endpoint} 已註冊；可接受的分類為 [{Categories}]",
            GilDeltaIpc.HintEndpoint, string.Join(",", GilHintStore.AcceptedCategories));
    }

    private static Language ResolveLanguage(int code)
    {
        if (code >= 0 && code < Enum.GetValues<Language>().Length) return (Language)code;

        // TC's Dalamud fork reports ClientLanguage 7 ("TraditionalChinese") where
        // upstream global clients only ever report 0..3; the global-only enum member
        // named "ChineseTraditional" is 5. Compare by numeric value, not enum name.
        return (int)Service.ClientState.ClientLanguage switch
        {
            0          => Language.Japanese,
            2          => Language.German,
            3          => Language.French,
            4          => Language.SimplifiedChinese,
            5 or 7     => Language.ChineseTraditional,
            _          => Language.English,
        };
    }

    private void DrawUi() => _windowSystem.Draw();

    private void OnOpenConfigUi() { if (_configWindow is not null) _configWindow.IsOpen = true; }
    private void OnOpenMainUi()   { if (_dashboard    is not null) _dashboard.IsOpen    = true; }

    public void Dispose()
    {
        // First: stop other plugins from being able to call in while we tear down.
        _ipc?.Dispose();
        _watcher?.Dispose();
        Service.Framework.Update   -= OnFrameworkTick;
        Service.ClientState.Login  -= OnLogin;
        Service.ClientState.Logout -= OnLogout;
        Service.CommandManager.RemoveHandler("/gildelta");
        Service.CommandManager.RemoveHandler("/gd");
        Service.PluginInterface.UiBuilder.Draw         -= DrawUi;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        Service.PluginInterface.UiBuilder.OpenMainUi   -= OnOpenMainUi;
        _windowSystem.RemoveAllWindows();
        Service.PluginInterface.SavePluginConfig(_config);
        LogDiffSummary(_diffSummary.Take(DateTimeOffset.Now));

        // Final state goes out on this thread: a queued background write is not
        // guaranteed to run before the plugin is unloaded.
        try
        {
            if (_snapshotsDirty)
            {
                _snapshotsDirty = false;
                _snapshotWriter.Queue(_snapshots.Serialize());
            }
            PersistLedger(DateTimeOffset.Now, force: true);
            _snapshotWriter.Flush();
            _characterWriter.Flush();
            foreach (var writer in _eventWriters.Values) writer.Flush();
            foreach (var writer in _dailyWriters.Values) writer.Flush();
        }
        catch (Exception e)
        {
            Service.Log.Warning(e, "GilDelta: final state flush failed");
        }
    }
}
