using System;

namespace GilDelta.Wallet;

/// <summary>
/// Remembers, per slot, which string a raw name field last decoded to, so a name that has
/// not changed costs a byte comparison instead of a new string on every pass.
/// 🔴 Holds managed copies only. The key is the bytes themselves, never the slot alone:
/// a renamed retainer, or another retainer moving into the slot, has to miss.
/// </summary>
public sealed class NameMemo
{
    private readonly byte[][] _raw;
    private readonly string?[] _text;

    /// <summary>Bytes remembered per slot; -1 = nothing remembered.</summary>
    private readonly int[] _length;

    /// <param name="slots">How many independent names are tracked.</param>
    /// <param name="capacity">The longest name field, in bytes, that can be remembered.</param>
    public NameMemo(int slots, int capacity)
    {
        _raw = new byte[slots][];
        _text = new string?[slots];
        _length = new int[slots];
        for (var i = 0; i < slots; i++)
        {
            _raw[i] = new byte[capacity];
            _length[i] = -1;
        }
    }

    /// <param name="field">The whole fixed-size field; the name is what precedes its first NUL.</param>
    /// <returns>
    /// false when this is not the name remembered for the slot - and always for a field with
    /// no NUL in it, because the decode this stands in for does not stop at the field's end.
    /// </returns>
    public bool TryGet(int slot, ReadOnlySpan<byte> field, out string text)
    {
        text = string.Empty;
        if ((uint)slot >= (uint)_length.Length) return false;

        var length = field.IndexOf((byte)0);
        if (length < 0 || length != _length[slot]) return false;
        if (!field[..length].SequenceEqual(_raw[slot].AsSpan(0, length))) return false;

        text = _text[slot]!;
        return true;
    }

    /// <summary>Records what <paramref name="field"/> decoded to. Ignores what cannot be keyed.</summary>
    public void Remember(int slot, ReadOnlySpan<byte> field, string text)
    {
        if ((uint)slot >= (uint)_length.Length) return;

        var length = field.IndexOf((byte)0);
        if (length < 0 || length > _raw[slot].Length)
        {
            _length[slot] = -1;
            return;
        }

        field[..length].CopyTo(_raw[slot]);
        _length[slot] = length;
        _text[slot] = text;
    }
}
