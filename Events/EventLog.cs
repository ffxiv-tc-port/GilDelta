using System;
using System.Collections.Generic;

namespace GilDelta.Events;

/// <summary>
/// In-memory cache of the account's events — every character the plugin has records
/// for, not just the one being played. Loaded from <see cref="EventStore"/> at
/// startup; updated via Add() when WalletWatcher emits new events. Windows read from
/// this list, never directly from disk.
/// Per-character buckets are maintained as events go in, so a view that filters to
/// one character costs nothing per frame.
/// </summary>
public sealed class EventLog
{
    private static readonly IReadOnlyList<GilEvent> Empty = Array.Empty<GilEvent>();

    private readonly List<GilEvent> _events = new();
    private readonly Dictionary<ulong, List<GilEvent>> _byCharacter = new();

    /// <summary>Every event, every character.</summary>
    public IReadOnlyList<GilEvent> All => _events;

    /// <summary>
    /// Bumped by every change to the cached events, so a view can tell whether what it
    /// built from them is still current without comparing contents.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>
    /// Bumped by every change other than an append (replace, prune, clear), so a view that
    /// folds new records in as they arrive knows when it has to start over.
    /// </summary>
    public long RewriteVersion { get; private set; }

    /// <summary>Content ids that actually have records loaded (0 = unknown owner).</summary>
    public IReadOnlyCollection<ulong> ContentIds => _byCharacter.Keys;

    /// <summary>Replaces the cache with one store's contents.</summary>
    public void LoadFromStore(EventStore store)
    {
        Clear();
        AddRange(store.LoadAll());
    }

    /// <summary>Appends a store's contents without dropping what is already cached.</summary>
    public void AddRange(IEnumerable<GilEvent> events)
    {
        foreach (var ev in events) Add(ev);
    }

    public void Add(GilEvent ev)
    {
        Version++;
        _events.Add(ev);
        if (!_byCharacter.TryGetValue(ev.ContentId, out var bucket))
        {
            bucket = new List<GilEvent>();
            _byCharacter[ev.ContentId] = bucket;
        }
        bucket.Add(ev);
    }

    /// <summary>Events belonging to one character. Never null; an unseen id yields an empty list.</summary>
    public IReadOnlyList<GilEvent> For(ulong contentId) =>
        _byCharacter.TryGetValue(contentId, out var bucket) ? bucket : Empty;

    /// <summary>
    /// Drops one character's records that are older than <paramref name="cutoff"/>.
    /// </summary>
    /// <remarks>
    /// Strictly older: a record sitting exactly on the cutoff is inside the retention
    /// window and is kept. Retention is a deletion, so the boundary errs towards
    /// keeping data.
    /// </remarks>
    /// <returns>How many records were removed; 0 means the file needs no rewrite.</returns>
    public int Prune(ulong contentId, DateTimeOffset cutoff)
    {
        if (!_byCharacter.TryGetValue(contentId, out var bucket)) return 0;

        var removed = bucket.RemoveAll(ev => ev.Timestamp < cutoff);
        if (removed == 0) return 0;

        Version++;
        RewriteVersion++;
        _events.RemoveAll(ev => ev.ContentId == contentId && ev.Timestamp < cutoff);
        return removed;
    }

    /// <summary>
    /// Swaps one record for an edited copy of itself, in both the flat list and the
    /// owning character's bucket, so the two can never drift apart.
    /// </summary>
    /// <remarks>
    /// Matching is by value, because <see cref="GilEvent"/> is a record and that is what
    /// <c>IndexOf</c> uses. The replacement must belong to the same character; moving a
    /// record between owners through here would leave it in the wrong bucket.
    /// </remarks>
    /// <returns>false when the record is not in the cache, or would change owner.</returns>
    public bool Replace(GilEvent original, GilEvent replacement)
    {
        if (replacement.ContentId != original.ContentId) return false;
        if (!_byCharacter.TryGetValue(original.ContentId, out var bucket)) return false;

        var inBucket = bucket.IndexOf(original);
        if (inBucket < 0) return false;

        var inAll = _events.IndexOf(original);
        if (inAll < 0) return false;

        Version++;
        RewriteVersion++;
        bucket[inBucket] = replacement;
        _events[inAll] = replacement;
        return true;
    }

    public void Clear()
    {
        Version++;
        RewriteVersion++;
        _events.Clear();
        _byCharacter.Clear();
    }

    public IEnumerable<GilEvent> Since(DateTimeOffset threshold)
    {
        foreach (var ev in _events)
            if (ev.Timestamp >= threshold)
                yield return ev;
    }
}
