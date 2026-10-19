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

public sealed partial class Plugin
{
    // --- View composition ---------------------------------------------------

    private void InvalidateCharacterCache()
    {
        _characterListCache = null;
        _characterLabelCache = null;
    }

    /// <summary>
    /// Characters offered in the picker. Cached because it is read every frame the
    /// dashboard is open; the cache is keyed by UI language too, since the unknown
    /// bucket's label is translated.
    /// </summary>
    private IReadOnlyList<CharacterInfo> CharacterList()
    {
        if (_characterListCache is not null && _characterCacheLanguage == Strings.Current)
            return _characterListCache;

        var list = new List<CharacterInfo>(_characters.All);
        if (_log.For(CharacterInfo.UnknownContentId).Count > 0) list.Add(CharacterInfo.Unknown);

        _characterListCache = list;
        _characterLabelCache = null;
        _characterCacheLanguage = Strings.Current;
        return list;
    }

    private IReadOnlyDictionary<ulong, string> CharacterLabels()
    {
        var list = CharacterList();
        if (_characterLabelCache is not null) return _characterLabelCache;

        var map = new Dictionary<ulong, string>(list.Count);
        foreach (var info in list) map[info.ContentId] = info.Label;
        _characterLabelCache = map;
        return map;
    }

    private IReadOnlyList<GilEvent> AllEventsFor(ulong? scope) =>
        scope is null ? _log.All : _log.For(scope.Value);

    /// <summary>
    /// What the views are allowed to count: everything except the records the user has
    /// excluded.
    /// </summary>
    /// <remarks>
    /// Filtered at this single funnel rather than in each tab: a view that forgets to
    /// filter does not fail, it quietly disagrees with every other view. Returns the
    /// original list untouched when nothing is flagged, which must not allocate.
    /// </remarks>
    /// <param name="memo">One per view: each keeps the list it handed out last and only folds new records in.</param>
    private IReadOnlyList<GilEvent> EventsFor(ulong? scope, VisibleEvents memo, bool excludeFc) =>
        memo.Get(AllEventsFor(scope), _log.RewriteVersion, excludeFc);

    private static IReadOnlyList<GilEvent> OnlyExcluded(IReadOnlyList<GilEvent> events)
    {
        List<GilEvent>? flagged = null;
        foreach (var ev in events)
            if (ev.Suspect)
                (flagged ??= new List<GilEvent>()).Add(ev);
        return flagged ?? (IReadOnlyList<GilEvent>)Array.Empty<GilEvent>();
    }

    /// <summary>
    /// The wallets of the character being played, as the snapshot store holds them.
    /// 🔴 Not <c>WalletWatcher.LastSnapshot</c> directly: that is one raw reading, and
    /// <c>WalletReader</c> gives up on each of its three sections independently, so it
    /// routinely arrives without the retainer rows just after a login.
    /// Null while nothing has been read for this character yet, so the callers can keep
    /// saying "waiting for game state" rather than showing an empty wallet as a zero.
    /// </summary>
    private IReadOnlyList<Wallet.Wallet>? ActiveWallets()
    {
        var live = _watcher?.LastSnapshot;
        if (live is null || live.Count == 0) return null;

        // The store is refreshed from the same reading earlier in the tick; falling back
        // to the raw list covers the very first reading of a brand-new character.
        return _snapshots.TryGet(_activeContentId, out var stored) && stored.Wallets.Count > 0
            ? stored.Wallets
            : live;
    }

    /// <summary>
    /// Wallets for a scope, each still carrying which character it was read from and
    /// when. A null scope means the whole account.
    /// 🔴 Attributed rather than flat because <c>DashboardContext</c> derives its wallet
    /// list from these very rows: the balance tab has to contain exactly the rows the
    /// total was summed over. The returned <c>At</c> is the scope-level staleness the
    /// header reports, which is NOT the same as any one row's reading time.
    /// </summary>
    private (IReadOnlyList<WalletOrigin> Origins, DateTimeOffset? At, bool HasData) OriginsFor(
        ulong? scope, DateTimeOffset now)
    {
        List<WalletOrigin> origins;
        DateTimeOffset? at;

        if (scope is null)
        {
            var all = _snapshots.All;
            if (all.Count == 0) return (Array.Empty<WalletOrigin>(), null, false);
            origins = WalletSnapshotStore.MergeDetailed(all);
            at = WalletSnapshotStore.OldestAt(all);
        }
        else if (scope.Value == _activeContentId && ActiveWallets() is { Count: > 0 } live)
        {
            // Per row: null rather than "now" when there is no stored snapshot. The rows
            // the game really answered for this frame are flagged by MarkLive below; the
            // rest have no timestamp we can honestly claim.
            origins = Attributed(live, _activeContentId,
                _snapshots.TryGet(_activeContentId, out var stored) ? stored.At : (DateTimeOffset?)null);
            at = now;
        }
        else if (_snapshots.TryGet(scope.Value, out var snapshot))
        {
            origins = Attributed(snapshot.Wallets, scope.Value, snapshot.At);
            at = snapshot.At;
        }
        else
        {
            return (Array.Empty<WalletOrigin>(), null, false);
        }

        MarkLive(origins);
        return (origins, at, true);
    }

    /// <summary>Stamps a flat wallet list with the character and reading time it came from.</summary>
    private static List<WalletOrigin> Attributed(
        IReadOnlyList<Wallet.Wallet> wallets, ulong contentId, DateTimeOffset? at)
    {
        var origins = new List<WalletOrigin>(wallets.Count);
        foreach (var wallet in wallets) origins.Add(new WalletOrigin(wallet, contentId, at));
        return origins;
    }

    /// <summary>
    /// The wallet rows behind the widget, each still carrying which character it came
    /// from and when it was read.
    /// The account-wide branch goes through the very merge <see cref="OriginsFor"/>
    /// uses, so the widget total and the dashboard's balance rows cannot end up
    /// describing different sets of wallets.
    /// </summary>
    private IReadOnlyList<WalletOrigin>? WidgetOrigins(bool accountWide)
    {
        List<WalletOrigin> origins;

        if (accountWide)
        {
            var all = _snapshots.All;
            if (all.Count == 0) return null;
            origins = WalletSnapshotStore.MergeDetailed(all);
        }
        else
        {
            // v1 behaviour verbatim: without a live reading the widget says it is
            // waiting rather than showing a stale number as if it were current.
            var wallets = ActiveWallets();
            if (wallets is null || wallets.Count == 0) return null;

            // Null rather than "now" when there is no stored snapshot: the rows that
            // really were read this frame are marked below, and the rest have no
            // timestamp we can honestly claim.
            var at = _snapshots.TryGet(_activeContentId, out var stored)
                ? stored.At
                : (DateTimeOffset?)null;

            origins = Attributed(wallets, _activeContentId, at);
        }

        MarkLive(origins);
        return origins;
    }

    /// <summary>
    /// Flags the rows the game actually answered for on this frame.
    /// 🔴 The test is wallet identity against the reading the watcher just took, NOT
    /// "does this row belong to the character being played": a retainer row can belong
    /// to the character on screen and still be an hour old, and calling that "now" is
    /// the same class of lie as printing 0 for a wallet we could not read.
    /// </summary>
    private void MarkLive(List<WalletOrigin> origins)
    {
        if (_activeContentId == 0) return;

        var live = _watcher?.LastSnapshot;
        if (live is null || live.Count == 0) return;

        for (var i = 0; i < origins.Count; i++)
        {
            var origin = origins[i];
            if (origin.ContentId != _activeContentId) continue;
            if (!Contains(live, origin.Wallet)) continue;
            origins[i] = origin with { IsLive = true };
        }
    }

    /// <summary>Whole-value match: a row whose amount has moved on is not the live one.</summary>
    private static bool Contains(IReadOnlyList<Wallet.Wallet> wallets, Wallet.Wallet wallet)
    {
        foreach (var candidate in wallets)
            if (candidate.Equals(wallet)) return true;
        return false;
    }

    /// <summary>
    /// The widget's context, rebuilt only when something it is built from has changed.
    /// </summary>
    /// <remarks>
    /// The key covers every field <see cref="BuildWidgetContext"/> reads; a new input there
    /// has to join the key, or the widget keeps showing the old figure.
    /// </remarks>
    private WidgetContext? WidgetView()
    {
        var now = DateTimeOffset.Now;
        var accountWide = _config.WidgetAllCharacters;
        var inputs = new ViewInputs(
            EventsVersion: _log.Version,
            WalletsVersion: _snapshots.Version,
            LedgerVersion: 0,
            ActiveContentId: _activeContentId,
            Scope: accountWide ? null : _displayContentId,
            ExcludeFreeCompanyChest: _config.ExcludeFreeCompanyChest,
            Labels: CharacterLabels(),
            Characters: null,
            Minute: ViewInputs.MinuteOf(now),
            Offset: now.Offset);

        var reading = _watcher?.LastSnapshot;
        if (_widgetView.TryGet(inputs, reading, out var cached)) return cached;
        return _widgetView.Store(inputs, reading, BuildWidgetContext(now));
    }

    private WidgetContext? BuildWidgetContext(DateTimeOffset now)
    {
        var dashboard = _dashboard;
        var excludeFc = _config.ExcludeFreeCompanyChest;
        var accountWide = _config.WidgetAllCharacters;

        var origins = WidgetOrigins(accountWide);
        if (origins is null || origins.Count == 0) return null;

        // Filtered here, once, so every renderer, every derived figure inside
        // WidgetContext (Total / SelfTotal / today / week / month) and the balance list
        // agree. Doing it per view would leave whichever one was missed quietly
        // disagreeing.
        var visible = WalletExclusion.Origins(origins, excludeFc);
        if (visible.Count == 0) return null;

        ulong? eventScope = accountWide ? null : _displayContentId;

        return new WidgetContext(
            origins: visible,
            recentEvents: EventsFor(eventScope, _widgetEvents, excludeFc),
            now: now,
            theme: Theme.MidnightCoin.Instance)
        {
            IsAccountWide = accountWide,
            ExcludeFreeCompanyChest = excludeFc,
            CharacterLabels = CharacterLabels(),
            // Null until the window exists, which is only true while the plugin is
            // still being built; the renderers answer that by not offering the click
            // at all rather than by offering one that does nothing. Captured in a local
            // so the lambda cannot observe the field being torn down at dispose.
            OpenDailySettlement = dashboard is null
                ? null
                : () => dashboard.ShowTab(DashboardTab.Daily),
        };
    }

    /// <summary>
    /// The dashboard's context, shared by the dashboard and the detail window and rebuilt
    /// only when something it is built from has changed. Same contract as <see cref="WidgetView"/>.
    /// </summary>
    private DashboardContext? DashboardView()
    {
        var now = DateTimeOffset.Now;
        var scope = CharacterScope.Resolve(_config.DashboardCharacter, _displayContentId);
        var inputs = new ViewInputs(
            EventsVersion: _log.Version,
            WalletsVersion: _snapshots.Version,
            LedgerVersion: _ledgerVersion,
            ActiveContentId: _activeContentId,
            Scope: scope,
            ExcludeFreeCompanyChest: _config.ExcludeFreeCompanyChest,
            Labels: CharacterLabels(),
            Characters: CharacterList(),
            Minute: ViewInputs.MinuteOf(now),
            Offset: now.Offset);

        var reading = _watcher?.LastSnapshot;
        if (_dashboardView.TryGet(inputs, reading, out var cached)) return cached;
        return _dashboardView.Store(inputs, reading, BuildDashboardContext(scope, now));
    }

    private DashboardContext? BuildDashboardContext(ulong? scope, DateTimeOffset now)
    {
        var excludeFc = _config.ExcludeFreeCompanyChest;

        var (rawOrigins, at, hasWallets) = OriginsFor(scope, now);
        var events = EventsFor(scope, _dashboardEvents, excludeFc);
        var origins = WalletExclusion.Origins(rawOrigins, excludeFc);

        // Excluded records go through the same chest filter as the counted ones, so the
        // timeline does not start showing chest rows the moment one of them is flagged.
        var excluded = WalletExclusion.Events(OnlyExcluded(AllEventsFor(scope)), excludeFc);

        // v1 returned null (which renders "waiting for game state") whenever no wallet
        // had been read yet. Keep that only when there is genuinely nothing to show —
        // stored records are worth reading at the title screen.
        if (!hasWallets && events.Count == 0) return null;

        return new DashboardContext(origins, events, now, Theme.MidnightCoin.Instance)
        {
            Characters        = CharacterList(),
            CharacterLabels   = CharacterLabels(),
            SelectedContentId = scope,
            Excluded          = excluded,
            ToggleExcluded    = RequestToggleExcluded,
            // Null until the window exists, which is only true while the plugin is still
            // being built; the tabs answer that by not offering the click at all.
            ShowDetail        = _detail is null ? null : _detail.Show,
            // Excluding the chest can empty a wallet list outright, and an empty list
            // has to read as "unknown", not as a real zero.
            HasWalletData     = hasWallets && origins.Count > 0,
            WalletReadingAt   = at,
            // Not filtered on the way in: a settlement carries both halves, and the
            // views pick the one that matches the flag. That is what lets the setting be
            // flipped and give an exact answer for days that are already settled.
            Daily             = LedgerView(scope),
            ExcludeFreeCompanyChest = excludeFc,
        };
    }

    /// <summary>
    /// The settlement rows for a scope, cached between frames.
    /// The account-wide view folds every character's book together by date, which is
    /// linear in characters times days — cheap once, wasteful sixty times a second while
    /// the window is open. The cache is invalidated by a counter bumped wherever the
    /// ledger is actually written to, not by comparing contents.
    /// </summary>
    private IReadOnlyList<DailySummary> LedgerView(ulong? scope)
    {
        if (_ledgerViewCache is not null &&
            _ledgerViewVersion == _ledgerVersion &&
            _ledgerViewScope == scope)
        {
            return _ledgerViewCache;
        }

        _ledgerViewCache = _ledger.For(scope);
        _ledgerViewScope = scope;
        _ledgerViewVersion = _ledgerVersion;
        return _ledgerViewCache;
    }
}
