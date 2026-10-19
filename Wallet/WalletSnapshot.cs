using System;
using System.Collections.Generic;

namespace GilDelta.Wallet;

/// <summary>
/// The last known wallet contents for one character.
/// </summary>
public sealed record WalletSnapshot(
    ulong ContentId,
    DateTimeOffset At,
    IReadOnlyList<Wallet> Wallets)
{
    /// <summary>
    /// Sum of the wallets we actually have a figure for. A wallet we could not read
    /// contributes nothing - so this is a <em>lower bound</em> whenever
    /// <see cref="HasUnknown"/> is true, which is why the views pair the two.
    /// </summary>
    public long Total
    {
        get
        {
            long sum = 0;
            foreach (var w in Wallets)
                if (w.Amount is { } amount) sum += amount;
            return sum;
        }
    }

    /// <summary>True when at least one wallet in this reading could not be read.</summary>
    public bool HasUnknown
    {
        get
        {
            foreach (var w in Wallets)
                if (!w.IsKnown) return true;
            return false;
        }
    }
}
