using System;
using GilDelta.Events.Rules;

namespace GilDelta.Events;

/// <summary>
/// The one rule chain, in priority order: the first rule to claim a change wins. The
/// plugin and the tests both build from here, so the order cannot drift between them.
/// </summary>
public static class RuleChain
{
    public static IInferenceRule[] Build(
        Func<TimeSpan> characterTransferWindow,
        Action<GilEvent, ulong>? onCounterpartMatched = null) => new IInferenceRule[]
    {
        // PairedTransferRule first: a Self<->Retainer pair is self-evidently a
        // transfer and needs no help. HintRule then outranks every addon-driven
        // rule, because "a plugin told us what it just did" beats "an addon
        // happened to be open".
        new PairedTransferRule(),
        new HintRule(),
        new FreeCompanyChestDepositRule(),
        new NpcShopBuyRule(),
        new NpcShopSellRule(),
        new MarketBoardBuyRule(),
        new RepairRule(),
        // Below the shop rules on purpose. Its retainer-wallet half cannot collide
        // with them (they are all Self-only), while its player-wallet half is the
        // weaker inference of the two and should lose to a shop that was open.
        new RetainerSaleRule(),
        // Below every shop, repair and retainer rule: see the remarks on TeleportRule.
        // Those get first refusal, so a false positive needs a spend that none of them
        // could explain.
        new TeleportRule(),
        // Below everything else for the same reason as TeleportRule, and it only
        // pairs against a counterparty no other rule claimed: both halves have to
        // be an unexplained change, or a mail half.
        new CharacterTransferRule(characterTransferWindow, onCounterpartMatched),
        // Below CharacterTransferRule on purpose: gil the account moves between its
        // own characters is sent by mail, so both rules see the same addon and the
        // pairing is the more specific answer.
        new MailRule(),
        new MiscRule(),
    };
}
