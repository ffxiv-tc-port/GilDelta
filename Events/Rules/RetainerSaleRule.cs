using System.Diagnostics.CodeAnalysis;
using System.Linq;
using GilDelta.Wallet;

namespace GilDelta.Events.Rules;

/// <summary>
/// Money arriving from a retainer.
/// </summary>
/// <remarks>
/// The retainer's own wallet going up is unambiguous. 🔴 The player's wallet going up
/// needs corroboration: under unattended automation the retainer wallets are often not
/// readable when the money moves, so a retainer window open recently is what makes that
/// side attributable. ⚠️ <c>RetainerList</c> is the bell menu, which covers withdrawals
/// as well as sales, so a Self increase matched through it may really be a withdrawal -
/// a <c>RetainerWithdraw</c> hint from a plugin that knows runs upstream and wins.
/// </remarks>
public sealed class RetainerSaleRule : IInferenceRule
{
    /// <summary>
    /// Windows whose presence makes a player-side increase retainer money. With only the sell
    /// list open, <see cref="NpcShopSellRule"/> (earlier in the chain) has already taken it as a vendor sale.
    /// </summary>
    private static readonly string[] RetainerAddons =
        { "RetainerSellList", "RetainerSell", "RetainerList", "RetainerTaskResult" };

    public bool TryClassify(WalletDiff diff, GameContext ctx, [NotNullWhen(true)] out GilEvent? ev)
    {
        ev = null;
        if (diff.Delta <= 0) return false;

        switch (diff.Id.Kind)
        {
            case WalletKind.Retainer:
                ev = new GilEvent(
                    Timestamp: diff.At,
                    Wallet:    diff.Id,
                    Amount:    diff.Delta,
                    Category:  GilEventCategory.RetainerSale,
                    Note:      $"rule=RetainerSaleRule; retainer={diff.Id.Identifier}; +{diff.Delta}");
                return true;

            // A letter seen later than every retainer window is the fresher signal.
            case WalletKind.Self when RetainerAddons.Any(ctx.OpenAddons.Contains)
                                      && !MailRule.IsFresherSignal(diff, ctx, RetainerAddons):
                ev = new GilEvent(
                    Timestamp: diff.At,
                    Wallet:    diff.Id,
                    Amount:    diff.Delta,
                    Category:  GilEventCategory.RetainerSale,
                    Note:      $"rule=RetainerSaleRule; self; addons=[{string.Join(",", ctx.OpenAddons)}]");
                return true;

            default:
                return false;
        }
    }
}
