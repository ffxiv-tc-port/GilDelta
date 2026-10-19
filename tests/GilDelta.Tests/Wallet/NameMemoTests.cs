using System.Text;
using GilDelta.Wallet;

namespace GilDelta.Tests.Wallet;

/// <summary>
/// The memo stands in for a decode that runs every frame. It may only ever answer with
/// the string those exact bytes decode to: a wallet is identified by its name, so a
/// wrong answer here files gil under the wrong wallet.
/// </summary>
public class NameMemoTests
{
    /// <summary>A fixed-size name field as the game holds it: UTF-8, NUL padded.</summary>
    private static byte[] Field(string name, int size = 32)
    {
        var field = new byte[size];
        Encoding.UTF8.GetBytes(name, field);
        return field;
    }

    [Fact]
    public void Nothing_is_remembered_to_begin_with()
    {
        var memo = new NameMemo(slots: 2, capacity: 64);

        Assert.False(memo.TryGet(0, Field("Bob"), out _));
    }

    [Fact]
    public void The_same_bytes_give_back_the_very_string_that_was_remembered()
    {
        var memo = new NameMemo(slots: 2, capacity: 64);
        var text = new string("僱員甲".ToCharArray());
        memo.Remember(0, Field("僱員甲"), text);

        Assert.True(memo.TryGet(0, Field("僱員甲"), out var again));
        Assert.Same(text, again);
    }

    [Fact]
    public void A_rename_of_the_same_length_is_a_miss()
    {
        var memo = new NameMemo(slots: 2, capacity: 64);
        memo.Remember(0, Field("Alice"), "Alice");

        Assert.False(memo.TryGet(0, Field("Alicf"), out _));
    }

    [Fact]
    public void A_longer_or_shorter_name_is_a_miss()
    {
        var memo = new NameMemo(slots: 2, capacity: 64);
        memo.Remember(0, Field("Alice"), "Alice");

        Assert.False(memo.TryGet(0, Field("Alic"), out _));
        Assert.False(memo.TryGet(0, Field("Alicee"), out _));
    }

    [Fact]
    public void Slots_do_not_answer_for_each_other()
    {
        var memo = new NameMemo(slots: 2, capacity: 64);
        memo.Remember(0, Field("Alice"), "Alice");

        Assert.False(memo.TryGet(1, Field("Alice"), out _));
    }

    [Fact]
    public void Another_name_moving_into_a_slot_replaces_what_was_remembered()
    {
        var memo = new NameMemo(slots: 2, capacity: 64);
        memo.Remember(0, Field("Alice"), "Alice");
        memo.Remember(0, Field("Bob"), "Bob");

        Assert.False(memo.TryGet(0, Field("Alice"), out _));
        Assert.True(memo.TryGet(0, Field("Bob"), out var name));
        Assert.Equal("Bob", name);
    }

    [Fact]
    public void Bytes_after_the_terminator_are_not_part_of_the_name()
    {
        var memo = new NameMemo(slots: 1, capacity: 64);
        memo.Remember(0, Field("Alice"), "Alice");

        var leftover = Field("Alice");
        leftover[20] = (byte)'x';

        Assert.True(memo.TryGet(0, leftover, out var name));
        Assert.Equal("Alice", name);
    }

    [Fact]
    public void An_empty_name_is_remembered_like_any_other()
    {
        var memo = new NameMemo(slots: 1, capacity: 64);
        memo.Remember(0, Field(""), "");

        Assert.True(memo.TryGet(0, Field(""), out var name));
        Assert.Equal("", name);
        Assert.False(memo.TryGet(0, Field("A"), out _));
    }

    [Fact]
    public void A_field_with_no_terminator_is_never_answered_from_memory()
    {
        // The decode this replaces reads on past the end of such a field, so the field's
        // own bytes do not determine the result and cannot be used as the key.
        var memo = new NameMemo(slots: 1, capacity: 64);
        var full = new byte[32];
        Array.Fill(full, (byte)'A');

        memo.Remember(0, full, new string('A', 32));

        Assert.False(memo.TryGet(0, full, out _));
    }

    [Fact]
    public void Failing_to_remember_also_forgets_what_the_slot_held()
    {
        var memo = new NameMemo(slots: 1, capacity: 64);
        memo.Remember(0, Field("Alice"), "Alice");

        var full = new byte[32];
        Array.Fill(full, (byte)'A');
        memo.Remember(0, full, new string('A', 32));

        Assert.False(memo.TryGet(0, Field("Alice"), out _));
    }

    [Fact]
    public void A_name_longer_than_the_capacity_is_simply_not_remembered()
    {
        var memo = new NameMemo(slots: 1, capacity: 4);
        memo.Remember(0, Field("Alice"), "Alice");

        Assert.False(memo.TryGet(0, Field("Alice"), out _));
    }

    [Fact]
    public void A_slot_outside_the_table_is_a_miss_rather_than_an_error()
    {
        var memo = new NameMemo(slots: 1, capacity: 64);
        memo.Remember(5, Field("Alice"), "Alice");
        memo.Remember(-1, Field("Alice"), "Alice");

        Assert.False(memo.TryGet(5, Field("Alice"), out _));
        Assert.False(memo.TryGet(-1, Field("Alice"), out _));
    }
}
