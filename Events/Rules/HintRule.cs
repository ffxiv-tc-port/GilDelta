using System.Diagnostics.CodeAnalysis;
using GilDelta.Wallet;

namespace GilDelta.Events.Rules;

/// <summary>
/// Classifies a wallet change from a hint another plugin pushed in over
/// <c>GilDelta.Hint</c>. This is what makes automation-heavy play classifiable at all:
/// unattended rounds open and close addons faster than a once-per-frame probe can see
/// them, so addon sniffing misses nearly everything.
/// </summary>
/// <remarks>
/// Ordered <em>after</em> <see cref="PairedTransferRule"/> and before every
/// addon-driven rule: a Self↔Retainer pair is self-evidently a transfer and needs no
/// help, while an addon that happened to be open is a much weaker signal than a
/// plugin saying what it did.
/// </remarks>
public sealed class HintRule : IInferenceRule
{
    public bool TryClassify(WalletDiff diff, GameContext ctx, [NotNullWhen(true)] out GilEvent? ev)
    {
        ev = null;
        if (ctx.Hints is null) return false;
        if (!ctx.Hints.TryConsume(diff, out var hint)) return false;

        var note = string.IsNullOrWhiteSpace(hint.Note) ? hint.Category.ToString() : hint.Note;

        ev = new GilEvent(
            Timestamp: diff.At,
            Wallet:    diff.Id,
            Amount:    diff.Delta,
            Category:  hint.Category,
            Note:      $"rule=HintRule; hint={note}");
        return true;
    }
}
