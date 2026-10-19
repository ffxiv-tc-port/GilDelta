using GilDelta.Wallet;

namespace GilDelta.Tests.Wallet;

/// <summary>
/// Telling "the free company chest holds nothing" apart from "the server has not told us
/// what the chest holds" - the judgement that decides whether a 0 may be diffed.
/// 🔴 Every test here passes <c>containerLoaded: true</c> unless it is specifically
/// about the flag: that is the real measured condition, and a gate that only works when
/// the flag goes false is a gate that does not work.
/// </summary>
public class FreeCompanyChestGateTests
{
    private const string Fc = "蜥呱重工";
    private const string OtherFc = "Another Company";

    [Fact]
    public void Away_from_the_chest_a_zero_is_not_a_balance()
    {
        var gate = new FreeCompanyChestGate();

        var figure = gate.Read(Fc, containerLoaded: true, rawGil: 0);

        Assert.Null(figure.Amount);
        Assert.False(figure.IsKnown);
        Assert.Equal(ChestFigureState.FigureNotSentYet, figure.State);
    }

    [Fact]
    public void A_figure_the_server_sent_is_reported_as_is()
    {
        var gate = new FreeCompanyChestGate();

        var figure = gate.Read(Fc, containerLoaded: true, rawGil: 50_000_000);

        Assert.Equal(50_000_000, figure.Amount);
        Assert.Equal(ChestFigureState.Readable, figure.State);
        Assert.True(gate.HasSeenFigure(Fc));
    }

    [Fact]
    public void A_zero_is_a_genuine_empty_chest_once_the_server_has_spoken()
    {
        // The case a "treat every 0 as unknown" rule would get wrong: the player empties
        // the chest while standing at it. That 0 is real and has to stay diffable.
        var gate = new FreeCompanyChestGate();
        gate.Read(Fc, containerLoaded: true, rawGil: 50_000_000);

        var figure = gate.Read(Fc, containerLoaded: true, rawGil: 0);

        Assert.Equal(0, figure.Amount);
        Assert.True(figure.IsKnown);
        Assert.Equal(ChestFigureState.Readable, figure.State);
    }

    [Fact]
    public void An_unloaded_container_is_not_read_at_all()
    {
        var gate = new FreeCompanyChestGate();

        var figure = gate.Read(Fc, containerLoaded: false, rawGil: 50_000_000);

        Assert.Null(figure.Amount);
        Assert.Equal(ChestFigureState.ContainerNotLoaded, figure.State);
        // A figure read out of a container that is not loaded proves nothing about the
        // company, so it must not make a later 0 trustworthy either.
        Assert.False(gate.HasSeenFigure(Fc));
    }

    [Fact]
    public void Reset_forgets_that_the_server_has_spoken()
    {
        // 🔴 A character swap. Keeping the memory would make the next character's opening
        // placeholder 0 look like a real empty chest - the bug, one character removed.
        var gate = new FreeCompanyChestGate();
        gate.Read(Fc, containerLoaded: true, rawGil: 50_000_000);
        Assert.Equal(1, gate.ProvenCount);

        gate.Reset();

        Assert.Equal(0, gate.ProvenCount);
        Assert.False(gate.HasSeenFigure(Fc));
        Assert.Null(gate.Read(Fc, containerLoaded: true, rawGil: 0).Amount);
    }

    [Fact]
    public void Companies_are_proven_one_at_a_time()
    {
        var gate = new FreeCompanyChestGate();
        gate.Read(Fc, containerLoaded: true, rawGil: 50_000_000);

        // Seeing one company's chest says nothing about another company's.
        Assert.Null(gate.Read(OtherFc, containerLoaded: true, rawGil: 0).Amount);
        Assert.Equal(0, gate.Read(Fc, containerLoaded: true, rawGil: 0).Amount);
    }

    // --- The gate and the baseline together ---------------------------------
    //
    // What actually matters is whether a recorded event comes out the far end, so these
    // drive the real WalletBaseline with the readings the gate produces.

    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-09-10T01:55:00+08:00");

    private static WalletDiff? Feed(
        FreeCompanyChestGate gate, WalletBaseline baseline, long rawGil, int second)
    {
        var figure = gate.Read(Fc, containerLoaded: true, rawGil);
        var wallet = new GilDelta.Wallet.Wallet(new WalletId(WalletKind.FreeCompanyChest, Fc), figure.Amount);
        return baseline.Observe(wallet, T0.AddSeconds(second));
    }

    /// <summary>
    /// Regression test for the recurrence: an <c>IsLoaded</c>-only gate never says
    /// "unknown" on this client, and the fabricated deposits arrived with the chest
    /// window already open - which is why "gate on the addon" would not catch them.
    /// </summary>
    [Fact]
    public void The_fabricated_50m_deposit_does_not_come_back_even_with_the_loaded_flag_true()
    {
        var gate = new FreeCompanyChestGate();
        var baseline = new WalletBaseline();

        // Logged in, away from the chest: the container is loaded and answers 0.
        Assert.Null(Feed(gate, baseline, 0, 0));
        Assert.Null(Feed(gate, baseline, 0, 1));
        Assert.Null(baseline.BaselineOf(new WalletId(WalletKind.FreeCompanyChest, Fc)));

        // Opens the chest; a moment later the real balance arrives. This is the exact
        // transition that must never be booked as a deposit.
        Assert.Null(Feed(gate, baseline, 50_000_000, 2));
        Assert.Null(Feed(gate, baseline, 50_000_000, 3));
    }

    [Fact]
    public void A_deposit_made_at_the_chest_is_still_recorded()
    {
        var gate = new FreeCompanyChestGate();
        var baseline = new WalletBaseline();

        Feed(gate, baseline, 0, 0);                 // away from the chest
        Feed(gate, baseline, 50_000_000, 1);        // balance arrives, seeds the baseline

        var diff = Feed(gate, baseline, 50_500_000, 2);

        Assert.NotNull(diff);
        Assert.Equal(500_000, diff!.Value.Delta);
    }

    [Fact]
    public void Emptying_the_chest_at_the_chest_is_recorded_as_a_withdrawal()
    {
        // The 0 here is real, and losing it would mean the one case where the chest
        // legitimately reads 0 silently produced no record.
        var gate = new FreeCompanyChestGate();
        var baseline = new WalletBaseline();

        Feed(gate, baseline, 50_000_000, 0);

        var diff = Feed(gate, baseline, 0, 1);

        Assert.NotNull(diff);
        Assert.Equal(-50_000_000, diff!.Value.Delta);
    }

    [Fact]
    public void A_swap_to_another_character_in_the_same_company_fabricates_nothing()
    {
        // 🔴 The reset path, end to end: WalletWatcher.Reset clears both of these, so the
        // second character's placeholder 0 must not be diffable against what the first
        // character saw.
        var gate = new FreeCompanyChestGate();
        var baseline = new WalletBaseline();

        Feed(gate, baseline, 50_000_000, 0);

        gate.Reset();
        baseline.Clear();

        Assert.Null(Feed(gate, baseline, 0, 60));            // second character, away
        Assert.Null(Feed(gate, baseline, 50_000_000, 61));   // walks to the chest
    }

    [Fact]
    public void Going_unreadable_and_back_to_the_same_figure_records_nothing()
    {
        var gate = new FreeCompanyChestGate();
        var baseline = new WalletBaseline();
        var id = new WalletId(WalletKind.FreeCompanyChest, Fc);

        Feed(gate, baseline, 50_000_000, 0);

        // An unreadable reading in the middle: the container drops out, so not even the
        // gate's session memory can vouch for the figure.
        var unreadable = gate.Read(Fc, containerLoaded: false, rawGil: 0);
        Assert.Null(baseline.Observe(new GilDelta.Wallet.Wallet(id, unreadable.Amount), T0.AddSeconds(1)));
        Assert.Null(baseline.BaselineOf(id));

        Assert.Null(Feed(gate, baseline, 50_000_000, 2));
    }
}
