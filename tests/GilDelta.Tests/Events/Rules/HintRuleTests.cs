using System.Collections.Generic;
using GilDelta.Events;
using GilDelta.Events.Rules;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events.Rules;

public class HintRuleTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static WalletDiff TeleportFee() =>
        new(new WalletId(WalletKind.Self, ""), 1_000_000, 999_975, T0);

    private static GameContext WithHints(IHintSource hints) =>
        new(new HashSet<string>(), Array.Empty<WalletDiff>(), T0, hints);

    [Fact]
    public void A_matching_hint_classifies_the_change_and_records_its_note()
    {
        var store = new GilHintStore();
        store.Submit("Teleport", "Lifestream:利姆薩·羅敏薩", 10_000);

        var ok = new HintRule().TryClassify(TeleportFee(), WithHints(store), out var ev);

        Assert.True(ok);
        Assert.Equal(GilEventCategory.Teleport, ev!.Category);
        Assert.Equal(-25, ev.Amount);
        Assert.Equal("rule=HintRule; hint=Lifestream:利姆薩·羅敏薩", ev.Note);
    }

    [Fact]
    public void An_expired_hint_does_not_classify()
    {
        var now = T0;
        var store = new GilHintStore(() => now);
        store.Submit("Teleport", "Lifestream:x", 1_000);
        now = T0.AddSeconds(5);

        Assert.False(new HintRule().TryClassify(TeleportFee(), WithHints(store), out _));
    }

    [Fact]
    public void An_empty_note_falls_back_to_the_category_name()
    {
        var store = new GilHintStore();
        store.Submit("Repair", "", 10_000);

        Assert.True(new HintRule().TryClassify(TeleportFee(), WithHints(store), out var ev));
        Assert.Equal("rule=HintRule; hint=Repair", ev!.Note);
    }

    [Fact]
    public void A_context_with_no_hint_source_never_matches()
    {
        // This is the Reclassify path: replaying history must not eat live hints.
        Assert.False(new HintRule().TryClassify(TeleportFee(), GameContext.Empty(T0), out _));
    }

    [Fact]
    public void A_hint_beats_the_addon_rules_but_not_a_paired_transfer()
    {
        var store = new GilHintStore();
        store.Submit("Teleport", "Lifestream:x", 10_000);

        var inferrer = new Inferrer(new IInferenceRule[]
        {
            new PairedTransferRule(),
            new HintRule(),
            new NpcShopBuyRule(),
            new MiscRule(),
        });

        // The Shop addon was open too; the hint still wins.
        var ctx = new GameContext(
            new HashSet<string> { "Shop" }, Array.Empty<WalletDiff>(), T0, store);

        Assert.Equal(GilEventCategory.Teleport, inferrer.Classify(TeleportFee(), ctx).Category);
    }

    [Fact]
    public void A_paired_transfer_is_not_stolen_by_a_stale_hint()
    {
        var store = new GilHintStore();
        store.Submit("RetainerSale", "AutoRetainer:x", 60_000);
        store.Drain();  // so ActiveCount below reports the shelf, not the inbox

        var inferrer = new Inferrer(new IInferenceRule[]
        {
            new PairedTransferRule(),
            new HintRule(),
            new MiscRule(),
        });

        var selfId = new WalletId(WalletKind.Self, "");
        var retId  = new WalletId(WalletKind.Retainer, "Yui");
        var ctx = new GameContext(
            new HashSet<string>(),
            new[] { new WalletDiff(selfId, 500_000, 0, T0) },
            T0,
            store);

        var ev = inferrer.Classify(new WalletDiff(retId, 0, 500_000, T0), ctx);

        Assert.Equal(GilEventCategory.RetainerDeposit, ev.Category);
        // and the hint is still on the shelf for the change it was actually about
        Assert.Equal(1, store.ActiveCount);
    }
}
