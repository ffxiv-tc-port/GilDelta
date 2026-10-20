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
    // --- Events -------------------------------------------------------------

    private void OnLogin()
    {
        // The framework tick owns the actual switch; PlayerState is usually not populated
        // yet here, so the event is only counted and reported on the switch's Debug line.
        _loginEventsSinceSwitch++;
    }

    private void OnLogout(int type, int code)
    {
        // A normal logout (type 0, code 10000) is the same line every time, so it goes to
        // Verbose; anything else (a disconnect is code 90002) stays at Information.
        if (type == 0 && code == 10000)
            Service.Log.Verbose($"GilDelta: logout type={type} code={code}");
        else
            Service.Log.Information($"GilDelta: logout type={type} code={code}");
        LogDiffSummary(_diffSummary.Take(DateTimeOffset.Now));
        PersistSnapshots();

        // Logging out is the most likely moment for the game to be closed altogether,
        // so the day's settlement goes out now rather than waiting on the throttle.
        try
        {
            PersistLedger(DateTimeOffset.Now, force: true);
        }
        catch (Exception e)
        {
            Service.Log.Warning(e, "GilDelta: writing the daily settlements at logout failed");
        }
    }

    private void OnCommand(string command, string args)
    {
        var argument = args.Trim();

        if (argument.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            if (_configWindow is not null) _configWindow.IsOpen = !_configWindow.IsOpen;
        }
        else if (argument.Equals("wallets", StringComparison.OrdinalIgnoreCase))
        {
            // The way in that does not depend on the widget being on screen: the balances
            // are a dashboard tab, so this opens the dashboard on it.
            _dashboard?.ShowTab(DashboardTab.Balances);
        }
        else
        {
            if (_dashboard is not null) _dashboard.IsOpen = !_dashboard.IsOpen;
        }
    }

    private void HandleDiff(Wallet.WalletDiff diff)
    {
        var owner = _activeContentId;
        if (owner == 0) return;  // nothing to attribute this to; do not invent an owner

        // Use the rolling 2-second window of recently-seen addons so the rules
        // still match even when the Shop / Teleport / Repair addon was closed
        // by the time WalletWatcher detected the diff on the next tick.
        var lastSeen = _addonState.LastSeenWithin(AddonRecencyWindow);
        var openAddons = new HashSet<string>(lastSeen.Keys);
        var ctx = new GameContext(openAddons, _recentDiffs.ToArray(), DateTimeOffset.Now, _hints,
                                  owner, TransferCandidates(diff.At), lastSeen);
        _recentDiffs.Add(diff);
        if (_recentDiffs.Count > 32) _recentDiffs.RemoveAt(0);

        // The rules build the event without knowing who is logged in; stamp the owner.
        var ev = _inferrer.Classify(diff, ctx) with { ContentId = owner };

        // Diagnostic breadcrumb so misclassifications can be debugged from /xllog.
        // hintsLeft is what tells a "my hints never land" report apart from a
        // "nobody is sending hints" one: 0 before and after means nothing arrived.
        // Per change at Debug; the Information-level gil total is DiffLogSummary's line.
        Service.Log.Debug(
            "Diff {Kind}({Id}) {Delta:+#,0;-#,0;0} -> {Cat}; cid={Cid}; recentAddons=[{Addons}]; hintsLeft={Hints}",
            diff.Id.Kind, diff.Id.Identifier, diff.Delta, ev.Category, owner,
            string.Join(",", openAddons), _hints.ActiveCount);
        LogDiffSummary(_diffSummary.Add(owner, ev.Category, diff.Delta, diff.At));

        try
        {
            _log.Add(ev);

            // Booked into the day the event's own timestamp falls on, so a change that
            // lands either side of midnight goes to the right day regardless of when the
            // framework tick happens to notice the date rolled over.
            _ledger.Add(ev);
            MarkLedgerDirty(owner);

            // While a retention rewrite of this character's file is in flight, an
            // append would be silently clobbered by the rename that finishes it. Skip
            // the append and re-render the whole file once the rewrite lands - the
            // event is already in _log, which is what the rewrite renders from.
            if (_rewriteInFlight.Contains(owner))
                _appendSkippedDuringRewrite.Add(owner);
            else
                _stores.For(owner).Append(ev);
        }
        catch (Exception e)
        {
            Service.Log.Warning(e, "EventStore.Append failed");
        }
    }

    private static void LogDiffSummary(string? line)
    {
        if (line is not null) Service.Log.Information("{Summary:l}", line);
    }

    /// <summary>
    /// The events the other half of a transfer could be. Empty while the feature is
    /// off, which is the only gate <see cref="CharacterTransferRule"/> needs, and
    /// empty during replay, which is what stops history being re-paired.
    /// </summary>
    private IReadOnlyList<GilEvent> TransferCandidates(DateTimeOffset at)
    {
        if (!_config.DetectCharacterTransfers) return Array.Empty<GilEvent>();

        var cutoff = at - CharacterTransferWindow();
        List<GilEvent>? found = null;
        foreach (var ev in _log.All)
        {
            if (!CharacterTransferRule.IsPairable(ev.Category)) continue;
            if (ev.Timestamp < cutoff || ev.Timestamp > at) continue;
            (found ??= new List<GilEvent>()).Add(ev);
        }
        return found ?? (IReadOnlyList<GilEvent>)Array.Empty<GilEvent>();
    }

    /// <summary>
    /// Relabels the earlier half of a cross-character transfer, in each of the three
    /// places the flip has to land - the in-memory log, that character's file, and the
    /// day it was booked into. Retracted from the day and deliberately not re-added:
    /// a transfer is not booked at all, see <see cref="DailyLedger.IsBooked"/>.
    /// </summary>
    private void RelabelAsCharacterTransfer(GilEvent original, ulong counterpartContentId)
    {
        var updated = original with
        {
            Category = GilEventCategory.CharacterTransfer,
            Note = $"rule=CharacterTransferRule; counterparty=cid:{counterpartContentId}; relabelled",
        };

        if (!_log.Replace(original, updated))
        {
            Service.Log.Information(
                "GilDelta: 角色間轉帳的另一半已經不在紀錄裡了，略過（{When:yyyy-MM-dd HH:mm:ss} {Amount}）",
                original.Timestamp, original.Amount);
            return;
        }

        if (_ledger.Retract(original)) MarkLedgerDirty(original.ContentId);
        RewriteEventFile(original.ContentId);

        // Information level on purpose: this is the line to ask a user for when a
        // record they recognise turns into a transfer they did not expect.
        Service.Log.Information(
            "GilDelta: 已把一筆紀錄改標成角色間轉帳（{When:yyyy-MM-dd HH:mm:ss} {Amount}，角色 {Character}）",
            original.Timestamp, original.Amount, LabelForLog(original.ContentId));
    }

    /// <summary>Queued from the UI; applied here, on the framework thread.</summary>
    private void RequestToggleExcluded(GilEvent ev) => _toggleRequests.Enqueue(ev);

    private void DrainToggleRequests()
    {
        while (_toggleRequests.TryDequeue(out var ev))
        {
            try
            {
                ToggleExcluded(ev);
            }
            catch (Exception e)
            {
                Service.Log.Warning(e, "GilDelta: 排除／復原一筆紀錄時失敗");
            }
        }
    }

    /// <summary>
    /// 🔴 Carries the flip through every place it has to land: the in-memory log, that
    /// character's file on disk, and the settled day the record was booked into. The
    /// chart and the daily tab read the settlement book, not the event list, so a record
    /// dropped only from the event list would look like the feature had done nothing.
    /// </summary>
    private void ToggleExcluded(GilEvent ev)
    {
        var updated = ev with { Suspect = !ev.Suspect };
        if (!_log.Replace(ev, updated))
        {
            // The list the window is drawing is a snapshot; the record can be gone by the
            // time the button is pressed (retention, or a character file reloaded).
            Service.Log.Information(
                "GilDelta: 要排除的紀錄已經不在紀錄裡了，略過（{When:yyyy-MM-dd HH:mm:ss} {Amount}）",
                ev.Timestamp, ev.Amount);
            return;
        }

        if (updated.Suspect)
        {
            if (_ledger.Retract(updated)) MarkLedgerDirty(updated.ContentId);
        }
        else
        {
            _ledger.Reinstate(updated);
            MarkLedgerDirty(updated.ContentId);
        }

        RewriteEventFile(updated.ContentId);

        Service.Log.Information(
            "GilDelta: {Action}一筆紀錄（{When:yyyy-MM-dd HH:mm:ss} {Amount} {Category}，角色 {Character}）",
            updated.Suspect ? "排除" : "復原",
            updated.Timestamp, updated.Amount, updated.Category, LabelForLog(updated.ContentId));
    }
}
