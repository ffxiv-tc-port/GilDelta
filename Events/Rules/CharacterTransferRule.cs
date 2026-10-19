using System;
using System.Diagnostics.CodeAnalysis;
using GilDelta.Wallet;

namespace GilDelta.Events.Rules;

/// <summary>
/// Gil the account moved between two of its own characters - by mail, in practice,
/// which is why the addon signal alone cannot tell it from any other letter.
/// </summary>
/// <remarks>
/// The defence against false positives is position plus a narrow counterparty: every
/// rule but <see cref="MailRule"/> and <see cref="MiscRule"/> gets first refusal, and
/// only a prior one of those two produced may be claimed. Claiming one relabels it, so
/// a given counterparty can be consumed exactly once.
/// </remarks>
public sealed class CharacterTransferRule : IInferenceRule
{
    private readonly Func<TimeSpan> _window;
    private readonly Action<GilEvent, ulong>? _onCounterpartMatched;

    /// <param name="window">
    /// How far apart the two halves may sit. Read per classification, so changing the
    /// setting takes effect without a restart.
    /// </param>
    /// <param name="onCounterpartMatched">
    /// (the prior event, the character this diff belongs to) when a pair is made, so the
    /// owner can relabel that half too. Runs on the framework thread, inside Classify.
    /// </param>
    public CharacterTransferRule(Func<TimeSpan> window, Action<GilEvent, ulong>? onCounterpartMatched = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _onCounterpartMatched = onCounterpartMatched;
    }

    public bool TryClassify(WalletDiff diff, GameContext ctx, [NotNullWhen(true)] out GilEvent? ev)
    {
        ev = null;

        // A transfer between characters lands in the player's own wallet on both sides,
        // and an unknown owner cannot be told apart from the character being played.
        if (diff.Id.Kind != WalletKind.Self) return false;
        if (diff.Delta == 0) return false;
        if (ctx.ContentId == 0) return false;
        if (ctx.RecentEvents.Count == 0) return false;

        var window = _window();
        GilEvent? best = null;
        var bestGap = TimeSpan.MaxValue;

        foreach (var prior in ctx.RecentEvents)
        {
            if (!IsCounterpart(prior, diff, ctx.ContentId)) continue;

            var gap = Abs(diff.At - prior.Timestamp);
            if (gap > window) continue;
            if (best is not null && gap >= bestGap) continue;

            best = prior;
            bestGap = gap;
        }

        if (best is null) return false;

        ev = new GilEvent(
            Timestamp: diff.At,
            Wallet:    diff.Id,
            Amount:    diff.Delta,
            Category:  GilEventCategory.CharacterTransfer,
            Note:      $"rule=CharacterTransferRule; counterparty=cid:{best.ContentId}; " +
                       $"gap={(long)bestGap.TotalSeconds}s");

        _onCounterpartMatched?.Invoke(best, ctx.ContentId);
        return true;
    }

    private static bool IsCounterpart(GilEvent prior, WalletDiff diff, ulong owner)
    {
        if (prior.Suspect) return false;

        if (!IsPairable(prior.Category)) return false;
        if (prior.Wallet.Kind != WalletKind.Self) return false;
        if (prior.ContentId == 0 || prior.ContentId == owner) return false;

        return prior.Amount + diff.Delta == 0;
    }

    /// <summary>
    /// Categories a transfer may claim the earlier half from: Misc, and the two mail
    /// halves that go with the same letter. Any other category is another rule's
    /// evidence, and a CharacterTransfer has already been paired off once.
    /// </summary>
    internal static bool IsPairable(GilEventCategory category) =>
        category is GilEventCategory.Misc
            or GilEventCategory.MailSend
            or GilEventCategory.MailReceive;

    private static TimeSpan Abs(TimeSpan t) => t < TimeSpan.Zero ? t.Negate() : t;
}
