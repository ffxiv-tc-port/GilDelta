using GilDelta.Wallet;

namespace GilDelta.Events;

/// <summary>
/// Decides whether a hint of a given category may claim a given wallet change. Both
/// halves matter: the wallet the money moved in, and which way it moved - without that
/// a leftover "Teleport" hint is eaten by the venture payout landing two seconds later.
/// </summary>
public static class HintScope
{
    public static bool Matches(GilEventCategory category, WalletKind kind, long delta)
    {
        if (delta == 0) return false;
        return KindMatches(category, kind) && DirectionMatches(category, delta);
    }

    private static bool KindMatches(GilEventCategory category, WalletKind kind) => category switch
    {
        GilEventCategory.SubmarineReturn => kind == WalletKind.FreeCompanyChest,

        // Retainer money reaches the player either in the retainer's own wallet or —
        // once something like AutoRetainer has already withdrawn it — in the
        // player's. Both readings are legitimate for a retainer-flavoured hint.
        GilEventCategory.RetainerSale
            or GilEventCategory.RetainerDeposit
            or GilEventCategory.RetainerWithdraw => kind is WalletKind.Retainer or WalletKind.Self,

        // "I know what this is, you just have no category for it."
        GilEventCategory.Misc => true,

        // Everything else is money the character spends or earns directly.
        _ => kind == WalletKind.Self,
    };

    private static bool DirectionMatches(GilEventCategory category, long delta) => category switch
    {
        GilEventCategory.NpcShopBuy
            or GilEventCategory.MarketBoardBuy
            or GilEventCategory.Repair
            or GilEventCategory.Teleport
            or GilEventCategory.MailSend => delta < 0,

        GilEventCategory.NpcShopSell
            or GilEventCategory.SubmarineReturn
            or GilEventCategory.RetainerSale
            or GilEventCategory.MailReceive => delta > 0,

        // Deposit / withdraw show up on both sides of the same transfer, and Misc is
        // deliberately unconstrained.
        _ => true,
    };
}
