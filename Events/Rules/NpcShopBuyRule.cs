using System.Diagnostics.CodeAnalysis;
using System.Linq;
using GilDelta.Wallet;

namespace GilDelta.Events.Rules;

public sealed class NpcShopBuyRule : IInferenceRule
{
    private static readonly string[] ShopAddons = { "Shop", "InclusionShop" };

    public bool TryClassify(WalletDiff diff, GameContext ctx, [NotNullWhen(true)] out GilEvent? ev)
    {
        ev = null;
        if (diff.Id.Kind != WalletKind.Self) return false;
        if (diff.Delta >= 0) return false;
        if (!ShopAddons.Any(ctx.OpenAddons.Contains)) return false;
        // A letter seen later than this rule's own window is the fresher signal.
        if (MailRule.IsFresherSignal(diff, ctx, ShopAddons)) return false;

        ev = new GilEvent(
            Timestamp: diff.At,
            Wallet:    diff.Id,
            Amount:    diff.Delta,
            Category:  GilEventCategory.NpcShopBuy,
            Note:      $"rule=NpcShopBuyRule; addons=[{string.Join(",", ctx.OpenAddons)}]");
        return true;
    }
}
