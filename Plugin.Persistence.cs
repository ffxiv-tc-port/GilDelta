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
    // --- Startup ------------------------------------------------------------

    private void LoadPersistedState(string configDir)
    {
        TryLoadJson(Path.Combine(configDir, "characters.json"), _characters.LoadFrom, "characters.json");
        TryLoadJson(Path.Combine(configDir, "wallets.json"),    _snapshots.LoadFrom,  "wallets.json");

        foreach (var contentId in _stores.KnownContentIds())
        {
            try
            {
                _log.AddRange(_stores.For(contentId).LoadAll());
                _loadedCharacters.Add(contentId);
                _characters.EnsureKnown(contentId);
            }
            catch (Exception e)
            {
                Service.Log.Warning(e, $"GilDelta: loading the event log for content id {contentId} failed");
            }
        }

        InvalidateCharacterCache();

        // Information level on purpose: this is the line to ask a user for when
        // multi-character records look wrong.
        Service.Log.Information(
            "GilDelta: loaded {Events} gil records for {Characters} character(s) [{Ids}]",
            _log.All.Count, _loadedCharacters.Count, string.Join(",", _loadedCharacters));

        var now = DateTimeOffset.Now;

        // 🔴 Settlements are loaded and backfilled BEFORE retention runs. That ordering
        // is the whole point: a day whose events retention is about to delete still gets
        // its totals written down first, and on every later start the stored row is the
        // one that stands.
        LoadLedger(now);

        // Retention pass 1 of 2: at load, before the WalletWatcher exists, so nothing
        // can append while these files are being rewritten.
        _lastRetentionDate = now.LocalDateTime.Date;
        ApplyRetention(now);
    }

    // --- Daily settlements --------------------------------------------------

    private void LoadLedger(DateTimeOffset now)
    {
        _lastLedgerDate = now.LocalDateTime.Date;

        try
        {
            Directory.CreateDirectory(_dailyRoot);
            foreach (var file in Directory.EnumerateFiles(_dailyRoot, "*.jsonl"))
            {
                // Same rule as EventStoreSet: a file whose name is not a bare content id
                // (backups, temp files, anything dropped in by hand) is skipped, never
                // guessed at.
                if (!ulong.TryParse(Path.GetFileNameWithoutExtension(file), out var cid)) continue;

                try
                {
                    _ledger.LoadFrom(cid, File.ReadAllText(file));
                    _ledgerVersion++;
                }
                catch (Exception e)
                {
                    Service.Log.Warning(e, $"GilDelta: loading the daily settlements for content id {cid} failed");
                }
            }
        }
        catch (Exception e)
        {
            Service.Log.Warning(e, "GilDelta: reading the daily settlement folder failed");
        }

        try
        {
            foreach (var cid in _ledger.BackfillFromEvents(_log.All, DateOnly.FromDateTime(now.Date)))
                MarkLedgerDirty(cid);
        }
        catch (Exception e)
        {
            Service.Log.Warning(e, "GilDelta: rebuilding the daily settlements from the event log failed");
        }

        PersistLedger(now, force: true);

        // Information level on purpose: this is the line to ask a user for when the
        // daily settlement figures look wrong.
        Service.Log.Information(
            "GilDelta: 每日結算已載入 {Days} 天（{Characters} 個角色）",
            CountLedgerDays(), _ledger.ContentIds.Count);
    }

    /// <summary>
    /// One place that both queues the file rewrite and invalidates the dashboard's
    /// cached view. Splitting the two is how one of them ends up forgotten.
    /// </summary>
    private void MarkLedgerDirty(ulong contentId)
    {
        _ledgerDirty.Add(contentId);
        _ledgerVersion++;
    }

    private int CountLedgerDays()
    {
        var days = 0;
        foreach (var cid in _ledger.ContentIds) days += _ledger.For(cid).Count;
        return days;
    }

    private SerialJsonWriter DailyWriterFor(ulong contentId)
    {
        if (_dailyWriters.TryGetValue(contentId, out var writer)) return writer;

        writer = new SerialJsonWriter(
            Path.Combine(_dailyRoot, $"{contentId}.jsonl"),
            e => Service.Log.Warning(e, $"GilDelta: writing the daily settlements for content id {contentId} failed"));
        _dailyWriters[contentId] = writer;
        return writer;
    }

    /// <summary>
    /// Writes out every character whose settlements moved. Serialized here on the
    /// framework thread and handed to a background writer, so no dictionary is walked
    /// while a file is being written.
    /// </summary>
    private void PersistLedger(DateTimeOffset now, bool force)
    {
        if (_ledgerDirty.Count == 0) return;
        if (!force && now - _lastLedgerPersist < LedgerPersistInterval) return;

        _lastLedgerPersist = now;

        foreach (var contentId in _ledgerDirty)
        {
            try
            {
                DailyWriterFor(contentId).Queue(_ledger.Serialize(contentId));
            }
            catch (Exception e)
            {
                Service.Log.Warning(e, $"GilDelta: rendering the daily settlements for content id {contentId} failed");
            }
        }

        _ledgerDirty.Clear();
    }

    // --- Retention ----------------------------------------------------------

    /// <summary>
    /// Deletes records older than <see cref="Configuration.RetentionDays"/>, one
    /// character's file at a time - the unknown-owner bucket included.
    /// </summary>
    /// <remarks>
    /// 0 (the default, and therefore what every existing user has) means nothing is
    /// ever deleted; <see cref="Retention.CutoffFor"/> owns that rule.
    /// </remarks>
    private void ApplyRetention(DateTimeOffset now)
    {
        if (Retention.CutoffFor(_config.RetentionDays, now) is not { } cutoff) return;

        // Snapshot the ids: Prune mutates the buckets being walked.
        foreach (var contentId in new List<ulong>(_log.ContentIds))
        {
            int removed;
            try
            {
                removed = _log.Prune(contentId, cutoff);
            }
            catch (Exception e)
            {
                Service.Log.Warning(e, $"GilDelta: pruning content id {contentId} failed");
                continue;
            }

            if (removed == 0) continue;  // nothing deleted: no rewrite, no log line

            RewriteEventFile(contentId);

            Service.Log.Information(
                "保留 {Days} 天：已清除 {Removed} 筆早於 {Cutoff:yyyy-MM-dd HH:mm} 的紀錄（角色 {Character}）",
                _config.RetentionDays, removed, cutoff.LocalDateTime, LabelForLog(contentId));
        }
    }

    private string LabelForLog(ulong contentId) =>
        contentId == CharacterInfo.UnknownContentId
            ? $"{Strings.CharacterUnknown} ({contentId})"
            : $"{_characters.Get(contentId).Label} ({contentId})";

    /// <summary>
    /// Re-renders one character's whole file from the in-memory log. Serialized here on
    /// the framework thread; the write itself goes to the background through
    /// <see cref="SerialJsonWriter"/> (temp file + rename, last write wins).
    /// </summary>
    private void RewriteEventFile(ulong contentId)
    {
        string payload;
        try
        {
            payload = _stores.For(contentId).SerializeAll(_log.For(contentId));
        }
        catch (Exception e)
        {
            Service.Log.Warning(e, $"GilDelta: rendering the event log for content id {contentId} failed");
            return;
        }

        _rewriteInFlight.Add(contentId);
        _appendSkippedDuringRewrite.Remove(contentId);
        EventWriterFor(contentId).Queue(payload);
    }

    private SerialJsonWriter EventWriterFor(ulong contentId)
    {
        if (_eventWriters.TryGetValue(contentId, out var writer)) return writer;

        writer = new SerialJsonWriter(
            _stores.PathFor(contentId),
            e => Service.Log.Warning(e, $"GilDelta: rewriting the event log for content id {contentId} failed"),
            () => _rewriteCompleted.Enqueue(contentId));
        _eventWriters[contentId] = writer;
        return writer;
    }

    /// <summary>
    /// Closes the window between "serialize for a rewrite" and "the rewrite lands":
    /// a gil event in that window skipped its append, so the file is re-rendered now
    /// that the rewrite has finished.
    /// </summary>
    private void DrainCompletedRewrites()
    {
        while (_rewriteCompleted.TryDequeue(out var contentId))
        {
            _rewriteInFlight.Remove(contentId);
            if (_appendSkippedDuringRewrite.Remove(contentId)) RewriteEventFile(contentId);
        }
    }

    private static void TryLoadJson(string path, Action<string> load, string label)
    {
        try
        {
            if (File.Exists(path)) load(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Service.Log.Warning(e, $"GilDelta: reading {label} failed");
        }
    }
}
