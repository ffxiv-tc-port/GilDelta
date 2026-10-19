using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace GilDelta.Wallet;

public static class AddonProbe
{
    /// <summary>
    /// Names of Dalamud addons the inference rules care about. An entry nothing reads
    /// yet still earns its keep: it shows up in the <c>recentAddons=[...]</c> breadcrumb.
    /// </summary>
    /// <remarks>
    /// ⚠️ These are addon names, which are language-independent — do not "translate"
    /// them for the TC client.
    /// 🔴 <c>TelepotTown</c> really is spelled that way in the game. The correctly
    /// spelled "TeleportTown" matches nothing and fails silently.
    /// </remarks>
    private static readonly string[] Watched = {
        // NPC shops
        "Shop", "ShopExchangeCurrency", "ShopExchangeItem", "InclusionShop",
        // Retainer market listing (treated as Shop-equivalent for sells)
        "RetainerSellList", "RetainerSell",
        // Retainer bell menu + venture payout summary. Under unattended automation
        // these are the only windows that open at all around retainer gil.
        "RetainerList", "RetainerTaskResult",
        // Marketboard
        "ItemSearch", "ItemSearchResult",
        // Repair
        "Repair",
        // Teleport: the aetheryte menu, and the (misspelled) aethernet menu. Aethernet
        // trips are free, so TelepotTown is a diagnostic breadcrumb rather than a
        // signal — that is why TeleportRule looks only at "Teleport".
        "Teleport", "TelepotTown",
        // Grand Company supply/provisioning hand-ins, which pay gil
        "GrandCompanySupplyList", "GrandCompanySupplyReward",
        // FC chest, so an FC-chest change can be told apart from a submarine payout
        "FreeCompanyChest",
        // Moogle mail. LetterViewer is open while an attachment is collected and
        // LetterEditor while one is attached; LetterList is open in both directions,
        // so it is a breadcrumb only and MailRule does not look at it.
        "LetterList", "LetterViewer", "LetterEditor",
    };

    /// <summary>
    /// Replaces the contents of <paramref name="sink"/> with the watched addons that are
    /// loaded right now, in <see cref="Watched"/> order. The caller owns and reuses the
    /// list, so sampling every frame allocates nothing.
    /// </summary>
    public static void CollectOpen(IGameGui gui, List<string> sink)
    {
        sink.Clear();
        foreach (var name in Watched)
        {
            var addon = gui.GetAddonByName(name);
            // Dalamud API 15 returns AtkUnitBasePtr; .Address != 0 means the addon is loaded.
            // (For "visible" specifically, would need .IsVisible; "loaded" is fine for v1.)
            if (addon.Address != 0)
                sink.Add(name);
        }
    }
}
