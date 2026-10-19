using System.Collections.Generic;
using GilDelta.Events;
using GilDelta.Events.Rules;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events;

public class RuleChainTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// The chain the plugin itself runs, not a copy of it. When a test in here fails,
    /// rule order is the first thing to look at.
    /// </summary>
    private static Inferrer FullChain() => new(RuleChain.Build(() => TimeSpan.FromMinutes(30)));

    [Fact]
    public void A_letter_from_another_character_of_the_account_is_paired_not_filed_as_mail()
    {
        var self = new WalletId(WalletKind.Self, "");
        var sent = new GilEvent(T0.AddMinutes(-2), self, -1_000_000,
            GilEventCategory.MailSend, "rule=MailRule; addons=[LetterEditor]") { ContentId = 111 };
        var ctx = new GameContext(new HashSet<string> { "LetterList", "LetterViewer" },
            Array.Empty<WalletDiff>(), T0, null, 222, new[] { sent });

        var ev = FullChain().Classify(new WalletDiff(self, 0, 1_000_000, T0), ctx);

        Assert.Equal(GilEventCategory.CharacterTransfer, ev.Category);
    }

    private static GameContext Seen(params (string Addon, double SecondsAgo)[] addons)
    {
        var lastSeen = new Dictionary<string, DateTimeOffset>();
        foreach (var (addon, ago) in addons)
            lastSeen[addon] = T0.AddSeconds(-ago);
        return new GameContext(new HashSet<string>(lastSeen.Keys), Array.Empty<WalletDiff>(), T0,
            addonLastSeen: lastSeen);
    }

    [Fact]
    public void Gil_mailed_while_the_teleport_menu_is_still_in_the_window_is_a_mail_send()
    {
        var spend = new WalletDiff(new WalletId(WalletKind.Self, ""), 1_000_000, 0, T0);
        var ctx = Seen(("Teleport", 8), ("LetterList", 0), ("LetterEditor", 0));

        Assert.Equal(GilEventCategory.MailSend, FullChain().Classify(spend, ctx).Category);
    }

    [Fact]
    public void A_teleport_fee_right_after_writing_a_letter_is_still_a_teleport()
    {
        var fee = new WalletDiff(new WalletId(WalletKind.Self, ""), 1_000_000, 999_700, T0);
        var ctx = Seen(("LetterList", 9), ("LetterEditor", 9), ("Teleport", 6));

        Assert.Equal(GilEventCategory.Teleport, FullChain().Classify(fee, ctx).Category);
    }

    [Theory]
    [InlineData("Shop", false, GilEventCategory.NpcShopBuy)]
    [InlineData("ItemSearch", false, GilEventCategory.MarketBoardBuy)]
    [InlineData("Repair", false, GilEventCategory.Repair)]
    [InlineData("Shop", true, GilEventCategory.NpcShopSell)]
    [InlineData("RetainerSellList", true, GilEventCategory.NpcShopSell)]
    [InlineData("RetainerList", true, GilEventCategory.RetainerSale)]
    public void A_letter_seen_later_than_another_rules_window_is_mail_and_not_the_other_way_round(
        string addon, bool gain, GilEventCategory otherwise)
    {
        var self = new WalletId(WalletKind.Self, "");
        var diff = gain ? new WalletDiff(self, 0, 5_000, T0) : new WalletDiff(self, 5_000, 0, T0);
        var letter = gain ? "LetterViewer" : "LetterEditor";
        var mail = gain ? GilEventCategory.MailReceive : GilEventCategory.MailSend;

        Assert.Equal(mail, FullChain().Classify(diff, Seen((addon, 8), (letter, 0))).Category);
        Assert.Equal(otherwise, FullChain().Classify(diff, Seen((letter, 8), (addon, 0))).Category);
        Assert.Equal(otherwise, FullChain().Classify(diff, Seen((letter, 3), (addon, 3))).Category);
    }

    [Fact]
    public void Retainer_deposit_is_caught_by_PairedTransferRule_not_RetainerSaleRule()
    {
        // If RuleChainTests fails here, rule order is wrong.
        var inferrer = FullChain();
        var selfId = new WalletId(WalletKind.Self, "");
        var retId  = new WalletId(WalletKind.Retainer, "Yui");
        var ctx = new GameContext(
            new HashSet<string>(),
            new[] { new WalletDiff(selfId, 500_000, 0, T0) },
            T0);
        var diff = new WalletDiff(retId, 0, 500_000, T0);

        var ev = inferrer.Classify(diff, ctx);

        Assert.Equal(GilEventCategory.RetainerDeposit, ev.Category);
    }

    [Fact]
    public void An_unexplained_FC_chest_gain_is_a_deposit_not_a_submarine_return()
    {
        var inferrer = FullChain();
        var diff = new WalletDiff(
            new WalletId(WalletKind.FreeCompanyChest, "yagi2 FC"),
            18_000_000, 18_450_000, T0);

        var ev = inferrer.Classify(diff, GameContext.Empty(T0));

        Assert.Equal(GilEventCategory.FreeCompanyChestDeposit, ev.Category);
    }

    [Fact]
    public void Submarine_return_classification_end_to_end_via_a_hint()
    {
        var store = new GilHintStore();
        store.Submit("SubmarineReturn", "AutoRetainer:Rhapsody", 60_000);

        var inferrer = FullChain();
        var diff = new WalletDiff(
            new WalletId(WalletKind.FreeCompanyChest, "yagi2 FC"),
            18_000_000, 18_450_000, T0);
        var ctx = new GameContext(new HashSet<string>(), Array.Empty<WalletDiff>(), T0, store);

        Assert.Equal(GilEventCategory.SubmarineReturn, inferrer.Classify(diff, ctx).Category);
    }

    [Fact]
    public void Teleport_fee_classification_end_to_end()
    {
        var inferrer = FullChain();
        var diff = new WalletDiff(new WalletId(WalletKind.Self, ""), 1_000_000, 999_975, T0);
        var ctx = new GameContext(new HashSet<string> { "Teleport" }, Array.Empty<WalletDiff>(), T0);

        Assert.Equal(GilEventCategory.Teleport, inferrer.Classify(diff, ctx).Category);
    }

    [Fact]
    public void Standalone_retainer_increase_classifies_as_RetainerSale()
    {
        var inferrer = FullChain();
        var diff = new WalletDiff(
            new WalletId(WalletKind.Retainer, "Aki"), 0, 412_800, T0);

        var ev = inferrer.Classify(diff, GameContext.Empty(T0));

        Assert.Equal(GilEventCategory.RetainerSale, ev.Category);
    }

    [Fact]
    public void A_retainer_selling_to_the_vendor_is_an_NPC_sale_not_a_retainer_sale()
    {
        var inferrer = FullChain();
        var diff = new WalletDiff(new WalletId(WalletKind.Self, ""), 1_000, 1_045, T0);
        var ctx = new GameContext(new HashSet<string> { "RetainerSellList", "RetainerSell" },
            Array.Empty<WalletDiff>(), T0);

        Assert.Equal(GilEventCategory.NpcShopSell, inferrer.Classify(diff, ctx).Category);
    }

    [Fact]
    public void Self_increase_with_the_sell_list_and_the_bell_menu_stays_a_retainer_sale()
    {
        // Could be a withdrawal, so RetainerSaleRule keeps it.
        var inferrer = FullChain();
        var diff = new WalletDiff(new WalletId(WalletKind.Self, ""), 0, 172_660, T0);
        var ctx = new GameContext(new HashSet<string> { "RetainerList", "RetainerSellList" },
            Array.Empty<WalletDiff>(), T0);

        Assert.Equal(GilEventCategory.RetainerSale, inferrer.Classify(diff, ctx).Category);
    }

    [Fact]
    public void Retainer_wallet_increase_with_the_sell_list_open_stays_a_retainer_sale()
    {
        var inferrer = FullChain();
        var diff = new WalletDiff(new WalletId(WalletKind.Retainer, "Aki"), 0, 412_800, T0);
        var ctx = new GameContext(new HashSet<string> { "RetainerSellList" },
            Array.Empty<WalletDiff>(), T0);

        Assert.Equal(GilEventCategory.RetainerSale, inferrer.Classify(diff, ctx).Category);
    }

    [Fact]
    public void Self_decrease_in_NPC_shop_classifies_as_NpcShopBuy()
    {
        var inferrer = FullChain();
        var diff = new WalletDiff(
            new WalletId(WalletKind.Self, ""), 100_000, 50_000, T0);
        var ctx = new GameContext(new HashSet<string> { "Shop" }, Array.Empty<WalletDiff>(), T0);

        var ev = inferrer.Classify(diff, ctx);

        Assert.Equal(GilEventCategory.NpcShopBuy, ev.Category);
    }

    [Fact]
    public void Random_self_change_with_no_addons_falls_through_to_Misc()
    {
        var inferrer = FullChain();
        var diff = new WalletDiff(
            new WalletId(WalletKind.Self, ""), 0, 1_234, T0);

        var ev = inferrer.Classify(diff, GameContext.Empty(T0));

        Assert.Equal(GilEventCategory.Misc, ev.Category);
    }

    [Fact]
    public void Mail_gil_is_classified_by_the_letter_that_was_open()
    {
        var inferrer = FullChain();
        var self = new WalletId(WalletKind.Self, "");

        Assert.Equal(GilEventCategory.MailReceive, inferrer.Classify(
            new WalletDiff(self, 0, 1_000_000, T0),
            new GameContext(new HashSet<string> { "LetterList", "LetterViewer" },
                Array.Empty<WalletDiff>(), T0)).Category);

        Assert.Equal(GilEventCategory.MailSend, inferrer.Classify(
            new WalletDiff(self, 1_000_000, 0, T0),
            new GameContext(new HashSet<string> { "LetterList", "LetterEditor" },
                Array.Empty<WalletDiff>(), T0)).Category);
    }
}
