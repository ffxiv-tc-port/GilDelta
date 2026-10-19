using System.Collections.Generic;
using GilDelta.Events;
using GilDelta.Events.Rules;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events.Rules;

public class RetainerSaleRuleTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static GameContext With(params string[] addons) =>
        new(new HashSet<string>(addons), Array.Empty<WalletDiff>(), T0);

    [Fact]
    public void Retainer_increase_classifies_as_RetainerSale()
    {
        var rule = new RetainerSaleRule();
        var diff = new WalletDiff(
            new WalletId(WalletKind.Retainer, "Yui"), 0, 412_800, T0);

        var ok = rule.TryClassify(diff, GameContext.Empty(T0), out var ev);

        Assert.True(ok);
        Assert.Equal(GilEventCategory.RetainerSale, ev!.Category);
        Assert.Equal(412_800, ev.Amount);
        Assert.Contains("Yui", ev.Note);
    }

    [Fact]
    public void Retainer_decrease_does_not_match()
    {
        var rule = new RetainerSaleRule();
        var diff = new WalletDiff(
            new WalletId(WalletKind.Retainer, "Yui"), 500, 0, T0);

        Assert.False(rule.TryClassify(diff, GameContext.Empty(T0), out _));
    }

    [Fact]
    public void Self_increase_with_no_retainer_window_does_not_match()
    {
        var rule = new RetainerSaleRule();
        var diff = new WalletDiff(
            new WalletId(WalletKind.Self, ""), 0, 1_000, T0);

        Assert.False(rule.TryClassify(diff, GameContext.Empty(T0), out _));
    }

    [Theory]
    [InlineData("RetainerSellList")]
    [InlineData("RetainerSell")]
    [InlineData("RetainerList")]
    [InlineData("RetainerTaskResult")]
    public void Self_increase_near_a_retainer_window_classifies_as_RetainerSale(string addon)
    {
        // The retainer wallets are frequently unreadable at the moment automation
        // moves the money, so this Self-side diff arrives with no counterparty and
        // PairedTransferRule cannot fire.
        var rule = new RetainerSaleRule();
        var diff = new WalletDiff(new WalletId(WalletKind.Self, ""), 0, 412_800, T0);

        var ok = rule.TryClassify(diff, With(addon), out var ev);

        Assert.True(ok);
        Assert.Equal(GilEventCategory.RetainerSale, ev!.Category);
        Assert.Contains(addon, ev.Note);
    }

    [Fact]
    public void Self_increase_near_an_unrelated_window_does_not_match()
    {
        var rule = new RetainerSaleRule();
        var diff = new WalletDiff(new WalletId(WalletKind.Self, ""), 0, 412_800, T0);

        Assert.False(rule.TryClassify(diff, With("Repair", "Teleport"), out _));
    }

    [Fact]
    public void Self_decrease_never_matches_however_many_retainer_windows_are_open()
    {
        var rule = new RetainerSaleRule();
        var diff = new WalletDiff(new WalletId(WalletKind.Self, ""), 412_800, 0, T0);

        Assert.False(rule.TryClassify(diff, With("RetainerSellList", "RetainerList"), out _));
    }

    [Fact]
    public void An_NPC_sale_at_a_retainer_bell_stays_an_NPC_sale()
    {
        // Rule order is the guard: the Self half of this rule is the weaker of the
        // two inferences and has to lose to a shop that was actually open.
        var inferrer = new Inferrer(new IInferenceRule[]
        {
            new NpcShopSellRule(),
            new RetainerSaleRule(),
            new MiscRule(),
        });
        var diff = new WalletDiff(new WalletId(WalletKind.Self, ""), 0, 5_000, T0);

        var ev = inferrer.Classify(diff, With("Shop", "RetainerList"));

        Assert.Equal(GilEventCategory.NpcShopSell, ev.Category);
    }

    [Fact]
    public void FC_chest_increase_does_not_match()
    {
        var rule = new RetainerSaleRule();
        var diff = new WalletDiff(
            new WalletId(WalletKind.FreeCompanyChest, "yagi2 FC"), 0, 100_000, T0);

        Assert.False(rule.TryClassify(diff, GameContext.Empty(T0), out _));
    }

    [Fact]
    public void FC_chest_increase_does_not_match_even_beside_a_retainer_window()
    {
        var rule = new RetainerSaleRule();
        var diff = new WalletDiff(
            new WalletId(WalletKind.FreeCompanyChest, "yagi2 FC"), 0, 100_000, T0);

        Assert.False(rule.TryClassify(diff, With("RetainerList"), out _));
    }
}
