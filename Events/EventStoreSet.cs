using System;
using System.Collections.Generic;
using System.IO;

namespace GilDelta.Events;

/// <summary>
/// The account's event log: one <see cref="EventStore"/> per character, all under
/// <c>&lt;pluginConfigDir&gt;/events/</c> as <c>&lt;contentId&gt;.jsonl</c>.
/// One file per character (rather than one shared file with a character column) is
/// deliberate: appends stay O(1) and never touch another character's data, a
/// corrupt file can only ever cost one character's history, and the file name
/// doubles as the recorded owner for rows written before the <c>cid</c> field existed.
/// </summary>
public sealed class EventStoreSet
{
    private readonly string _root;
    private readonly Dictionary<ulong, EventStore> _stores = new();

    public EventStoreSet(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public string PathFor(ulong contentId) => Path.Combine(_root, $"{contentId}.jsonl");

    /// <summary>Gets (and caches) the store for one character.</summary>
    public EventStore For(ulong contentId)
    {
        if (_stores.TryGetValue(contentId, out var store)) return store;
        store = new EventStore(PathFor(contentId), contentId);
        _stores[contentId] = store;
        return store;
    }

    /// <summary>
    /// Every character that already has a file on disk, oldest id first.
    /// Files whose name is not a bare content id (backups, temp files, anything a
    /// user dropped in) are skipped rather than guessed at.
    /// </summary>
    public IReadOnlyList<ulong> KnownContentIds()
    {
        var ids = new List<ulong>();
        if (!Directory.Exists(_root)) return ids;

        foreach (var file in Directory.EnumerateFiles(_root, "*.jsonl"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (ulong.TryParse(name, out var cid)) ids.Add(cid);
        }

        ids.Sort();
        return ids;
    }

    /// <summary>
    /// Loads every character's records. A single unreadable file is reported through
    /// <paramref name="onError"/> and skipped — one broken character must not take
    /// the rest of the account down with it.
    /// </summary>
    public List<GilEvent> LoadAll(Action<ulong, Exception>? onError = null)
    {
        var all = new List<GilEvent>();
        foreach (var cid in KnownContentIds())
        {
            try
            {
                all.AddRange(For(cid).LoadAll());
            }
            catch (Exception e)
            {
                onError?.Invoke(cid, e);
            }
        }
        return all;
    }
}
