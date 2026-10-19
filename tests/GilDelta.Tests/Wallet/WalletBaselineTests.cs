using GilDelta.Wallet;

namespace GilDelta.Tests.Wallet;

/// <summary>
/// The rule that decides whether a wallet reading becomes a recorded event.
/// 🔴 The invariant under test: a diff is only ever emitted between two consecutive
/// KNOWN readings of the same wallet. Everything else here is a corollary of that.
/// </summary>
public class WalletBaselineTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-09-09T00:49:59+08:00");

    private static readonly WalletId Chest = new(WalletKind.FreeCompanyChest, "FC");
    private static readonly WalletId Self  = new(WalletKind.Self, "");

    private static GilDelta.Wallet.Wallet Known(WalletId id, long amount) => new(id, amount);
    private static GilDelta.Wallet.Wallet Unknown(WalletId id) => new(id, null);

    [Fact]
    public void An_unreadable_reading_never_produces_a_diff()
    {
        var baseline = new WalletBaseline();
        Assert.Null(baseline.Observe(Unknown(Chest), T0));
        Assert.Null(baseline.Observe(Unknown(Chest), T0.AddSeconds(1)));
    }

    [Fact]
    public void The_first_known_reading_only_seeds_the_baseline()
    {
        var baseline = new WalletBaseline();
        Assert.Null(baseline.Observe(Known(Self, 1_000), T0));
        Assert.Equal(1_000, baseline.BaselineOf(Self));
    }

    [Fact]
    public void A_change_between_two_known_readings_is_reported()
    {
        var baseline = new WalletBaseline();
        baseline.Observe(Known(Self, 1_000), T0);

        var diff = baseline.Observe(Known(Self, 1_500), T0.AddSeconds(1));

        Assert.NotNull(diff);
        Assert.Equal(500, diff!.Value.Delta);
        Assert.Equal(1_000, diff.Value.Before);
        Assert.Equal(1_500, diff.Value.After);
        Assert.Equal(Self, diff.Value.Id);
    }

    [Fact]
    public void A_known_reading_that_did_not_move_reports_nothing()
    {
        var baseline = new WalletBaseline();
        baseline.Observe(Known(Self, 1_000), T0);
        Assert.Null(baseline.Observe(Known(Self, 1_000), T0.AddSeconds(1)));
    }

    [Fact]
    public void An_unreadable_reading_drops_the_baseline_so_the_next_one_reseeds_silently()
    {
        // The whole point of dropping rather than holding: a change that happened while
        // the wallet was invisible must not surface later with the wrong timestamp.
        var baseline = new WalletBaseline();
        baseline.Observe(Known(Chest, 50_000_000), T0);

        Assert.Null(baseline.Observe(Unknown(Chest), T0.AddMinutes(1)));
        Assert.Null(baseline.BaselineOf(Chest));

        Assert.Null(baseline.Observe(Known(Chest, 60_000_000), T0.AddMinutes(2)));
        Assert.Equal(60_000_000, baseline.BaselineOf(Chest));
    }

    [Fact]
    public void Going_unreadable_and_back_to_the_same_value_reports_nothing()
    {
        var baseline = new WalletBaseline();
        baseline.Observe(Known(Chest, 50_000_000), T0);
        baseline.Observe(Unknown(Chest), T0.AddMinutes(1));

        Assert.Null(baseline.Observe(Known(Chest, 50_000_000), T0.AddMinutes(2)));
    }

    [Fact]
    public void Wallets_are_tracked_independently()
    {
        var baseline = new WalletBaseline();
        baseline.Observe(Known(Self, 1_000), T0);
        baseline.Observe(Unknown(Chest), T0);

        // The chest being unreadable must not disturb the character's own purse.
        var diff = baseline.Observe(Known(Self, 900), T0.AddSeconds(1));
        Assert.NotNull(diff);
        Assert.Equal(-100, diff!.Value.Delta);
    }

    [Fact]
    public void Clear_forgets_every_baseline()
    {
        var baseline = new WalletBaseline();
        baseline.Observe(Known(Self, 1_000), T0);
        Assert.Equal(1, baseline.Count);

        baseline.Clear();

        Assert.Equal(0, baseline.Count);
        Assert.Null(baseline.Observe(Known(Self, 9_999), T0.AddSeconds(1)));
    }

    /// <summary>
    /// Regression test: a fabricated
    /// <c>FreeCompanyChest</c> "deposit" with no matching debit anywhere - the chest
    /// reading flipping from an unreadable 0 to its real balance the moment the player
    /// walked into the company house.
    /// </summary>
    [Fact]
    public void The_free_company_chest_zero_that_faked_a_50m_deposit_is_not_an_event_any_more()
    {
        var baseline = new WalletBaseline();

        // Away from the chest: the game reports nothing usable. A literal 0 here would
        // become the baseline.
        Assert.Null(baseline.Observe(Unknown(Chest), T0));
        Assert.Null(baseline.Observe(Unknown(Chest), T0.AddMinutes(5)));

        // Walks into the company house; the real balance shows up.
        Assert.Null(baseline.Observe(Known(Chest, 50_000_000), T0.AddMinutes(6)));

        // ...and a genuine deposit made while standing there is still reported.
        var real = baseline.Observe(Known(Chest, 50_500_000), T0.AddMinutes(7));
        Assert.NotNull(real);
        Assert.Equal(500_000, real!.Value.Delta);
    }
}
