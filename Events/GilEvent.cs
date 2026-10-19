using System;
using GilDelta.Wallet;

namespace GilDelta.Events;

public sealed record GilEvent(
    DateTimeOffset Timestamp,
    WalletId Wallet,
    long Amount,
    GilEventCategory Category,
    string? Note
)
{
    /// <summary>
    /// Content id of the character this event belongs to. 0 means "unknown owner"
    /// (a record written before multi-character support, with no owner we can trust).
    /// </summary>
    public ulong ContentId { get; init; }

    /// <summary>
    /// Set by the user to say "this is not something that really happened" - it stays in
    /// the log, verbatim, but stops counting towards anything. 🔴 Excluding rather
    /// than deleting is the whole point: a deletion cannot be undone, an exclusion is a
    /// flag the user can take straight back off.
    /// </summary>
    public bool Suspect { get; init; }
}
