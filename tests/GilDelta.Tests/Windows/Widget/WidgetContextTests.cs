using System.Collections.Generic;
using System.Linq;
using GilDelta.Events;
using GilDelta.Theme;
using GilDelta.Wallet;
using GilDelta.Windows.Widget;

namespace GilDelta.Tests.Windows.Widget;

public class WidgetContextTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-05T18:00:00+09:00");

    [Fact]
    public void Total_sums_all_wallets()
    {
        var ctx = new WidgetContext(
            wallets: new[]
            {
                new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Self, ""), 5_000_000),
                new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Retainer, "A"), 2_000_000),
                new GilDelta.Wallet.Wallet(new WalletId(WalletKind.FreeCompanyChest, "FC"), 18_000_000),
            },
            recentEvents: Array.Empty<GilEvent>(),
            now: Now,
            theme: MidnightCoin.Instance);

        Assert.Equal(25_000_000, ctx.Total);
    }

    [Fact]
    public void TodayDelta_sums_today_events_only()
    {
        var ctx = new WidgetContext(
            wallets: Array.Empty<GilDelta.Wallet.Wallet>(),
            recentEvents: new[]
            {
                new GilEvent(Now,                  new WalletId(WalletKind.Self, ""), +500, GilEventCategory.Misc, null),
                new GilEvent(Now.AddHours(-1),     new WalletId(WalletKind.Self, ""), +200, GilEventCategory.Misc, null),
                new GilEvent(Now.AddDays(-2),      new WalletId(WalletKind.Self, ""), +999, GilEventCategory.Misc, null),
            },
            now: Now,
            theme: MidnightCoin.Instance);

        Assert.Equal(700, ctx.TodayDelta);
    }

    [Fact]
    public void Breakdown_groups_by_wallet_kind()
    {
        var ctx = new WidgetContext(
            wallets: new[]
            {
                new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Self, ""), 5_000_000),
                new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Retainer, "A"), 2_000_000),
                new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Retainer, "B"), 4_000_000),
                new GilDelta.Wallet.Wallet(new WalletId(WalletKind.FreeCompanyChest, "FC"), 18_000_000),
            },
            recentEvents: Array.Empty<GilEvent>(),
            now: Now,
            theme: MidnightCoin.Instance);

        Assert.Equal(5_000_000, ctx.SelfTotal);
        Assert.Equal(6_000_000, ctx.RetainerTotal);
        Assert.Equal(18_000_000, ctx.FreeCompanyTotal);
    }

    // ─── Wallets that could not be read ────────────────────────────────────────

    private static WidgetContext Build(params GilDelta.Wallet.Wallet[] wallets) =>
        new(wallets, Array.Empty<GilEvent>(), Now, MidnightCoin.Instance);

    [Fact]
    public void FreeCompanyTotal_is_null_when_the_chest_could_not_be_read()
    {
        var ctx = Build(
            new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Self, ""), 5_000_000),
            new GilDelta.Wallet.Wallet(new WalletId(WalletKind.FreeCompanyChest, "FC"), null));

        // 🔴 Null, not 0. A renderer printing 0 here is claiming the chest is empty.
        Assert.Null(ctx.FreeCompanyTotal);
        Assert.True(ctx.HasUnknownWallet);

        // The total is then a lower bound: the readable wallets only.
        Assert.Equal(5_000_000, ctx.Total);
    }

    [Fact]
    public void FreeCompanyTotal_is_zero_when_the_chest_was_read_and_is_empty()
    {
        var ctx = Build(
            new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Self, ""), 5_000_000),
            new GilDelta.Wallet.Wallet(new WalletId(WalletKind.FreeCompanyChest, "FC"), 0));

        Assert.Equal(0, ctx.FreeCompanyTotal);
        Assert.False(ctx.HasUnknownWallet);
    }

    [Fact]
    public void FreeCompanyTotal_is_zero_when_there_is_no_company_at_all()
    {
        var ctx = Build(new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Self, ""), 5_000_000));

        Assert.Equal(0, ctx.FreeCompanyTotal);
        Assert.False(ctx.HasUnknownWallet);
    }

    [Fact]
    public void One_unreadable_company_makes_the_whole_chest_figure_unknown()
    {
        // Summing only the companies we can see would print a number that is nobody's
        // actual balance.
        var ctx = Build(
            new GilDelta.Wallet.Wallet(new WalletId(WalletKind.FreeCompanyChest, "A"), 1_000),
            new GilDelta.Wallet.Wallet(new WalletId(WalletKind.FreeCompanyChest, "B"), null));

        Assert.Null(ctx.FreeCompanyTotal);
    }

    [Fact]
    public void An_unreadable_retainer_does_not_disturb_the_other_subtotals()
    {
        var ctx = Build(
            new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Self, ""), 5_000_000),
            new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Retainer, "A"), 2_000_000),
            new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Retainer, "B"), null));

        Assert.Equal(5_000_000, ctx.SelfTotal);
        Assert.Equal(2_000_000, ctx.RetainerTotal);
        Assert.Equal(0, ctx.FreeCompanyTotal);
        Assert.True(ctx.HasUnknownWallet);
    }

    // ─── Wallets with their provenance attached ────────────────────────────────

    [Fact]
    public void The_two_constructors_produce_the_same_figures()
    {
        // The wallet-list constructor is a wrapper around the origin one, so a caller
        // with no provenance to offer cannot end up with different arithmetic from one
        // that has.
        var wallets = new[]
        {
            new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Self, ""), 5_000_000),
            new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Retainer, "A"), 2_000_000),
            new GilDelta.Wallet.Wallet(new WalletId(WalletKind.FreeCompanyChest, "FC"), null),
        };

        var plain = new WidgetContext(wallets, Array.Empty<GilEvent>(), Now, MidnightCoin.Instance);
        var attributed = new WidgetContext(
            wallets.Select(w => new WalletOrigin(w, 1001, Now)).ToArray(),
            Array.Empty<GilEvent>(), Now, MidnightCoin.Instance);

        Assert.Equal(plain.Total, attributed.Total);
        Assert.Equal(plain.SelfTotal, attributed.SelfTotal);
        Assert.Equal(plain.RetainerTotal, attributed.RetainerTotal);
        Assert.Equal(plain.FreeCompanyTotal, attributed.FreeCompanyTotal);
        Assert.Equal(plain.HasUnknownWallet, attributed.HasUnknownWallet);
    }

    [Fact]
    public void Wallets_with_no_provenance_claim_neither_an_owner_nor_a_time()
    {
        // 🔴 Not "now", and not the character being played: a made-up timestamp would be
        // shown to the user as fact.
        var ctx = Build(new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Self, ""), 1));

        var origin = Assert.Single(ctx.Origins);
        Assert.Equal(0ul, origin.ContentId);
        Assert.Null(origin.At);
        Assert.False(origin.IsLive);
    }

    [Fact]
    public void LabelFor_never_renders_a_bare_content_id_as_if_it_were_a_name()
    {
        var ctx = new WidgetContext(
            Array.Empty<WalletOrigin>(), Array.Empty<GilEvent>(), Now, MidnightCoin.Instance)
        {
            CharacterLabels = new Dictionary<ulong, string> { [1001] = "Ayame@Ravana" },
        };

        Assert.Equal("Ayame@Ravana", ctx.LabelFor(1001));
        Assert.Equal("#2002", ctx.LabelFor(2002));
    }
}
