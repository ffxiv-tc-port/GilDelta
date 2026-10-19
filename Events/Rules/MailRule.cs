using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using GilDelta.Wallet;

namespace GilDelta.Events.Rules;

/// <summary>
/// Gil moved by moogle letter: collected from a letter that is open, or attached to
/// one being written.
/// </summary>
/// <remarks>
/// Ordered below <see cref="CharacterTransferRule"/>, not above it. Gil the account
/// moves between its own characters travels by mail too, and that pairing is the more
/// specific answer; the pairing rule accepts a mail half as a counterparty, so the
/// first leg gets relabelled when the second one lands.
/// </remarks>
public sealed class MailRule : IInferenceRule
{
    /// <summary>The open letter an attachment is collected from.</summary>
    private const string ViewerAddon = "LetterViewer";

    /// <summary>The letter being written, which is where gil is attached.</summary>
    private const string EditorAddon = "LetterEditor";

    /// <summary>
    /// True when the letter that would explain this change was seen later than every
    /// one of <paramref name="ownAddons"/>: the caller's window is the one that closed.
    /// For rules above this one, whose position would otherwise hand them the letter.
    /// </summary>
    public static bool IsFresherSignal(WalletDiff diff, GameContext ctx, IReadOnlyList<string> ownAddons)
    {
        if (diff.Id.Kind != WalletKind.Self || diff.Delta == 0) return false;
        return ctx.SeenAfter(diff.Delta > 0 ? ViewerAddon : EditorAddon, ownAddons);
    }

    public bool TryClassify(WalletDiff diff, GameContext ctx, [NotNullWhen(true)] out GilEvent? ev)
    {
        ev = null;
        if (diff.Id.Kind != WalletKind.Self) return false;

        GilEventCategory category;
        if (diff.Delta > 0 && ctx.OpenAddons.Contains(ViewerAddon))
            category = GilEventCategory.MailReceive;
        else if (diff.Delta < 0 && ctx.OpenAddons.Contains(EditorAddon))
            category = GilEventCategory.MailSend;
        else
            return false;

        ev = new GilEvent(diff.At, diff.Id, diff.Delta, category,
            $"rule=MailRule; addons=[{string.Join(",", ctx.OpenAddons)}]");
        return true;
    }
}
