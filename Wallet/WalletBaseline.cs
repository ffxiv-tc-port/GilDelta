using System;
using System.Collections.Generic;

namespace GilDelta.Wallet;

/// <summary>
/// The last known balance of each wallet, and the one place that decides whether a fresh
/// reading is worth reporting as a change.
/// 🔴 The invariant, and the whole point of this class: <b>a diff is only ever emitted
/// between two consecutive KNOWN readings of the same wallet.</b> A reading we could not
/// take emits nothing <em>and drops the baseline</em>, so that whatever comes back later
/// re-seeds silently instead of being diffed across the blind gap.
/// </summary>
public sealed class WalletBaseline
{
    private readonly Dictionary<WalletId, long> _known = new();

    /// <summary>How many wallets currently hold a baseline.</summary>
    public int Count => _known.Count;

    /// <summary>Forgets every baseline. The next readings re-seed without emitting.</summary>
    public void Clear() => _known.Clear();

    /// <summary>The baseline held for one wallet, or null when it has none.</summary>
    public long? BaselineOf(WalletId id) => _known.TryGetValue(id, out var v) ? v : null;

    /// <summary>
    /// Feeds one reading in and returns the change to report, or <c>null</c> when there
    /// is nothing to report - which covers all of: an unreadable reading, the first
    /// reading of a wallet, the first reading after an unreadable one, and a reading that
    /// simply did not move.
    /// </summary>
    public WalletDiff? Observe(Wallet wallet, DateTimeOffset at)
    {
        if (wallet.Amount is not { } amount)
        {
            _known.Remove(wallet.Id);
            return null;
        }

        WalletDiff? diff = null;
        if (_known.TryGetValue(wallet.Id, out var previous) && previous != amount)
            diff = new WalletDiff(wallet.Id, previous, amount, at);

        _known[wallet.Id] = amount;
        return diff;
    }
}
