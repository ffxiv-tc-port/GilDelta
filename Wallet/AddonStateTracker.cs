using System;
using System.Collections.Generic;

namespace GilDelta.Wallet;

/// <summary>
/// Rolling history of which Dalamud addons have been open recently.
/// The wallet watcher detects gil changes on the framework tick *after* the
/// game updates the value — by which time the player may have already closed
/// the Shop / Teleport / Repair addon. Tracking "addons seen in the last N
/// seconds" instead of "addons open right now" lets the rules still match the
/// just-closed window.
/// </summary>
public sealed class AddonStateTracker
{
    private readonly Dictionary<string, DateTimeOffset> _lastSeen = new();

    /// <summary>Refresh "last seen" timestamps for every addon currently open.</summary>
    /// <param name="currentlyOpen">Each name at most once. Read by index, so a reused list costs nothing.</param>
    public void Tick(IReadOnlyList<string> currentlyOpen)
    {
        var now = DateTimeOffset.Now;
        for (var i = 0; i < currentlyOpen.Count; i++)
            _lastSeen[currentlyOpen[i]] = now;
    }

    /// <summary>All addon names whose last-seen timestamp falls within <paramref name="window"/>.</summary>
    public IReadOnlySet<string> RecentlyOpen(TimeSpan window)
    {
        var threshold = DateTimeOffset.Now - window;
        var set = new HashSet<string>();
        foreach (var pair in _lastSeen)
            if (pair.Value >= threshold)
                set.Add(pair.Key);
        return set;
    }

    /// <summary>
    /// The same addons as <see cref="RecentlyOpen"/>, each with when it was last seen,
    /// so a rule can tell the window that is open now from one closed seconds ago.
    /// </summary>
    public IReadOnlyDictionary<string, DateTimeOffset> LastSeenWithin(TimeSpan window)
    {
        var threshold = DateTimeOffset.Now - window;
        var seen = new Dictionary<string, DateTimeOffset>();
        foreach (var pair in _lastSeen)
            if (pair.Value >= threshold)
                seen[pair.Key] = pair.Value;
        return seen;
    }
}
