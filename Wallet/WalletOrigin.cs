using System;

namespace GilDelta.Wallet;

/// <summary>
/// One wallet row plus where the figure came from, so a breakdown can say more than
/// "this number exists": which character it belongs to, and whether it was read just
/// now or is the last thing we saw.
/// </summary>
/// <param name="Wallet">The wallet itself. <c>Amount</c> is null when it could not be read.</param>
/// <param name="ContentId">The character this reading was taken from. 0 = not attributed.</param>
/// <param name="At">
/// When the reading was taken, or null when that is not known either. 🔴 Null here is
/// "we do not know when", not "just now" - the view prints "?" rather than a time.
/// </param>
/// <param name="IsLive">
/// True when the game answered for this exact wallet on the frame the row was built.
/// 🔴 This is NOT "belongs to the character being played": a retainer row can belong
/// to the character on screen and still be a figure from an hour ago.
/// </param>
public readonly record struct WalletOrigin(
    Wallet Wallet,
    ulong ContentId,
    DateTimeOffset? At,
    bool IsLive = false);
