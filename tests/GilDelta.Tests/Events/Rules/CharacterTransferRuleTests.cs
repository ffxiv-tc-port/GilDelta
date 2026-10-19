using System.Collections.Generic;
using GilDelta.Events;
using GilDelta.Events.Rules;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events.Rules;

public class CharacterTransferRuleTests
{
    private const ulong Alt  = 111;
    private const ulong Main = 222;

    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-09-16T21:26:00+08:00");
    private static readonly WalletId Self = new(WalletKind.Self, "");

    private static CharacterTransferRule Rule(
        Action<GilEvent, ulong>? onMatch = null, int windowMinutes = 30) =>
        new(() => TimeSpan.FromMinutes(windowMinutes), onMatch);

    private static GameContext Ctx(ulong owner, params GilEvent[] recent) =>
        new(new HashSet<string>(), Array.Empty<WalletDiff>(), T0, null, owner, recent);

    private static GilEvent Sent(long amount, DateTimeOffset at, ulong cid = Alt) =>
        new(at, Self, amount, GilEventCategory.Misc, "rule=MiscRule; addons=[]") { ContentId = cid };

    private static WalletDiff Received(long amount, DateTimeOffset at) =>
        new(Self, 0, amount, at);

    [Fact]
    public void Pairs_an_equal_opposite_change_on_another_character()
    {
        GilEvent? matched = null;
        ulong matchedOwner = 0;
        var rule = Rule((ev, owner) => { matched = ev; matchedOwner = owner; });

        var prior = Sent(-1_000_000, T0.AddMinutes(-7));
        var ok = rule.TryClassify(Received(1_000_000, T0), Ctx(Main, prior), out var ev);

        Assert.True(ok);
        Assert.Equal(GilEventCategory.CharacterTransfer, ev!.Category);
        Assert.Equal(1_000_000, ev.Amount);
        Assert.Contains("cid:111", ev.Note);
        Assert.Equal(prior, matched);
        Assert.Equal(Main, matchedOwner);
    }

    [Fact]
    public void Does_not_pair_the_same_character_with_itself()
    {
        var rule = Rule();
        var prior = Sent(-1_000_000, T0.AddMinutes(-1), cid: Main);

        Assert.False(rule.TryClassify(Received(1_000_000, T0), Ctx(Main, prior), out _));
    }

    [Fact]
    public void Does_not_pair_outside_the_window()
    {
        var rule = Rule(windowMinutes: 5);
        var prior = Sent(-1_000_000, T0.AddMinutes(-7));

        Assert.False(rule.TryClassify(Received(1_000_000, T0), Ctx(Main, prior), out _));
    }

    [Fact]
    public void Does_not_pair_amounts_that_differ()
    {
        var rule = Rule();
        var prior = Sent(-600_000, T0.AddMinutes(-1));

        Assert.False(rule.TryClassify(Received(1_000_000, T0), Ctx(Main, prior), out _));
    }

    [Fact]
    public void Only_takes_a_counterparty_that_is_still_Misc()
    {
        var rule = Rule();
        var claimed = Sent(-1_000_000, T0.AddMinutes(-1)) with
        {
            Category = GilEventCategory.MarketBoardBuy,
        };

        Assert.False(rule.TryClassify(Received(1_000_000, T0), Ctx(Main, claimed), out _));
    }

    [Fact]
    public void Skips_a_counterparty_the_user_excluded()
    {
        var rule = Rule();
        var excluded = Sent(-1_000_000, T0.AddMinutes(-1)) with { Suspect = true };

        Assert.False(rule.TryClassify(Received(1_000_000, T0), Ctx(Main, excluded), out _));
    }

    [Fact]
    public void Ignores_the_unknown_owner_bucket_on_both_sides()
    {
        var rule = Rule();
        var orphan = Sent(-1_000_000, T0.AddMinutes(-1), cid: 0);

        Assert.False(rule.TryClassify(Received(1_000_000, T0), Ctx(Main, orphan), out _));
        Assert.False(rule.TryClassify(
            Received(1_000_000, T0), Ctx(0, Sent(-1_000_000, T0.AddMinutes(-1))), out _));
    }

    [Fact]
    public void Ignores_retainer_and_chest_wallets()
    {
        var rule = Rule();
        var prior = Sent(-1_000_000, T0.AddMinutes(-1));
        var chest = new WalletDiff(new WalletId(WalletKind.FreeCompanyChest, ""), 0, 1_000_000, T0);

        Assert.False(rule.TryClassify(chest, Ctx(Main, prior), out _));

        var retainerPrior = prior with { Wallet = new WalletId(WalletKind.Retainer, "Yui") };
        Assert.False(rule.TryClassify(Received(1_000_000, T0), Ctx(Main, retainerPrior), out _));
    }

    [Fact]
    public void Picks_the_closest_counterparty_in_time()
    {
        GilEvent? matched = null;
        var rule = Rule((ev, _) => matched = ev);

        var far   = Sent(-1_000_000, T0.AddMinutes(-20));
        var near  = Sent(-1_000_000, T0.AddMinutes(-2));

        Assert.True(rule.TryClassify(Received(1_000_000, T0), Ctx(Main, far, near), out _));
        Assert.Equal(near, matched);
    }

    [Fact]
    public void Does_nothing_when_there_are_no_candidates()
    {
        var rule = Rule();
        Assert.False(rule.TryClassify(Received(1_000_000, T0), Ctx(Main), out _));
    }

    [Fact]
    public void Pairs_the_outgoing_half_too_when_it_is_classified_second()
    {
        var rule = Rule();
        var priorIncoming = Sent(1_000_000, T0.AddMinutes(-3), cid: Main);

        var ok = rule.TryClassify(Received(-1_000_000, T0), Ctx(Alt, priorIncoming), out var ev);

        Assert.True(ok);
        Assert.Equal(GilEventCategory.CharacterTransfer, ev!.Category);
        Assert.Equal(-1_000_000, ev.Amount);
    }
    /// <summary>
    /// One character sends several identical lots and
    /// another collects them minutes later. Every receipt has to find its own
    /// counterparty, which is what relabelling a claimed one guarantees.
    /// </summary>
    [Fact]
    public void Identical_lots_are_consumed_one_for_one()
    {
        var pool = new List<GilEvent>
        {
            Sent(-1_000_000, T0.AddMinutes(-6)),
            Sent(-1_000_000, T0.AddMinutes(-5)),
            Sent(-1_000_000, T0.AddMinutes(-4)),
        };

        var rule = Rule((ev, _) =>
        {
            var i = pool.IndexOf(ev);
            pool[i] = ev with { Category = GilEventCategory.CharacterTransfer };
        });

        for (var n = 0; n < 3; n++)
        {
            var ok = rule.TryClassify(
                Received(1_000_000, T0.AddSeconds(n * 6)), Ctx(Main, pool.ToArray()), out var ev);
            Assert.True(ok);
            Assert.Equal(GilEventCategory.CharacterTransfer, ev!.Category);
        }

        Assert.All(pool, e => Assert.Equal(GilEventCategory.CharacterTransfer, e.Category));

        // The fourth receipt has nothing left to pair with and stays unexplained.
        Assert.False(rule.TryClassify(
            Received(1_000_000, T0.AddSeconds(18)), Ctx(Main, pool.ToArray()), out _));
    }

    [Fact]
    public void Pairs_a_half_MailRule_already_labelled()
    {
        GilEvent? matched = null;
        var rule = Rule((ev, _) => matched = ev);
        var prior = new GilEvent(T0.AddMinutes(-2), Self, -1_000_000,
            GilEventCategory.MailSend, "rule=MailRule; addons=[LetterEditor]") { ContentId = Alt };

        var ok = rule.TryClassify(Received(1_000_000, T0), Ctx(Main, prior), out var ev);

        Assert.True(ok);
        Assert.Equal(GilEventCategory.CharacterTransfer, ev!.Category);
        Assert.Equal(prior, matched);
    }

    [Fact]
    public void Does_not_pair_a_half_another_rule_explained()
    {
        var prior = new GilEvent(T0.AddMinutes(-2), Self, -1_000_000,
            GilEventCategory.Repair, "rule=RepairRule; addons=[Repair]") { ContentId = Alt };

        Assert.False(Rule().TryClassify(Received(1_000_000, T0), Ctx(Main, prior), out _));
    }
}
