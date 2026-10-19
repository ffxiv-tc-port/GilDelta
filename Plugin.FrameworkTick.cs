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
    // --- Framework tick -----------------------------------------------------

    private void OnFrameworkTick(Dalamud.Plugin.Services.IFramework _)
    {
        // Dalamud invokes a plugin's Framework.Update subscribers as one multicast
        // delegate and catches around the whole thing (PluginErrorHandler.InvokeAndCatch),
        // so an exception escaping here would ALSO skip WalletWatcher's tick - which is
        // subscribed after this one - and silently stop recording for that frame.
        DrainCompletedRewrites();
        DrainToggleRequests();

        // The day just rolled over with the game still running: settle yesterday to disk
        // straight away rather than waiting for the next throttle window.
        // 🔴 The day an event belongs to is decided by the event's OWN timestamp
        // (DailyLedger.Add), not by this check, so nothing depends on the tick noticing
        // midnight promptly.
        var todayLocal = DateTime.Now.Date;
        if (todayLocal != _lastLedgerDate)
        {
            _lastLedgerDate = todayLocal;
            try
            {
                PersistLedger(DateTimeOffset.Now, force: true);
                Service.Log.Information("GilDelta: 已跨過午夜，前一天的每日結算已寫入");
            }
            catch (Exception e)
            {
                Service.Log.Warning(e, "GilDelta: settling the previous day failed");
            }
        }

        // Retention pass 2 of 2: once per local day. Only the date comparison runs per
        // frame; the sweep itself does not.
        var today = DateTime.Now.Date;
        if (today != _lastRetentionDate)
        {
            _lastRetentionDate = today;
            try
            {
                ApplyRetention(DateTimeOffset.Now);
            }
            catch (Exception e)
            {
                Service.Log.Warning(e, "GilDelta: the daily retention sweep failed");
            }
        }

        try
        {
            TrackActiveCharacter();
        }
        catch (Exception e)
        {
            Service.Log.Warning(e, "GilDelta: tracking the active character failed");
        }

        SampleAddonState();

        // Move IPC submissions onto this thread and drop the ones nobody claimed.
        // Cheap, and it keeps the active list from ageing only when gil happens to
        // move. (TryConsume drains too, so a hint is never missed by one tick.)
        try
        {
            _hints.Drain();
        }
        catch (Exception e)
        {
            Service.Log.Warning(e, "GilDelta: draining the IPC hint queue failed");
        }

        try
        {
            TrackWalletSnapshot();
        }
        catch (Exception e)
        {
            Service.Log.Warning(e, "GilDelta: updating the account wallet cache failed");
        }

        try
        {
            PersistLedger(DateTimeOffset.Now, force: false);
        }
        catch (Exception e)
        {
            Service.Log.Warning(e, "GilDelta: writing the daily settlements failed");
        }
    }

    /// <summary>
    /// Detects which character is loaded, on the framework thread. This — not the
    /// Login event — is the authority: Login fires before PlayerState is populated,
    /// and a character swap through the lobby has to reset the wallet baseline anyway.
    /// </summary>
    private void TrackActiveCharacter()
    {
        ulong contentId;
        try
        {
            contentId = Service.PlayerState.IsLoaded ? Service.PlayerState.ContentId : 0;
        }
        catch
        {
            return;  // mid-teardown; try again next tick
        }

        if (contentId != _activeContentId)
        {
            _activeContentId = contentId;
            _recentDiffs.Clear();
            _watcher?.Reset();

            if (contentId == 0)
            {
                _identityIncomplete = false;
                PersistSnapshots();
                Service.Log.Debug("GilDelta: no character loaded; wallet baseline reset");
            }
            else
            {
                _displayContentId = contentId;
                _identityIncomplete = true;
                _identityAttempts = 0;
                EnsureCharacterLoaded(contentId);
                Service.Log.Debug(
                    "GilDelta: active character is now contentId={Cid}; wallet baseline reset; login events since last switch={Logins}",
                    contentId, _loginEventsSinceSwitch);
                _loginEventsSinceSwitch = 0;
            }
        }

        if (_identityIncomplete && contentId != 0) RefreshIdentity(contentId);
    }

    /// <summary>
    /// Fills in the display name / home world once the game has them. Runs only while
    /// they are still missing, because reading them allocates on every call.
    /// </summary>
    private void RefreshIdentity(ulong contentId)
    {
        string name;
        string world;
        try
        {
            name  = Service.PlayerState.CharacterName ?? string.Empty;
            world = Service.PlayerState.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;
        }
        catch
        {
            return;
        }

        if (_characters.Observe(contentId, name, world))
        {
            InvalidateCharacterCache();
            _characterWriter.Queue(_characters.Serialize());
        }

        if (_characters.Get(contentId).IsComplete)
        {
            _identityIncomplete = false;
            LogLoginReady(contentId, partial: false);
            return;
        }

        if (++_identityAttempts >= IdentityAttemptLimit)
        {
            _identityIncomplete = false;
            LogLoginReady(contentId, partial: true);
        }
    }

    /// <summary>
    /// The single Information line a login is allowed to produce - everything else on
    /// the login path is Debug, so asking a user for "the login line" gets one line
    /// back. Fires once per character switch, from the framework thread; at Information
    /// only the first time per character and session, or when the summary changed.
    /// </summary>
    private void LogLoginReady(ulong contentId, bool partial)
    {
        var label = _characters.Get(contentId).Label;
        var chest = WalletReader.DescribeForLogin(_reader?.FreeCompanyChestState);
        var note = partial ? $"；顯示名稱等了 {_identityAttempts} 次仍不完整" : string.Empty;

        // A character re-entered with the same summary is not news; it still goes to Debug.
        if (_loginSummaries.IsNew(contentId, $"{label}|{chest}|{partial}"))
            Service.Log.Information(LoginReadyTemplate, label, chest, note);
        else
            Service.Log.Debug(LoginReadyTemplate, label, chest, note);
    }

    private void SampleAddonState()
    {
        try
        {
            AddonProbe.CollectOpen(Service.GameGui, _openAddons);
            _addonState.Tick(_openAddons);
        }
        catch
        {
            // GetAddonByName can throw if the game is mid-teardown; skip the tick.
        }
    }

    /// <summary>
    /// Keeps the per-character wallet cache current so an account-wide total can be
    /// assembled from characters that are not logged in.
    /// </summary>
    private void TrackWalletSnapshot()
    {
        if (_activeContentId == 0) return;

        var live = _watcher?.LastSnapshot;
        if (live is null || live.Count == 0) return;

        var now = DateTimeOffset.Now;
        if (_snapshots.Update(_activeContentId, live, now)) _snapshotsDirty = true;
        if (_snapshotsDirty && now - _lastSnapshotPersist >= SnapshotPersistInterval) PersistSnapshots();

        // 🔴 Read back out of the store rather than settling the day against `live`.
        // The store is what carries a wallet kind the game did not answer for across,
        // and the first reading of a local day - taken seconds after login, when the
        // retainer list usually has not arrived - is exactly the one that needs it.
        var reading = ActiveWallets() ?? live;

        // The opening / closing totals of the daily settlement. The first reading of a
        // local day becomes the opening figure and every later one moves the closing
        // figure; a day with no reading at all keeps them null, which is what makes the
        // settlement tab able to print "?" rather than a fabricated 0.
        if (_ledger.ObserveTotal(
                _activeContentId,
                DailyLedger.LocalDayOf(now),
                WalletExclusion.Total(reading),
                WalletExclusion.TotalExcludingFreeCompany(reading)))
        {
            MarkLedgerDirty(_activeContentId);
        }
    }

    private void PersistSnapshots()
    {
        if (!_snapshotsDirty) return;
        _snapshotsDirty = false;
        _lastSnapshotPersist = DateTimeOffset.Now;
        // Serialized here on the framework thread; only the file write is backgrounded.
        _snapshotWriter.Queue(_snapshots.Serialize());
    }

    private void EnsureCharacterLoaded(ulong contentId)
    {
        if (_characters.EnsureKnown(contentId))
        {
            InvalidateCharacterCache();
            _characterWriter.Queue(_characters.Serialize());
        }

        if (!_loadedCharacters.Add(contentId)) return;

        try
        {
            _log.AddRange(_stores.For(contentId).LoadAll());
            InvalidateCharacterCache();
        }
        catch (Exception e)
        {
            Service.Log.Warning(e, $"GilDelta: loading the event log for content id {contentId} failed");
        }

        // A character first seen mid-session: its settlements have not been read yet, so
        // read them and fold the events just loaded in. Startup already did this for
        // everyone with a file on disk, and BackfillFromEvents leaves settled days alone,
        // so doing it again here cannot double-count.
        try
        {
            var path = Path.Combine(_dailyRoot, $"{contentId}.jsonl");
            if (File.Exists(path))
            {
                _ledger.LoadFrom(contentId, File.ReadAllText(path));
                _ledgerVersion++;
            }

            var today = DateOnly.FromDateTime(DateTime.Now);
            foreach (var cid in _ledger.BackfillFromEvents(_log.For(contentId), today))
                MarkLedgerDirty(cid);
        }
        catch (Exception e)
        {
            Service.Log.Warning(e, $"GilDelta: loading the daily settlements for content id {contentId} failed");
        }
    }
}
