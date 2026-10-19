using GilDelta.Events;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events;

public class WalletExclusionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-08T18:00:00+08:00");

    private static GilEvent E(WalletKind kind, long amount, GilEventCategory cat) =>
        new(Now, new WalletId(kind, kind == WalletKind.FreeCompanyChest ? "FC" : ""), amount, cat, null);

    [Fact]
    public void Events_are_untouched_when_the_exclusion_is_off()
    {
        var events = new[]
        {
            E(WalletKind.Self, 100, GilEventCategory.Misc),
            E(WalletKind.FreeCompanyChest, 50_000_000, GilEventCategory.FreeCompanyChestDeposit),
        };

        // Same instance, not just an equal one: the default path must not allocate.
        Assert.Same(events, WalletExclusion.Events(events, false));
    }

    [Fact]
    public void Events_lose_every_free_company_chest_row_regardless_of_category()
    {
        var events = new[]
        {
            E(WalletKind.Self, 100, GilEventCategory.Misc),
            E(WalletKind.FreeCompanyChest, 50_000_000, GilEventCategory.FreeCompanyChestDeposit),
            // A chest payout claimed over IPC, and a chest withdrawal, which has no rule
            // at all and lands in Misc. A category-based filter would leak both.
            E(WalletKind.FreeCompanyChest, 900_000, GilEventCategory.SubmarineReturn),
            E(WalletKind.FreeCompanyChest, -400_000, GilEventCategory.Misc),
            E(WalletKind.Retainer, 7, GilEventCategory.RetainerSale),
        };

        var filtered = WalletExclusion.Events(events, true);

        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, e => Assert.NotEqual(WalletKind.FreeCompanyChest, e.Wallet.Kind));
    }

    [Fact]
    public void Events_return_the_original_list_when_there_was_no_chest_row_to_drop()
    {
        var events = new[] { E(WalletKind.Self, 100, GilEventCategory.Misc) };
        Assert.Same(events, WalletExclusion.Events(events, true));
    }

    [Fact]
    public void Wallets_drop_the_chest_and_the_totals_follow()
    {
        var wallets = new[]
        {
            new GilDelta.Wallet.Wallet(new WalletId(WalletKind.Self, ""), 52_191_519),
            new GilDelta.Wallet.Wallet(new WalletId(WalletKind.FreeCompanyChest, "FC"), 50_000_000),
        };

        Assert.Equal(102_191_519, WalletExclusion.Total(wallets));
        Assert.Equal(52_191_519, WalletExclusion.TotalExcludingFreeCompany(wallets));

        var filtered = WalletExclusion.Wallets(wallets, true);
        Assert.Single(filtered);
        Assert.Equal(52_191_519, WalletExclusion.Total(filtered));
    }

    [Fact]
    public void IsExcluded_only_ever_fires_for_the_chest()
    {
        Assert.True(WalletExclusion.IsExcluded(WalletKind.FreeCompanyChest, true));
        Assert.False(WalletExclusion.IsExcluded(WalletKind.FreeCompanyChest, false));
        Assert.False(WalletExclusion.IsExcluded(WalletKind.Self, true));
        Assert.False(WalletExclusion.IsExcluded(WalletKind.Retainer, true));
    }

    // ─── Wallets that could not be read ────────────────────────────────────────

    private static GilDelta.Wallet.Wallet Known(WalletKind kind, string id, long amount) =>
        new(new WalletId(kind, id), amount);

    private static GilDelta.Wallet.Wallet Unreadable(WalletKind kind, string id) =>
        new(new WalletId(kind, id), null);

    [Fact]
    public void Total_leaves_out_a_wallet_that_could_not_be_read()
    {
        var wallets = new[]
        {
            Known(WalletKind.Self, "", 1_000),
            Unreadable(WalletKind.FreeCompanyChest, "FC"),
        };

        // An unreadable chest adds nothing, exactly as when it arrived as 0, so the daily
        // settlement figures built on this do not move for existing users.
        Assert.Equal(1_000, WalletExclusion.Total(wallets));
        Assert.True(WalletExclusion.HasUnknown(wallets));
    }

    [Fact]
    public void TotalExcludingFreeCompany_leaves_out_a_wallet_that_could_not_be_read()
    {
        var wallets = new[]
        {
            Known(WalletKind.Self, "", 1_000),
            Unreadable(WalletKind.Retainer, "A"),
            Unreadable(WalletKind.FreeCompanyChest, "FC"),
        };

        Assert.Equal(1_000, WalletExclusion.TotalExcludingFreeCompany(wallets));
    }

    [Fact]
    public void HasUnknown_is_false_when_every_wallet_was_read()
    {
        var wallets = new[]
        {
            Known(WalletKind.Self, "", 1_000),
            Known(WalletKind.FreeCompanyChest, "FC", 0),
        };

        // A chest that genuinely holds 0 is known, not unknown - that distinction is the
        // point of the nullable amount.
        Assert.False(WalletExclusion.HasUnknown(wallets));
    }
}
