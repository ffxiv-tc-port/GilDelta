using System.Collections.Generic;
using GilDelta.Events;
using GilDelta.Events.Rules;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events.Rules;

public class NpcShopSellRuleTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static GameContext Ctx(params string[] addons) =>
        new(new HashSet<string>(addons), Array.Empty<WalletDiff>(), T0);

    [Fact]
    public void Self_increase_with_Shop_addon_classifies_as_NpcShopSell()
    {
        var rule = new NpcShopSellRule();
        var diff = new WalletDiff(
            new WalletId(WalletKind.Self, ""), 1_000, 5_000, T0);

        var ok = rule.TryClassify(diff, Ctx("Shop"), out var ev);

        Assert.True(ok);
        Assert.Equal(GilEventCategory.NpcShopSell, ev!.Category);
        Assert.Equal(4_000, ev.Amount);
    }

    [Fact]
    public void Self_decrease_does_not_match()
    {
        var rule = new NpcShopSellRule();
        var diff = new WalletDiff(
            new WalletId(WalletKind.Self, ""), 5_000, 1_000, T0);

        Assert.False(rule.TryClassify(diff, Ctx("Shop"), out _));
    }

    [Fact]
    public void No_Shop_addon_does_not_match()
    {
        var rule = new NpcShopSellRule();
        var diff = new WalletDiff(
            new WalletId(WalletKind.Self, ""), 1_000, 5_000, T0);

        Assert.False(rule.TryClassify(diff, Ctx(), out _));
    }

    [Theory]
    [InlineData("RetainerSellList")]
    [InlineData("RetainerSell")]
    public void Self_increase_with_only_the_retainer_sell_list_is_a_vendor_sale(string addon)
    {
        // A retainer selling to the vendor pays the player; market proceeds would have
        // arrived on the retainer's own wallet instead.
        var rule = new NpcShopSellRule();
        var diff = new WalletDiff(new WalletId(WalletKind.Self, ""), 1_000, 1_045, T0);

        var ok = rule.TryClassify(diff, Ctx(addon), out var ev);

        Assert.True(ok);
        Assert.Equal(GilEventCategory.NpcShopSell, ev!.Category);
        Assert.Equal(45, ev.Amount);
        Assert.Contains("via=retainer", ev.Note);
        Assert.StartsWith("rule=NpcShopSellRule;", ev.Note);
    }

    [Theory]
    [InlineData("RetainerSellList", "RetainerList")]
    [InlineData("RetainerSell", "RetainerTaskResult")]
    public void Retainer_sell_list_beside_the_bell_menu_is_not_claimed(string sellAddon, string menuAddon)
    {
        // The bell menu covers withdrawals too; that case stays with RetainerSaleRule.
        var rule = new NpcShopSellRule();
        var diff = new WalletDiff(new WalletId(WalletKind.Self, ""), 0, 172_660, T0);

        Assert.False(rule.TryClassify(diff, Ctx(sellAddon, menuAddon), out _));
    }

    [Fact]
    public void A_shop_window_wins_even_beside_the_bell_menu()
    {
        var rule = new NpcShopSellRule();
        var diff = new WalletDiff(new WalletId(WalletKind.Self, ""), 0, 5_000, T0);

        var ok = rule.TryClassify(diff, Ctx("Shop", "RetainerList", "RetainerSellList"), out var ev);

        Assert.True(ok);
        Assert.DoesNotContain("via=retainer", ev!.Note);
    }

    [Fact]
    public void Retainer_wallet_increase_with_the_sell_list_open_does_not_match()
    {
        // Market proceeds: the retainer's own wallet. RetainerSaleRule owns those.
        var rule = new NpcShopSellRule();
        var diff = new WalletDiff(new WalletId(WalletKind.Retainer, "Yui"), 0, 412_800, T0);

        Assert.False(rule.TryClassify(diff, Ctx("RetainerSellList"), out _));
    }

    [Fact]
    public void Self_decrease_with_the_retainer_sell_list_open_does_not_match()
    {
        var rule = new NpcShopSellRule();
        var diff = new WalletDiff(new WalletId(WalletKind.Self, ""), 5_000, 4_000, T0);

        Assert.False(rule.TryClassify(diff, Ctx("RetainerSellList", "RetainerSell"), out _));
    }
}
