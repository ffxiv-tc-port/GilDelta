using System.Diagnostics.CodeAnalysis;
using System.Linq;
using GilDelta.Wallet;

namespace GilDelta.Events.Rules;

/// <summary>
/// Money from selling to an NPC vendor: at a shop window, or through a retainer selling
/// to the vendor for the player.
/// </summary>
/// <remarks>
/// A retainer's vendor sale pays into the PLAYER's wallet while its sell list is open;
/// market proceeds land in the retainer's own wallet instead. With the bell menu or a
/// venture result also in the recency window the increase may be a withdrawal, so that
/// case is left to <see cref="RetainerSaleRule"/>.
/// </remarks>
public sealed class NpcShopSellRule : IInferenceRule
{
    private static readonly string[] ShopAddons = { "Shop", "InclusionShop" };

    private static readonly string[] RetainerSellAddons = { "RetainerSellList", "RetainerSell" };

    private static readonly string[] RetainerMenuAddons = { "RetainerList", "RetainerTaskResult" };

    public bool TryClassify(WalletDiff diff, GameContext ctx, [NotNullWhen(true)] out GilEvent? ev)
    {
        ev = null;
        if (diff.Id.Kind != WalletKind.Self) return false;
        if (diff.Delta <= 0) return false;

        // Either branch stands down for a letter seen later than its own windows: that
        // letter is the fresher signal.
        string note;
        if (ShopAddons.Any(ctx.OpenAddons.Contains) && !MailRule.IsFresherSignal(diff, ctx, ShopAddons))
            note = $"rule=NpcShopSellRule; addons=[{string.Join(",", ctx.OpenAddons)}]";
        else if (RetainerSellAddons.Any(ctx.OpenAddons.Contains) && !RetainerMenuAddons.Any(ctx.OpenAddons.Contains)
                 && !MailRule.IsFresherSignal(diff, ctx, RetainerSellAddons))
            note = $"rule=NpcShopSellRule; via=retainer; addons=[{string.Join(",", ctx.OpenAddons)}]";
        else
            return false;

        ev = new GilEvent(diff.At, diff.Id, diff.Delta, GilEventCategory.NpcShopSell, note);
        return true;
    }
}
