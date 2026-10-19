using System.Diagnostics.CodeAnalysis;
using GilDelta.Wallet;

namespace GilDelta.Events.Rules;

/// <summary>
/// The character spent gil with the aetheryte teleport menu open recently.
/// </summary>
/// <remarks>
/// 🔴 The defence against false positives is <b>position</b>: this rule sits below every shop,
/// repair and retainer rule (only <see cref="CharacterTransferRule"/>, <see cref="MailRule"/> and
/// <see cref="MiscRule"/> come later), so a wrong answer needs a spend none of those explained.
/// Only <c>Teleport</c> counts; <c>TelepotTown</c> (the aethernet menu, misspelled in
/// the game itself) is watched for the breadcrumb but never matched: aethernet is free.
/// </remarks>
public sealed class TeleportRule : IInferenceRule
{
    private static readonly string[] OwnAddons = { "Teleport" };

    public bool TryClassify(WalletDiff diff, GameContext ctx, [NotNullWhen(true)] out GilEvent? ev)
    {
        ev = null;
        if (diff.Id.Kind != WalletKind.Self) return false;
        if (diff.Delta >= 0) return false;
        if (!ctx.OpenAddons.Contains("Teleport")) return false;

        // Position cannot protect a letter, which sits below this rule: a letter being
        // written after the menu closed is where the gil went. The other way round -
        // menu opened after the letter - stays a teleport.
        if (MailRule.IsFresherSignal(diff, ctx, OwnAddons)) return false;

        ev = new GilEvent(diff.At, diff.Id, diff.Delta, GilEventCategory.Teleport,
            $"rule=TeleportRule; addons=[{string.Join(",", ctx.OpenAddons)}]");
        return true;
    }
}
