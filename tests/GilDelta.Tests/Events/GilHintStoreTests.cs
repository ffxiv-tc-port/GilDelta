using System.Collections.Generic;
using GilDelta.Events;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events;

public class GilHintStoreTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    /// <summary>A clock the test moves by hand, so ageing a hint costs no wall time.</summary>
    private sealed class FakeClock
    {
        public DateTimeOffset Now { get; set; } = T0;
        public DateTimeOffset Read() => Now;
        public void Advance(int ms) => Now = Now.AddMilliseconds(ms);
    }

    private static WalletDiff SelfSpend(long amount, DateTimeOffset at) =>
        new(new WalletId(WalletKind.Self, ""), amount, 0, at);

    [Fact]
    public void An_unexpired_hint_of_the_right_kind_is_consumed()
    {
        var clock = new FakeClock();
        var store = new GilHintStore(clock.Read);

        Assert.True(store.Submit("Teleport", "Lifestream:gridania", 10_000));
        clock.Advance(500);

        Assert.True(store.TryConsume(SelfSpend(25, clock.Now), out var hint));
        Assert.Equal(GilEventCategory.Teleport, hint.Category);
        Assert.Equal("Lifestream:gridania", hint.Note);
    }

    [Fact]
    public void A_consumed_hint_cannot_be_consumed_twice()
    {
        var clock = new FakeClock();
        var store = new GilHintStore(clock.Read);
        store.Submit("Teleport", "Lifestream:gridania", 10_000);

        Assert.True(store.TryConsume(SelfSpend(25, clock.Now), out _));
        Assert.False(store.TryConsume(SelfSpend(25, clock.Now), out _));
        Assert.Equal(0, store.ActiveCount);
    }

    [Fact]
    public void An_expired_hint_is_not_consumed()
    {
        var clock = new FakeClock();
        var store = new GilHintStore(clock.Read);

        Assert.True(store.Submit("Teleport", "Lifestream:gridania", 1_000));
        clock.Advance(1_500);

        Assert.False(store.TryConsume(SelfSpend(25, clock.Now), out _));
        Assert.Equal(0, store.ActiveCount);  // and it was dropped, not left lying around
    }

    [Fact]
    public void An_unknown_category_is_rejected_and_reported_once()
    {
        var log = new List<string>();
        var store = new GilHintStore(log: log.Add);

        Assert.False(store.Submit("Groceries", "SomePlugin:oops", 10_000));
        Assert.False(store.Submit("Groceries", "SomePlugin:oops", 10_000));
        Assert.False(store.Submit("Groceries", "SomePlugin:oops", 10_000));

        Assert.Single(log);
        Assert.Contains("Groceries", log[0]);
        // The diagnostic has to say what *would* have worked.
        Assert.Contains("Teleport", log[0]);
    }

    [Fact]
    public void A_second_unknown_category_gets_its_own_line()
    {
        var log = new List<string>();
        var store = new GilHintStore(log: log.Add);

        store.Submit("Groceries", "", 10_000);
        store.Submit("Rent", "", 10_000);

        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void Category_names_are_matched_case_insensitively()
    {
        var store = new GilHintStore();
        Assert.True(store.Submit("teleport", "x", 10_000));
        Assert.True(store.Submit("SUBMARINERETURN", "x", 10_000));
    }

    [Fact]
    public void A_numeric_category_is_rejected_rather_than_parsed_as_an_ordinal()
    {
        // Enum.TryParse would happily turn "3" into RetainerDeposit, which is how a
        // consumer's stray number becomes a confident misclassification.
        var store = new GilHintStore();
        Assert.False(store.Submit("3", "x", 10_000));
        Assert.False(store.Submit("-1", "x", 10_000));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_category_is_rejected(string? category)
    {
        var store = new GilHintStore();
        Assert.False(store.Submit(category!, "x", 10_000));
    }

    [Fact]
    public void A_non_positive_ttl_is_rejected()
    {
        var store = new GilHintStore();
        Assert.False(store.Submit("Teleport", "x", 0));
        Assert.False(store.Submit("Teleport", "x", -1));
    }

    [Fact]
    public void A_ttl_beyond_the_cap_is_clamped_not_rejected()
    {
        var clock = new FakeClock();
        var store = new GilHintStore(clock.Read);

        Assert.True(store.Submit("Teleport", "x", int.MaxValue));

        clock.Advance(GilHintStore.MaxTtlMs + 1);
        Assert.False(store.TryConsume(SelfSpend(25, clock.Now), out _));
    }

    [Fact]
    public void A_null_note_is_accepted_and_stored_as_empty()
    {
        var store = new GilHintStore();
        Assert.True(store.Submit("Teleport", null!, 10_000));
        Assert.True(store.TryConsume(SelfSpend(25, T0), out var hint));
        Assert.Equal(string.Empty, hint.Note);
    }

    [Fact]
    public void A_hint_only_matches_a_wallet_its_category_can_apply_to()
    {
        var store = new GilHintStore();
        store.Submit("SubmarineReturn", "AutoRetainer:sub", 10_000);

        var selfGain = new WalletDiff(new WalletId(WalletKind.Self, ""), 0, 500_000, T0);
        Assert.False(store.TryConsume(selfGain, out _));

        var chestGain = new WalletDiff(new WalletId(WalletKind.FreeCompanyChest, "FC"), 0, 500_000, T0);
        Assert.True(store.TryConsume(chestGain, out _));
    }

    [Fact]
    public void A_spend_hint_does_not_claim_an_income_change()
    {
        // The teleport fee never arrived, but the retainer payout did. Without the
        // direction check the payout would be filed as a teleport.
        var store = new GilHintStore();
        store.Submit("Teleport", "Lifestream:gridania", 10_000);

        var selfGain = new WalletDiff(new WalletId(WalletKind.Self, ""), 0, 412_800, T0);
        Assert.False(store.TryConsume(selfGain, out _));
        Assert.Equal(1, store.ActiveCount);  // still waiting for its own change
    }

    [Fact]
    public void Hints_are_consumed_oldest_first()
    {
        var clock = new FakeClock();
        var store = new GilHintStore(clock.Read);

        store.Submit("Teleport", "first", 60_000);
        clock.Advance(100);
        store.Submit("Teleport", "second", 60_000);

        Assert.True(store.TryConsume(SelfSpend(25, clock.Now), out var a));
        Assert.True(store.TryConsume(SelfSpend(25, clock.Now), out var b));
        Assert.Equal("first", a.Note);
        Assert.Equal("second", b.Note);
    }

    [Fact]
    public void Submissions_stop_being_accepted_once_the_pending_cap_is_reached()
    {
        // Nothing drains while no character is loaded; a looping consumer must not be
        // able to grow the queue without bound.
        var store = new GilHintStore();
        for (var i = 0; i < GilHintStore.MaxPendingHints; i++)
            Assert.True(store.Submit("Teleport", "x", 60_000));

        Assert.False(store.Submit("Teleport", "x", 60_000));
    }

    [Fact]
    public void Draining_frees_the_pending_budget_again()
    {
        var store = new GilHintStore();
        for (var i = 0; i < GilHintStore.MaxPendingHints; i++)
            store.Submit("Teleport", "x", 60_000);

        store.Drain();

        Assert.True(store.Submit("Teleport", "x", 60_000));
        // Drain also trims the active list back to its own cap.
        Assert.Equal(GilHintStore.MaxActiveHints, store.ActiveCount);
    }

    [Fact]
    public void AcceptedCategories_lists_every_enum_name()
    {
        Assert.Equal(
            Enum.GetNames<GilEventCategory>().Length,
            GilHintStore.AcceptedCategories.Count);
        Assert.Contains("Teleport", GilHintStore.AcceptedCategories);
    }

    [Fact]
    public void Draining_drops_only_the_expired_hints_and_keeps_the_rest_in_order()
    {
        var clock = new FakeClock();
        var store = new GilHintStore(clock.Read);
        store.Submit("Misc", "short-1", 1_000);
        store.Submit("Misc", "long-1", 60_000);
        store.Submit("Misc", "short-2", 1_000);
        store.Submit("Misc", "long-2", 60_000);
        store.Drain();
        Assert.Equal(4, store.ActiveCount);
        clock.Advance(1_500);

        store.Drain();

        Assert.Equal(2, store.ActiveCount);
        Assert.True(store.TryConsume(SelfSpend(25, clock.Now), out var first));
        Assert.Equal("long-1", first.Note);
        Assert.True(store.TryConsume(SelfSpend(25, clock.Now), out var second));
        Assert.Equal("long-2", second.Note);
    }

    [Fact]
    public void A_hint_expires_on_the_instant_it_names_not_after_it()
    {
        var clock = new FakeClock();
        var store = new GilHintStore(clock.Read);
        store.Submit("Misc", "edge", 1_000);
        store.Drain();
        clock.Advance(1_000);

        store.Drain();

        Assert.Equal(0, store.ActiveCount);
    }
}
