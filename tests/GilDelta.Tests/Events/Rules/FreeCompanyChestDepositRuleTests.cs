using System.Collections.Generic;
using GilDelta.Events;
using GilDelta.Events.Rules;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events.Rules;

public class FreeCompanyChestDepositRuleTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public void FC_chest_increase_classifies_as_FreeCompanyChestDeposit()
    {
        var rule = new FreeCompanyChestDepositRule();
        var diff = new WalletDiff(
            new WalletId(WalletKind.FreeCompanyChest, "yagi2 FC"),
            Before: 18_000_000, After: 18_450_000, At: T0);

        var ok = rule.TryClassify(diff, GameContext.Empty(T0), out var ev);

        Assert.True(ok);
        Assert.Equal(GilEventCategory.FreeCompanyChestDeposit, ev!.Category);
        Assert.Equal(450_000, ev.Amount);
    }

    [Fact]
    public void An_unattributed_increase_is_never_called_a_submarine_return()
    {
        // Calling every increase a submarine payout would record an FC member simply
        // paying gil in as a submarine return.
        var rule = new FreeCompanyChestDepositRule();
        var diff = new WalletDiff(
            new WalletId(WalletKind.FreeCompanyChest, "yagi2 FC"), 0, 1_000_000, T0);

        rule.TryClassify(diff, GameContext.Empty(T0), out var ev);

        Assert.NotEqual(GilEventCategory.SubmarineReturn, ev!.Category);
    }

    [Fact]
    public void A_submarine_hint_still_produces_a_submarine_return()
    {
        var store = new GilHintStore();
        store.Submit("SubmarineReturn", "AutoRetainer:Rhapsody", 60_000);

        var inferrer = new Inferrer(new IInferenceRule[]
        {
            new HintRule(),
            new FreeCompanyChestDepositRule(),
            new MiscRule(),
        });
        var ctx = new GameContext(new HashSet<string>(), Array.Empty<WalletDiff>(), T0, store);
        var diff = new WalletDiff(
            new WalletId(WalletKind.FreeCompanyChest, "yagi2 FC"), 18_000_000, 18_450_000, T0);

        Assert.Equal(GilEventCategory.SubmarineReturn, inferrer.Classify(diff, ctx).Category);
    }

    [Fact]
    public void FC_chest_decrease_does_not_match()
    {
        var rule = new FreeCompanyChestDepositRule();
        var diff = new WalletDiff(
            new WalletId(WalletKind.FreeCompanyChest, "yagi2 FC"),
            Before: 18_000_000, After: 17_500_000, At: T0);

        Assert.False(rule.TryClassify(diff, GameContext.Empty(T0), out _));
    }

    [Fact]
    public void Self_increase_does_not_match()
    {
        var rule = new FreeCompanyChestDepositRule();
        var diff = new WalletDiff(
            new WalletId(WalletKind.Self, ""), 1_000, 2_000, T0);

        Assert.False(rule.TryClassify(diff, GameContext.Empty(T0), out _));
    }

    [Fact]
    public void Retainer_increase_does_not_match()
    {
        var rule = new FreeCompanyChestDepositRule();
        var diff = new WalletDiff(
            new WalletId(WalletKind.Retainer, "Yui"), 100, 1_000, T0);

        Assert.False(rule.TryClassify(diff, GameContext.Empty(T0), out _));
    }
}
