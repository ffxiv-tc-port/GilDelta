using System.Diagnostics.CodeAnalysis;
using GilDelta.Wallet;

namespace GilDelta.Events.Rules;

public sealed class MarketBoardBuyRule : IInferenceRule
{
    private static readonly string[] OwnAddons = { "ItemSearch" };

    public bool TryClassify(WalletDiff diff, GameContext ctx, [NotNullWhen(true)] out GilEvent? ev)
    {
        ev = null;
        if (diff.Id.Kind != WalletKind.Self) return false;
        if (diff.Delta >= 0) return false;
        if (!ctx.OpenAddons.Contains("ItemSearch")) return false;
        // A letter seen later than this rule's own window is the fresher signal.
        if (MailRule.IsFresherSignal(diff, ctx, OwnAddons)) return false;

        ev = new GilEvent(diff.At, diff.Id, diff.Delta, GilEventCategory.MarketBoardBuy,
            $"rule=MarketBoardBuyRule; addons=[{string.Join(",", ctx.OpenAddons)}]");
        return true;
    }
}
