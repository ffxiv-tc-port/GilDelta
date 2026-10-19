using System.Collections.Generic;
using GilDelta.Events;
using GilDelta.Theme;
using GilDelta.Wallet;
using GilDelta.Windows.Dashboard;

namespace GilDelta.Tests.Windows.Dashboard;

public class DashboardContextTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-06T18:00:00+09:00");

    private static GilEvent E(DateTimeOffset at, long amount, GilEventCategory cat = GilEventCategory.Misc) =>
        new(at, new WalletId(WalletKind.Self, ""), amount, cat, null);

    [Fact]
    public void Total_sums_all_wallets()
    {
        var ctx = new DashboardContext(
            wallets: new[]
            {
                new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Self, ""), 5_000_000),
                new GilDelta.Wallet.Wallet(new WalletId(WalletKind.FreeCompanyChest, "FC"), 18_000_000),
            },
            events: Array.Empty<GilEvent>(),
            now: Now,
            theme: MidnightCoin.Instance);

        Assert.Equal(23_000_000, ctx.Total);
    }

    [Fact]
    public void DailyNet_buckets_events_by_local_day()
    {
        var ctx = new DashboardContext(
            wallets: Array.Empty<GilDelta.Wallet.Wallet>(),
            events: new[]
            {
                E(Now,                  100),
                E(Now.AddHours(-2),     200),
                E(Now.AddDays(-1),      500),
                E(Now.AddDays(-1).AddMinutes(30), -50),
            },
            now: Now,
            theme: MidnightCoin.Instance);

        var today      = ctx.DailyNet(Now);
        var yesterday  = ctx.DailyNet(Now.AddDays(-1));

        Assert.Equal(300, today);       // 100 + 200
        Assert.Equal(450, yesterday);   // 500 + -50
    }

    [Fact]
    public void NetByCategory_groups_events_by_GilEventCategory()
    {
        var ctx = new DashboardContext(
            wallets: Array.Empty<GilDelta.Wallet.Wallet>(),
            events: new[]
            {
                E(Now, +500_000, GilEventCategory.SubmarineReturn),
                E(Now, +400_000, GilEventCategory.SubmarineReturn),
                E(Now, +200_000, GilEventCategory.RetainerSale),
                E(Now, -100_000, GilEventCategory.NpcShopBuy),
                E(Now,  +50_000, GilEventCategory.Misc),
            },
            now: Now,
            theme: MidnightCoin.Instance);

        var grouped = ctx.NetByCategory(Now.AddDays(-7), Now);
        Assert.Equal(900_000, grouped[GilEventCategory.SubmarineReturn]);
        Assert.Equal(200_000, grouped[GilEventCategory.RetainerSale]);
        Assert.Equal(-100_000, grouped[GilEventCategory.NpcShopBuy]);
        Assert.Equal(50_000, grouped[GilEventCategory.Misc]);
    }

    /// <summary>
    /// 🔴 The property the balance tab rests on: the rows it draws ARE the list the
    /// total was summed over. Total is computed over Wallets, Wallets is derived from
    /// Origins, and the tab renders Origins - so a breakdown that disagrees with the
    /// number above it is unrepresentable rather than merely untested.
    /// </summary>
    [Fact]
    public void Total_is_summed_over_the_very_origins_the_balance_tab_draws()
    {
        var origins = new[]
        {
            new WalletOrigin(new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Self, ""), 5_000_000), 1001, Now),
            new WalletOrigin(new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Retainer, "Bob"), 750_000), 1001, Now),
            new WalletOrigin(new GilDelta.Wallet.Wallet(new WalletId(WalletKind.FreeCompanyChest, "FC"), 18_000_000), 1002, Now),
        };

        var ctx = new DashboardContext(origins, Array.Empty<GilEvent>(), Now, MidnightCoin.Instance);

        Assert.Equal(23_750_000, ctx.Total);
        Assert.Equal(ctx.Total, BalanceRows.Total(BalanceRows.Build(ctx.Origins)));
        Assert.Equal(origins.Length, ctx.Wallets.Count);
        Assert.False(ctx.HasUnknownWallet);
    }

    /// <summary>
    /// An unreadable wallet is a "?" row, never a 0 one, and it leaves the total a lower
    /// bound.
    /// </summary>
    [Fact]
    public void An_unreadable_wallet_is_unknown_rather_than_zero()
    {
        var origins = new[]
        {
            new WalletOrigin(new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Self, ""), 1_000), 1001, Now),
            new WalletOrigin(new GilDelta.Wallet.Wallet(new WalletId(WalletKind.FreeCompanyChest, "FC"), null), 1001, null),
        };

        var ctx = new DashboardContext(origins, Array.Empty<GilEvent>(), Now, MidnightCoin.Instance);
        var rows = BalanceRows.Build(ctx.Origins);

        Assert.True(ctx.HasUnknownWallet);
        Assert.Equal(1_000, ctx.Total);
        Assert.Equal(ctx.Total, BalanceRows.Total(rows));
        Assert.Equal(1, BalanceRows.UnknownCount(rows));
    }

    /// <summary>
    /// The wallet-only constructor still works and wraps its rows as unattributed
    /// origins, so the tab prints "?" for owner and reading time rather than inventing
    /// a character or a timestamp.
    /// </summary>
    [Fact]
    public void Wallet_only_construction_yields_unattributed_origins()
    {
        var ctx = new DashboardContext(
            wallets: new[]
            {
                new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Self, ""), 42),
            },
            events: Array.Empty<GilEvent>(),
            now: Now,
            theme: MidnightCoin.Instance);

        var row = Assert.Single(ctx.Origins);
        Assert.Equal(0ul, row.ContentId);
        Assert.Null(row.At);
        Assert.False(row.IsLive);
        Assert.Equal(42, ctx.Total);
    }
}
