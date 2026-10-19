using GilDelta.Events;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events;

public class CharacterTransferLedgerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-09-16T21:26:00+08:00");

    private static GilEvent Transfer(long amount) =>
        new(T0, new WalletId(WalletKind.Self, ""), amount,
            GilEventCategory.CharacterTransfer, "rule=CharacterTransferRule") { ContentId = 7 };

    [Fact]
    public void A_transfer_is_never_booked_into_a_day()
    {
        var ledger = new DailyLedger();
        ledger.Add(Transfer(1_000_000));

        Assert.False(ledger.TryGet(7, DailyLedger.LocalDayOf(T0), out _));
    }

    [Fact]
    public void Retract_refuses_a_transfer_so_a_day_cannot_go_negative()
    {
        var ledger = new DailyLedger();
        var booked = new GilEvent(T0, new WalletId(WalletKind.Self, ""), 500,
            GilEventCategory.Misc, "m") { ContentId = 7 };
        ledger.Add(booked);

        Assert.False(ledger.Retract(Transfer(1_000_000)));
        Assert.True(ledger.TryGet(7, DailyLedger.LocalDayOf(T0), out var row));
        Assert.Equal(500, row.Net);
        Assert.Equal(1, row.EventCount);
    }

    [Fact]
    public void Backfill_skips_transfers()
    {
        var ledger = new DailyLedger();
        var touched = ledger.BackfillFromEvents(
            new[] { Transfer(1_000_000) }, DailyLedger.LocalDayOf(T0));

        Assert.Empty(touched);
        Assert.False(ledger.TryGet(7, DailyLedger.LocalDayOf(T0), out _));
    }
}
