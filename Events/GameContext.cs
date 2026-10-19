using System;
using System.Collections.Generic;
using GilDelta.Wallet;

namespace GilDelta.Events;

public sealed class GameContext
{
    public IReadOnlySet<string> OpenAddons { get; }
    public IReadOnlyList<WalletDiff> RecentDiffs { get; }
    public DateTimeOffset Now { get; }

    /// <summary>
    /// Hints other plugins pushed in over IPC, or null when there are none on offer.
    /// 🔴 Null is not merely "empty": consuming a hint <em>removes</em> it, so
    /// replaying history (<c>EventStore.Reclassify</c>) has to be handed no source.
    /// </summary>
    public IHintSource? Hints { get; }

    /// <summary>
    /// The character this wallet change belongs to; 0 = unknown owner. A
    /// <see cref="WalletDiff"/> carries no owner, so rules that compare characters
    /// have to be told.
    /// </summary>
    public ulong ContentId { get; }

    /// <summary>
    /// Events already recorded for every character on the account, for rules that
    /// pair across characters. Empty during replay, so a replay can never re-pair
    /// history it has already paired once.
    /// </summary>
    public IReadOnlyList<GilEvent> RecentEvents { get; }

    /// <summary>
    /// When each of <see cref="OpenAddons"/> was last seen, or null when only the names
    /// are known (replay, most tests). Null means no addon outranks another, so rule
    /// order alone decides.
    /// </summary>
    public IReadOnlyDictionary<string, DateTimeOffset>? AddonLastSeen { get; }

    public GameContext(
        IReadOnlySet<string> openAddons,
        IReadOnlyList<WalletDiff> recentDiffs,
        DateTimeOffset now,
        IHintSource? hints = null,
        ulong contentId = 0,
        IReadOnlyList<GilEvent>? recentEvents = null,
        IReadOnlyDictionary<string, DateTimeOffset>? addonLastSeen = null)
    {
        OpenAddons   = openAddons;
        RecentDiffs  = recentDiffs;
        Now          = now;
        Hints        = hints;
        ContentId    = contentId;
        RecentEvents = recentEvents ?? Array.Empty<GilEvent>();
        AddonLastSeen = addonLastSeen;
    }

    /// <summary>
    /// True when <paramref name="addon"/> is in the window and was seen strictly later
    /// than every one of <paramref name="others"/> that is. False whenever an age is
    /// unknown, and on a tie.
    /// </summary>
    public bool SeenAfter(string addon, IReadOnlyList<string> others)
    {
        if (AddonLastSeen is null) return false;
        if (!OpenAddons.Contains(addon) || !AddonLastSeen.TryGetValue(addon, out var at)) return false;

        foreach (var other in others)
        {
            if (!OpenAddons.Contains(other)) continue;
            if (!AddonLastSeen.TryGetValue(other, out var otherAt) || otherAt >= at) return false;
        }
        return true;
    }

    public static GameContext Empty(DateTimeOffset now) =>
        new(new HashSet<string>(), Array.Empty<WalletDiff>(), now);
}
